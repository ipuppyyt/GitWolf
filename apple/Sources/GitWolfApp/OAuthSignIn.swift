import AppKit
import GitWolfCore
import Network
import Observation

/// OAuth application client IDs. These are public identifiers (no secret), but they belong to an
/// application registered on each host, so GitWolf ships none by default. Maintainers can fill in
/// `builtIn` for github.com / gitlab.com / codeberg.org; everything else (GitHub Enterprise, GitLab
/// self-managed, other Gitea servers) uses a client ID the user registered, remembered per host.
enum OAuthClients {
    static let builtIn: [String: String] = [
        "github.com": "Ov23lir62T7trMJh2u2v",
    ]
    private static let key = "oauthClientIDs"

    static func clientID(host: String) -> String? {
        if let id = builtIn[host], !id.isEmpty { return id }
        return saved()[host].flatMap { $0.isEmpty ? nil : $0 }
    }

    static func hasBuiltIn(host: String) -> Bool { !(builtIn[host] ?? "").isEmpty }

    static func remember(_ id: String, host: String) {
        var all = saved()
        all[host] = id
        UserDefaults.standard.set(all, forKey: key)
    }

    private static func saved() -> [String: String] {
        UserDefaults.standard.dictionary(forKey: key) as? [String: String] ?? [:]
    }

    /// What to enter when registering an application on the provider.
    static func setupHint(_ kind: ProviderKind, host: String) -> String {
        switch kind {
        case .github:
            "On \(host): Settings → Developer settings → OAuth Apps → New. Tick “Enable Device Flow”, then paste the Client ID."
        case .gitlab:
            "On \(host): Preferences → Applications. Redirect URI \(OAuthLoopback.redirectURI), untick “Confidential”, scopes read_api, read_repository, write_repository."
        case .gitea:
            "On \(host): Settings → Applications → Manage OAuth2 applications. Redirect URI \(OAuthLoopback.redirectURI), untick “Confidential client”."
        case .bitbucket:
            "On bitbucket.org: Workspace settings → OAuth consumers → Add consumer. Callback URL \(OAuthLoopback.redirectURI), tick “This is a private consumer”, permissions Account (read), Repositories (read, write), Pull requests (read). Paste the Key here and the Secret below."
        case .azure:
            "In the Microsoft Entra admin center: App registrations → New registration. Add a “Mobile and desktop applications” redirect URI \(OAuthLoopback.redirectURI), turn on “Allow public client flows”, and under API permissions add Azure DevOps → user_impersonation. Paste the Application (client) ID."
        }
    }
}

/// A one-shot HTTP listener on 127.0.0.1 that catches the browser's redirect and returns the code.
final class LoopbackServer: @unchecked Sendable {
    private let listener: NWListener
    private let queue = DispatchQueue(label: "gitwolf.oauth.loopback")
    private var continuation: CheckedContinuation<String, Error>?
    private var finished = false

    struct Failure: LocalizedError {
        let errorDescription: String?
    }

    init() throws {
        let params = NWParameters.tcp
        params.requiredInterfaceType = .loopback
        guard let port = NWEndpoint.Port(rawValue: OAuthLoopback.port) else { throw Failure(errorDescription: "Invalid port.") }
        do { listener = try NWListener(using: params, on: port) } catch {
            throw Failure(errorDescription: "Port \(OAuthLoopback.port) is busy, so sign-in can’t listen for the browser. Close the other sign-in window and try again.")
        }
    }

    /// Starts listening; resolves with the authorization code once the browser comes back.
    func waitForCode(state: String) async throws -> String {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (cont: CheckedContinuation<String, Error>) in
                queue.async {
                    self.continuation = cont
                    self.listener.newConnectionHandler = { [weak self] connection in self?.handle(connection, state: state) }
                    self.listener.start(queue: self.queue)
                    self.queue.asyncAfter(deadline: .now() + 300) { self.finish(.failure(OAuthError.expired)) }
                }
            }
        } onCancel: {
            queue.async { self.finish(.failure(CancellationError())) }
        }
    }

    private func handle(_ connection: NWConnection, state: String) {
        connection.start(queue: queue)
        connection.receive(minimumIncompleteLength: 1, maximumLength: 8192) { [weak self] data, _, _, _ in
            guard let self, let data, let request = String(data: data, encoding: .utf8),
                  let line = request.split(separator: "\r\n").first else { connection.cancel(); return }
            let parts = line.split(separator: " ")
            let comps = parts.count > 1 ? URLComponents(string: String(parts[1])) : nil
            guard comps?.path == "/callback" else {
                connection.send(content: Self.response("Not found", status: "404 Not Found"), completion: .contentProcessed { _ in connection.cancel() })
                return
            }
            let items = Dictionary((comps?.queryItems ?? []).map { ($0.name, $0.value ?? "") }, uniquingKeysWith: { a, _ in a })
            let result: Result<String, Error>
            if items["state"] != state {
                result = .failure(OAuthError.server("The sign-in response didn’t match the request. Try again."))
            } else if let code = items["code"], !code.isEmpty {
                result = .success(code)
            } else {
                result = .failure(items["error"] == "access_denied" ? OAuthError.denied : OAuthError.server(items["error_description"] ?? "Sign-in failed."))
            }
            let ok: Bool
            if case .success = result { ok = true } else { ok = false }
            let body = ok ? "Signed in. You can close this tab and return to GitWolf." : "Sign-in failed. You can close this tab and try again in GitWolf."
            connection.send(content: Self.response(body), completion: .contentProcessed { _ in
                connection.cancel()
                self.finish(result)
            })
        }
    }

    private func finish(_ result: Result<String, Error>) {
        guard !finished else { return }
        finished = true
        listener.cancel()
        continuation?.resume(with: result)
        continuation = nil
    }

    private static func response(_ text: String, status: String = "200 OK") -> Data {
        let html = "<!doctype html><meta charset=utf-8><title>GitWolf</title><body style=\"font:16px -apple-system,sans-serif;padding:48px;text-align:center\">\(text)</body>"
        return Data("HTTP/1.1 \(status)\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: \(html.utf8.count)\r\nConnection: close\r\n\r\n\(html)".utf8)
    }
}

/// Drives a browser sign-in for one provider account.
@MainActor @Observable
final class OAuthSession {
    enum Phase: Equatable {
        case idle
        case device(code: String, url: URL)
        case browser
        case finishing
    }

    private(set) var phase = Phase.idle
    private var task: Task<Void, Never>?

    var isActive: Bool { phase != .idle }

    func start(account: ProviderAccount, clientID: String, clientSecret: String? = nil,
               done: @escaping @MainActor (ProviderAccount, OAuthTokens) -> Void,
               failed: @escaping @MainActor (String) -> Void) {
        guard let flow = account.kind.oauthFlow else { return }
        let client = OAuthClient(kind: account.kind, origin: account.origin, clientID: clientID, clientSecret: clientSecret)
        task = Task {
            do {
                let tokens: OAuthTokens
                switch flow {
                case .device:
                    let device = try await client.startDeviceFlow()
                    phase = .device(code: device.userCode, url: device.verificationURL)
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(device.userCode, forType: .string)
                    NSWorkspace.shared.open(device.verificationURL)
                    tokens = try await client.pollDeviceFlow(device)
                case .pkce:
                    let pkce = PKCE.generate()
                    let state = UUID().uuidString
                    let server = try LoopbackServer()
                    guard let url = client.authorizationURL(redirectURI: OAuthLoopback.redirectURI, state: state, challenge: pkce.challenge)
                    else { throw OAuthError.invalidResponse }
                    phase = .browser
                    async let code = server.waitForCode(state: state)
                    NSWorkspace.shared.open(url)
                    let received = try await code
                    phase = .finishing
                    tokens = try await client.exchange(code: received, verifier: pkce.verifier, redirectURI: OAuthLoopback.redirectURI)
                }
                phase = .finishing
                done(account, tokens)
            } catch is CancellationError {
                phase = .idle
            } catch {
                phase = .idle
                failed(error.localizedDescription)
            }
        }
    }

    func cancel() {
        task?.cancel()
        task = nil
        phase = .idle
    }
}
