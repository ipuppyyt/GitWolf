import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public struct RemoteRepository: Identifiable, Hashable, Sendable {
    public var id: String { fullName }
    public var fullName: String
    public var description: String
    public var isPrivate: Bool
    public var httpsURL: String
    public var sshURL: String
    public var updated: Date?

    public var name: String { fullName.split(separator: "/").last.map(String.init) ?? fullName }
}

public enum ProviderError: Error, LocalizedError, Sendable {
    case unauthorized
    case http(Int)
    /// A rejected request with the server's own explanation (for example "a pull request already exists").
    case api(Int, String)
    /// A write the server refused for lack of permission (401/403), with its own explanation. Signing in again
    /// with the current scopes can fix it for a browser sign-in.
    case forbidden(Int, String)
    case invalidResponse
    case unsupported(String)

    public var errorDescription: String? {
        switch self {
        case .unauthorized: "The server rejected this token. Check that it is valid and has repository access."
        case .http(let code): "The server returned an error (HTTP \(code))."
        case .api(_, let message): message
        case .forbidden(_, let message): message + ". This account may not be allowed to do that."
        case .invalidResponse: "The server sent a response GitWolf couldn’t understand."
        case .unsupported(let what): what
        }
    }
}

public typealias HTTPTransport = @Sendable (URLRequest) async throws -> (Data, HTTPURLResponse)

/// Talks to a hosting provider's REST API to verify a token and list the repositories it can see.
public struct ProviderClient: Sendable {
    public let account: ProviderAccount
    private let secret: String
    private let transport: HTTPTransport

    public static let urlSessionTransport: HTTPTransport = { request in
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw ProviderError.invalidResponse }
        return (data, http)
    }

    public init(account: ProviderAccount, token: String, transport: @escaping HTTPTransport = ProviderClient.urlSessionTransport) {
        self.account = account
        self.secret = token
        self.transport = transport
    }

    // MARK: Endpoints

    var apiBase: String {
        let h = account.host
        let o = account.origin
        switch account.kind {
        case .github:
            if h == "github.com" { return "https://api.github.com" }
            // GitHub Enterprise Cloud with data residency: api.<tenant>.ghe.com
            if h.hasSuffix(".ghe.com") { return "https://api.\(h)" }
            return "\(o)/api/v3"
        case .gitlab: return "\(o)/api/v4"
        case .bitbucket: return h == "bitbucket.org" ? "https://api.bitbucket.org/2.0" : "\(o)/rest/api/1.0"
        case .gitea: return "\(o)/api/v1"
        case .azure: return o
        }
    }

    func request(_ url: String) throws -> URLRequest {
        guard let u = URL(string: url) else { throw ProviderError.invalidResponse }
        var r = URLRequest(url: u)
        r.timeoutInterval = 30
        r.setValue("application/json", forHTTPHeaderField: "Accept")
        r.setValue("GitWolf", forHTTPHeaderField: "User-Agent")
        if secret.isEmpty { return r }  // public data without an account
        if account.usesOAuth {
            r.setValue("Bearer \(secret)", forHTTPHeaderField: "Authorization")
            if account.kind == .github { r.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version") }
            return r
        }
        if account.usesPassword == true {
            let basic = Data("\(account.username):\(secret)".utf8).base64EncodedString()
            r.setValue("Basic \(basic)", forHTTPHeaderField: "Authorization")
            return r
        }
        switch account.kind {
        case .github:
            r.setValue("Bearer \(secret)", forHTTPHeaderField: "Authorization")
            r.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        case .gitlab:
            r.setValue(secret, forHTTPHeaderField: "PRIVATE-TOKEN")
        case .gitea:
            r.setValue("token \(secret)", forHTTPHeaderField: "Authorization")
        case .bitbucket:
            r.setValue("Bearer \(secret)", forHTTPHeaderField: "Authorization")
        case .azure:
            let basic = Data("\(account.username):\(secret)".utf8).base64EncodedString()
            r.setValue("Basic \(basic)", forHTTPHeaderField: "Authorization")
        }
        return r
    }

    func fetch(_ url: String) async throws -> (Data, HTTPURLResponse) {
        try await send(request(url))
    }

    /// Sends a request that changes something (POST, PUT, PATCH, DELETE) with a JSON body.
    func send(_ method: String, _ url: String, json: [String: Any]? = nil) async throws -> (Data, HTTPURLResponse) {
        var r = try request(url)
        r.httpMethod = method
        if let json {
            r.httpBody = try JSONSerialization.data(withJSONObject: json)
            r.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        return try await send(r)
    }

    private func send(_ r: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await transport(r)
        let isRead = r.httpMethod == nil || r.httpMethod == "GET"
        if (200..<300).contains(response.statusCode) { return (data, response) }
        let code = response.statusCode
        // A refused write says why (for example "Resource not accessible by personal access token"), and that is
        // what the user needs: reading with a token can work while changing something is not allowed.
        if !isRead, code == 401 || code == 403, let message = Self.errorMessage(data) {
            throw ProviderError.forbidden(code, message.hasSuffix(".") ? String(message.dropLast()) : message)
        }
        switch code {
        case 401: throw ProviderError.unauthorized
        case 403 where isRead: throw ProviderError.unauthorized
        default:
            // Writes report the server's own reason; reads keep plain codes so callers can react to 404.
            if !isRead, let message = Self.errorMessage(data) { throw ProviderError.api(code, message) }
            if code == 403 { throw ProviderError.unauthorized }
            throw ProviderError.http(code)
        }
    }

    /// The human-readable reason in a provider's JSON error body.
    static func errorMessage(_ data: Data) -> String? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        var parts: [String] = []
        if let m = json["message"] as? String { parts.append(m) }
        else if let m = json["error"] as? String { parts.append(m) }
        else if let m = (json["error"] as? [String: Any])?["message"] as? String { parts.append(m) }
        // GitHub adds details such as "A pull request already exists for owner:branch".
        if let errors = json["errors"] as? [[String: Any]] {
            parts += errors.compactMap { $0["message"] as? String }
        }
        let text = parts.joined(separator: ": ")
        return text.isEmpty ? nil : text
    }

    // MARK: Verify

    /// Returns the username the token belongs to; throws `unauthorized` for a bad token.
    public func currentUser() async throws -> String {
        if account.kind == .azure, account.usesOAuth {
            // The Visual Studio profile service identifies the signed-in Entra user.
            let (data, _) = try await fetch("https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1")
            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let name = json["emailAddress"] as? String ?? json["displayName"] as? String, !name.isEmpty
            else { throw ProviderError.invalidResponse }
            return name
        }
        guard account.kind.supportsRepositoryListing else {
            throw ProviderError.unsupported("Azure DevOps tokens can’t be verified here; they are used when you clone, fetch and push.")
        }
        if account.kind == .bitbucket, account.host != "bitbucket.org" {
            // Data Center has no "current user" endpoint; listing one repository proves the token works.
            _ = try await fetch(apiBase + "/repos?limit=1")
            return account.username
        }
        let (data, _): (Data, HTTPURLResponse)
        do {
            (data, _) = try await fetch(apiBase + "/user")
        } catch where account.kind == .bitbucket {
            _ = try await fetch(apiBase + "/repositories?role=member&pagelen=1")
            return ""
        }
        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw ProviderError.invalidResponse }
        let name: String? = switch account.kind {
        case .github, .gitea: json["login"] as? String ?? json["username"] as? String
        case .gitlab: json["username"] as? String
        case .bitbucket: json["username"] as? String ?? json["nickname"] as? String ?? json["display_name"] as? String
        case .azure: nil
        }
        guard let name, !name.isEmpty else { throw ProviderError.invalidResponse }
        return name
    }

    // MARK: Repositories

    /// Repositories the token can access, most recently active first. Capped to keep it quick.
    public func repositories(maxPages: Int = 5) async throws -> [RemoteRepository] {
        var all: [RemoteRepository] = []
        switch account.kind {
        case .github:
            all = try await paged(maxPages: maxPages, perPage: 100) {
                "\(apiBase)/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member&page=\($0)"
            } parse: { try Self.parseGitHub($0) }
        case .gitlab:
            all = try await paged(maxPages: maxPages, perPage: 100) {
                "\(apiBase)/projects?membership=true&simple=true&per_page=100&order_by=last_activity_at&page=\($0)"
            } parse: { try Self.parseGitLab($0) }
        case .gitea:
            all = try await paged(maxPages: maxPages, perPage: 50) {
                "\(apiBase)/user/repos?limit=50&page=\($0)"
            } parse: { try Self.parseGitea($0) }
        case .bitbucket where account.host != "bitbucket.org":
            var start = 0
            for _ in 0..<max(maxPages, 1) {
                let (data, _) = try await fetch("\(apiBase)/repos?limit=100&start=\(start)")
                all += try Self.parseBitbucketServer(data)
                guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                      root["isLastPage"] as? Bool == false, let n = root["nextPageStart"] as? Int else { break }
                start = n
            }
        case .bitbucket:
            var next: String? = "\(apiBase)/repositories?role=member&pagelen=100&sort=-updated_on"
            var pages = 0
            while let url = next, pages < maxPages {
                let (data, _) = try await fetch(url)
                all += try Self.parseBitbucket(data)
                next = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["next"] as? String
                pages += 1
            }
        case .azure:
            throw ProviderError.unsupported("Listing repositories isn’t available for Azure DevOps. Paste a clone URL instead.")
        }
        return all
    }

    private func paged(
        maxPages: Int, perPage: Int, url: (Int) -> String, parse: (Data) throws -> [RemoteRepository]
    ) async throws -> [RemoteRepository] {
        var result: [RemoteRepository] = []
        for page in 1...max(maxPages, 1) {
            let (data, _) = try await fetch(url(page))
            let items = try parse(data)
            result += items
            if items.count < perPage { break }
        }
        return result
    }

    // MARK: Parsing (static so tests can feed sample payloads)

    private static func iso(_ s: String?) -> Date? {
        guard let s else { return nil }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: s)
    }

    private static func array(_ data: Data) throws -> [[String: Any]] {
        guard let a = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { throw ProviderError.invalidResponse }
        return a
    }

    static func parseGitHub(_ data: Data) throws -> [RemoteRepository] {
        try array(data).compactMap { r in
            guard let name = r["full_name"] as? String, let https = r["clone_url"] as? String else { return nil }
            return RemoteRepository(
                fullName: name, description: r["description"] as? String ?? "",
                isPrivate: r["private"] as? Bool ?? false, httpsURL: https,
                sshURL: r["ssh_url"] as? String ?? "", updated: iso(r["pushed_at"] as? String ?? r["updated_at"] as? String))
        }
    }

    static func parseGitLab(_ data: Data) throws -> [RemoteRepository] {
        try array(data).compactMap { r in
            guard let name = r["path_with_namespace"] as? String, let https = r["http_url_to_repo"] as? String else { return nil }
            return RemoteRepository(
                fullName: name, description: r["description"] as? String ?? "",
                isPrivate: (r["visibility"] as? String ?? "private") != "public", httpsURL: https,
                sshURL: r["ssh_url_to_repo"] as? String ?? "", updated: iso(r["last_activity_at"] as? String))
        }
    }

    static func parseGitea(_ data: Data) throws -> [RemoteRepository] {
        try array(data).compactMap { r in
            guard let name = r["full_name"] as? String, let https = r["clone_url"] as? String else { return nil }
            return RemoteRepository(
                fullName: name, description: r["description"] as? String ?? "",
                isPrivate: r["private"] as? Bool ?? false, httpsURL: https,
                sshURL: r["ssh_url"] as? String ?? "", updated: iso(r["updated_at"] as? String))
        }
    }

    static func parseBitbucketServer(_ data: Data) throws -> [RemoteRepository] {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let values = root["values"] as? [[String: Any]] else { throw ProviderError.invalidResponse }
        return values.compactMap { r in
            guard let slug = r["slug"] as? String, let project = (r["project"] as? [String: Any])?["key"] as? String else { return nil }
            let clones = ((r["links"] as? [String: Any])?["clone"] as? [[String: Any]]) ?? []
            func link(_ n: String) -> String { clones.first { $0["name"] as? String == n }?["href"] as? String ?? "" }
            var https = link("http")
            if let url = URL(string: https), url.user != nil {
                var c = URLComponents(url: url, resolvingAgainstBaseURL: false)
                c?.user = nil
                https = c?.string ?? https
            }
            return RemoteRepository(
                fullName: "\(project)/\(slug)", description: r["description"] as? String ?? "",
                isPrivate: !(r["public"] as? Bool ?? false), httpsURL: https, sshURL: link("ssh"), updated: nil)
        }
    }

    static func parseBitbucket(_ data: Data) throws -> [RemoteRepository] {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let values = root["values"] as? [[String: Any]] else { throw ProviderError.invalidResponse }
        return values.compactMap { r in
            guard let name = r["full_name"] as? String else { return nil }
            let clones = ((r["links"] as? [String: Any])?["clone"] as? [[String: Any]]) ?? []
            func link(_ n: String) -> String { clones.first { $0["name"] as? String == n }?["href"] as? String ?? "" }
            // Bitbucket puts the username into https clone URLs; strip it so our credential is used.
            var https = link("https")
            if let url = URL(string: https), url.user != nil {
                var c = URLComponents(url: url, resolvingAgainstBaseURL: false)
                c?.user = nil
                https = c?.string ?? https
            }
            return RemoteRepository(
                fullName: name, description: r["description"] as? String ?? "",
                isPrivate: r["is_private"] as? Bool ?? true, httpsURL: https,
                sshURL: link("ssh"), updated: iso(r["updated_on"] as? String))
        }
    }
}

// MARK: - Pull requests and issues

public enum CollabKind: String, Sendable, CaseIterable {
    case pullRequest, issue
}

public enum CollabState: String, Sendable {
    case open, closed, merged
}

public enum CollabFilter: String, Sendable, CaseIterable {
    case open, closed, all
}

/// A pull / merge request or an issue, normalised across providers.
public struct CollabItem: Identifiable, Hashable, Sendable {
    public var id: Int { number }
    public var number: Int
    public var title: String
    public var author: String
    public var state: CollabState
    public var isDraft: Bool
    public var labels: [String]
    public var comments: Int
    public var created: Date?
    public var updated: Date?
    public var url: URL?
    public var body: String
    public var sourceBranch: String
    public var targetBranch: String
    /// Commit at the tip of the pull request branch, used to look up CI checks.
    public var headSHA: String = ""

    public init(number: Int, title: String, author: String = "", state: CollabState = .open, isDraft: Bool = false,
                labels: [String] = [], comments: Int = 0, created: Date? = nil, updated: Date? = nil, url: URL? = nil,
                body: String = "", sourceBranch: String = "", targetBranch: String = "", headSHA: String = "") {
        self.number = number; self.title = title; self.author = author; self.state = state; self.isDraft = isDraft
        self.labels = labels; self.comments = comments; self.created = created; self.updated = updated
        self.url = url; self.body = body; self.sourceBranch = sourceBranch; self.targetBranch = targetBranch
        self.headSHA = headSHA
    }
}

public extension ProviderKind {
    /// Whether GitWolf can list pull requests / issues for this provider.
    func supportsCollab(_ kind: CollabKind, host: String) -> Bool {
        switch self {
        case .github, .gitlab, .gitea: true
        case .bitbucket: kind == .pullRequest && host == "bitbucket.org"
        case .azure: false
        }
    }
}

public extension ProviderClient {
    static let collabPageSize = 30

    /// One page (1-based) of pull requests or issues for the repository at `repoPath` (`owner/repo`).
    func collab(_ kind: CollabKind, repoPath: String, filter: CollabFilter, page: Int) async throws -> [CollabItem] {
        let host = account.host
        guard account.kind.supportsCollab(kind, host: host) else {
            throw ProviderError.unsupported("\(account.kind.title) \(kind == .issue ? "issues are" : "pull requests are") not supported here yet.")
        }
        let n = Self.collabPageSize
        switch account.kind {
        case .github:
            let state = filter.rawValue
            if kind == .pullRequest {
                let (data, _) = try await fetch("\(apiBase)/repos/\(repoPath)/pulls?state=\(state)&sort=updated&direction=desc&per_page=\(n)&page=\(page)")
                return try Self.parseGitHubCollab(data, kind: .pullRequest)
            }
            let (data, _) = try await fetch("\(apiBase)/repos/\(repoPath)/issues?state=\(state)&sort=updated&direction=desc&per_page=\(n)&page=\(page)")
            return try Self.parseGitHubCollab(data, kind: .issue)
        case .gitlab:
            let id = Self.gitlabProjectID(repoPath)
            let resource = kind == .pullRequest ? "merge_requests" : "issues"
            let states: [String] = switch (kind, filter) {
            case (_, .open): ["opened"]
            case (.pullRequest, .closed): ["closed", "merged"]
            case (.issue, .closed): ["closed"]
            case (_, .all): ["all"]
            }
            var items: [CollabItem] = []
            for state in states {
                let (data, _) = try await fetch("\(apiBase)/projects/\(id)/\(resource)?state=\(state)&order_by=updated_at&sort=desc&per_page=\(n)&page=\(page)")
                items += try Self.parseGitLabCollab(data, kind: kind)
            }
            return states.count > 1 ? items.sorted { ($0.updated ?? .distantPast) > ($1.updated ?? .distantPast) } : items
        case .gitea:
            let state = filter.rawValue
            if kind == .pullRequest {
                let (data, _) = try await fetch("\(apiBase)/repos/\(repoPath)/pulls?state=\(state)&sort=recentupdate&limit=\(n)&page=\(page)")
                return try Self.parseGiteaCollab(data, kind: .pullRequest)
            }
            let (data, _) = try await fetch("\(apiBase)/repos/\(repoPath)/issues?type=issues&state=\(state)&limit=\(n)&page=\(page)")
            return try Self.parseGiteaCollab(data, kind: .issue)
        case .bitbucket:
            let states: [String] = switch filter {
            case .open: ["OPEN"]
            case .closed: ["MERGED", "DECLINED"]
            case .all: ["OPEN", "MERGED", "DECLINED"]
            }
            let query = states.map { "state=\($0)" }.joined(separator: "&")
            let (data, _) = try await fetch("\(apiBase)/repositories/\(repoPath)/pullrequests?\(query)&sort=-updated_on&pagelen=\(n)&page=\(page)")
            return try Self.parseBitbucketCollab(data)
        case .azure:
            throw ProviderError.unsupported("Azure DevOps isn’t supported here yet.")
        }
    }

    static func gitlabProjectID(_ path: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return path.addingPercentEncoding(withAllowedCharacters: allowed) ?? path
    }

    // MARK: Parsing

    private static func objects(_ data: Data) throws -> [[String: Any]] {
        guard let a = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { throw ProviderError.invalidResponse }
        return a
    }

    private static func date(_ s: Any?) -> Date? { iso(s as? String) }
    private static func link(_ s: Any?) -> URL? { (s as? String).flatMap(URL.init(string:)) }

    static func parseGitHubCollab(_ data: Data, kind: CollabKind) throws -> [CollabItem] {
        try objects(data).compactMap { r in
            // The issues endpoint also returns pull requests; drop them.
            if kind == .issue, r["pull_request"] != nil { return nil }
            guard let number = r["number"] as? Int, let title = r["title"] as? String else { return nil }
            let merged = r["merged_at"] is String
            let state: CollabState = merged ? .merged : (r["state"] as? String == "open" ? .open : .closed)
            return CollabItem(
                number: number, title: title, author: (r["user"] as? [String: Any])?["login"] as? String ?? "",
                state: state, isDraft: r["draft"] as? Bool ?? false,
                labels: (r["labels"] as? [[String: Any]])?.compactMap { $0["name"] as? String } ?? [],
                comments: r["comments"] as? Int ?? 0, created: date(r["created_at"]), updated: date(r["updated_at"]),
                url: link(r["html_url"]), body: r["body"] as? String ?? "",
                sourceBranch: (r["head"] as? [String: Any])?["ref"] as? String ?? "",
                targetBranch: (r["base"] as? [String: Any])?["ref"] as? String ?? "",
                headSHA: (r["head"] as? [String: Any])?["sha"] as? String ?? "")
        }
    }

    static func parseGitLabCollab(_ data: Data, kind: CollabKind) throws -> [CollabItem] {
        try objects(data).compactMap { r in
            guard let number = r["iid"] as? Int, let title = r["title"] as? String else { return nil }
            let state: CollabState = switch r["state"] as? String {
            case "merged": .merged
            case "closed": .closed
            default: .open
            }
            return CollabItem(
                number: number, title: title, author: (r["author"] as? [String: Any])?["username"] as? String ?? "",
                state: state, isDraft: r["draft"] as? Bool ?? r["work_in_progress"] as? Bool ?? false,
                labels: r["labels"] as? [String] ?? [], comments: r["user_notes_count"] as? Int ?? 0,
                created: date(r["created_at"]), updated: date(r["updated_at"]), url: link(r["web_url"]),
                body: r["description"] as? String ?? "",
                sourceBranch: r["source_branch"] as? String ?? "", targetBranch: r["target_branch"] as? String ?? "",
                headSHA: r["sha"] as? String ?? "")
        }
    }

    static func parseGiteaCollab(_ data: Data, kind: CollabKind) throws -> [CollabItem] {
        try objects(data).compactMap { r in
            guard let number = r["number"] as? Int, let title = r["title"] as? String else { return nil }
            let merged = r["merged"] as? Bool ?? (r["merged_at"] is String)
            let state: CollabState = merged ? .merged : (r["state"] as? String == "open" ? .open : .closed)
            let lowered = title.lowercased()
            let draft = r["draft"] as? Bool ?? (lowered.hasPrefix("wip:") || lowered.hasPrefix("[wip]") || lowered.hasPrefix("draft:"))
            return CollabItem(
                number: number, title: title, author: (r["user"] as? [String: Any])?["login"] as? String ?? "",
                state: state, isDraft: kind == .pullRequest && draft,
                labels: (r["labels"] as? [[String: Any]])?.compactMap { $0["name"] as? String } ?? [],
                comments: r["comments"] as? Int ?? 0, created: date(r["created_at"]), updated: date(r["updated_at"]),
                url: link(r["html_url"]), body: r["body"] as? String ?? "",
                sourceBranch: (r["head"] as? [String: Any])?["ref"] as? String ?? "",
                targetBranch: (r["base"] as? [String: Any])?["ref"] as? String ?? "",
                headSHA: (r["head"] as? [String: Any])?["sha"] as? String ?? "")
        }
    }

    static func parseBitbucketCollab(_ data: Data) throws -> [CollabItem] {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let values = root["values"] as? [[String: Any]] else { throw ProviderError.invalidResponse }
        return values.compactMap { r in
            guard let number = r["id"] as? Int, let title = r["title"] as? String else { return nil }
            let state: CollabState = switch r["state"] as? String {
            case "MERGED": .merged
            case "OPEN": .open
            default: .closed
            }
            func branch(_ key: String) -> String {
                ((r[key] as? [String: Any])?["branch"] as? [String: Any])?["name"] as? String ?? ""
            }
            let html = (((r["links"] as? [String: Any])?["html"]) as? [String: Any])?["href"]
            let author = r["author"] as? [String: Any]
            return CollabItem(
                number: number, title: title, author: author?["display_name"] as? String ?? author?["nickname"] as? String ?? "",
                state: state, comments: r["comment_count"] as? Int ?? 0,
                created: date(r["created_on"]), updated: date(r["updated_on"]), url: link(html),
                body: r["description"] as? String ?? "", sourceBranch: branch("source"), targetBranch: branch("destination"),
                headSHA: ((r["source"] as? [String: Any])?["commit"] as? [String: Any])?["hash"] as? String ?? "")
        }
    }
}
