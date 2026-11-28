import Foundation

// MARK: - Provider kinds

/// Hosting services GitWolf knows how to talk to. Listing repositories needs an API per kind;
/// web links work for all of them.
public enum ProviderKind: String, Codable, CaseIterable, Sendable, Identifiable {
    case github, gitlab, bitbucket, gitea, azure

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .github: "GitHub"
        case .gitlab: "GitLab"
        case .bitbucket: "Bitbucket"
        case .gitea: "Gitea / Forgejo"
        case .azure: "Azure DevOps"
        }
    }

    public var defaultHost: String {
        switch self {
        case .github: "github.com"
        case .gitlab: "gitlab.com"
        case .bitbucket: "bitbucket.org"
        case .gitea: "codeberg.org"
        case .azure: "dev.azure.com"
        }
    }

    /// Whether repositories can be listed (and tokens verified) through an API.
    public var supportsRepositoryListing: Bool { self != .azure }

    public func needsUsername(host: String) -> Bool { self == .bitbucket && host != "bitbucket.org" }

    /// Where the user creates a token / app password for this provider.
    public func tokenPageURL(host: String) -> URL? {
        switch self {
        case .github: URL(string: "https://\(host)/settings/tokens/new?scopes=repo&description=GitWolf")
        case .gitlab: URL(string: "https://\(host)/-/user_settings/personal_access_tokens?name=GitWolf&scopes=read_api,read_repository,write_repository")
        case .bitbucket: host == "bitbucket.org" ? URL(string: "https://id.atlassian.com/manage-profile/security/api-tokens") : nil
        case .gitea: URL(string: "https://\(host)/user/settings/applications")
        case .azure: URL(string: "https://dev.azure.com")
        }
    }

    /// What the secret is called in this provider's UI.
    public var secretName: String {
        switch self {
        case .bitbucket: "API token"
        case .gitea: "Access token"
        default: "Personal access token"
        }
    }

    /// Short hint on the scopes the secret needs.
    public var scopeHint: String {
        switch self {
        case .github: "Needs the “repo” scope."
        case .gitlab: "Needs read_api, read_repository and write_repository."
        case .bitbucket: "Needs the read:repository, write:repository and read:pullrequest scopes."
        case .gitea: "Needs repository read and write permissions."
        case .azure: "Needs Code (read & write). In Azure DevOps: User settings → Personal access tokens."
        }
    }

    /// Best guess for a host that has no saved account.
    public static func guess(host: String) -> ProviderKind? {
        let h = host.lowercased()
        if h == "github.com" || h.hasSuffix(".github.com") || h.contains("github") { return .github }
        if h == "gitlab.com" || h.contains("gitlab") { return .gitlab }
        if h == "bitbucket.org" || h.contains("bitbucket") { return .bitbucket }
        if h == "dev.azure.com" || h.hasSuffix(".visualstudio.com") || h == "ssh.dev.azure.com" { return .azure }
        if h == "codeberg.org" || h.contains("gitea") || h.contains("forgejo") { return .gitea }
        return nil
    }
}

public struct ProviderAccount: Codable, Identifiable, Hashable, Sendable {
    public var id: String
    public var kind: ProviderKind
    public var host: String
    public var username: String
    /// True when the secret is an account password rather than an access token (sent as HTTP Basic).
    public var usesPassword: Bool?
    /// "http" for self-hosted servers without TLS; nil means https.
    public var scheme: String?
    /// OAuth sign-in: the secret in the credential store is a JSON `OAuthTokens`, refreshed on demand.
    public var oauthClientID: String?

    public init(id: String = UUID().uuidString, kind: ProviderKind, host: String, username: String, usesPassword: Bool = false, scheme: String? = nil, oauthClientID: String? = nil) {
        self.oauthClientID = oauthClientID
        self.scheme = scheme == "http" ? "http" : nil
        self.usesPassword = usesPassword ? true : nil
        self.id = id
        self.kind = kind
        self.host = host
        self.username = username
    }

    /// Host without a port or path, which is what Git remotes are matched against.
    public var hostName: String {
        var h = host
        if let slash = h.firstIndex(of: "/") { h = String(h[..<slash]) }
        if let colon = h.firstIndex(of: ":") { h = String(h[..<colon]) }
        return h.lowercased()
    }
    public var usesOAuth: Bool { oauthClientID != nil }
    public var origin: String { "\(scheme ?? "https")://\(host)" }

    public var title: String { host == kind.defaultHost ? kind.title : "\(kind.title) · \(host)" }
}

/// A secret handed to git for one host.
public struct GitCredential: Sendable, Equatable {
    public var host: String
    public var username: String
    public var token: String
    /// Send the token as `Authorization: Bearer` (Microsoft Entra tokens for Azure DevOps) instead of
    /// answering git's password prompt.
    public var useBearerHeader: Bool

    public init(host: String, username: String, token: String, useBearerHeader: Bool = false) {
        self.host = host
        self.username = username.isEmpty ? "git" : username
        self.token = token
        self.useBearerHeader = useBearerHeader
    }

    /// POSIX `GIT_ASKPASS` helper: answers git's username/password prompts from the environment, so
    /// the secret never appears in a URL, a command line or a config file.
    public static let askpassScript = """
        #!/bin/sh
        case "$1" in
          Username*|username*) printf '%s\\n' "$GITWOLF_ASKPASS_USER" ;;
          *) printf '%s\\n' "$GITWOLF_ASKPASS_TOKEN" ;;
        esac
        """

    /// Environment that makes git use this credential and nothing else (other helpers, such as a
    /// stale keychain entry, are disabled for the call).
    public func environment(askpassPath: String) -> [String: String] {
        var env = [
            "GIT_ASKPASS": askpassPath,
            "GITWOLF_ASKPASS_USER": username,
            "GITWOLF_ASKPASS_TOKEN": token,
            "GIT_CONFIG_COUNT": "1",
            "GIT_CONFIG_KEY_0": "credential.helper",
            "GIT_CONFIG_VALUE_0": "",
        ]
        if useBearerHeader {
            // Passed through the environment, never the command line or a config file.
            env["GIT_CONFIG_COUNT"] = "2"
            env["GIT_CONFIG_KEY_1"] = "http.extraheader"
            env["GIT_CONFIG_VALUE_1"] = "Authorization: Bearer \(token)"
        }
        return env
    }
}

// MARK: - Remote URLs

/// Where a remote lives: `github.com` + `owner/repo`.
public struct RemoteLocation: Equatable, Hashable, Sendable {
    public var host: String
    /// `owner/repo` (or `group/subgroup/project`, `org/project/_git/repo` ...), no `.git`.
    public var path: String

    public init(host: String, path: String) {
        self.host = host
        self.path = path
    }

    /// Parses https, ssh:// and scp-like (`git@host:owner/repo.git`) remotes. Local paths return nil.
    public static func parse(_ remote: String) -> RemoteLocation? {
        var s = remote.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return nil }

        var host: String
        var path: String
        if let range = s.range(of: "://") {
            let scheme = s[..<range.lowerBound].lowercased()
            guard ["http", "https", "ssh", "git", "git+ssh", "ssh+git"].contains(scheme) else { return nil }
            s = String(s[range.upperBound...])
            guard let slash = s.firstIndex(of: "/") else { return nil }
            var authority = String(s[..<slash])
            path = String(s[s.index(after: slash)...])
            if let at = authority.lastIndex(of: "@") { authority = String(authority[authority.index(after: at)...]) }
            if let colon = authority.firstIndex(of: ":") { authority = String(authority[..<colon]) }
            host = authority
        } else if let at = s.firstIndex(of: "@"), let colon = s[at...].firstIndex(of: ":") {
            host = String(s[s.index(after: at)..<colon])
            path = String(s[s.index(after: colon)...])
        } else {
            return nil
        }

        while path.hasSuffix("/") { path.removeLast() }
        if path.hasSuffix(".git") { path.removeLast(4) }
        while path.hasPrefix("/") { path.removeFirst() }
        guard !host.isEmpty, !path.isEmpty else { return nil }

        // Azure DevOps over ssh: ssh.dev.azure.com:v3/org/project/repo → dev.azure.com/org/project/_git/repo
        if host.lowercased() == "ssh.dev.azure.com" {
            let parts = path.split(separator: "/").map(String.init)
            if parts.count == 4, parts[0] == "v3" {
                return RemoteLocation(host: "dev.azure.com", path: "\(parts[1])/\(parts[2])/_git/\(parts[3])")
            }
        }
        return RemoteLocation(host: host.lowercased(), path: path)
    }
}

/// Deep links into a provider's web UI.
public struct WebLinks: Sendable {
    public var kind: ProviderKind?
    public var location: RemoteLocation

    public init(kind: ProviderKind?, location: RemoteLocation) {
        self.kind = kind
        self.location = location
    }

    private var base: String { "https://\(location.host)/\(location.path)" }

    private func enc(_ s: String, allowSlash: Bool = true) -> String {
        var allowed = CharacterSet.urlPathAllowed
        if !allowSlash { allowed.remove("/") }
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }
    private func query(_ s: String) -> String {
        var allowed = CharacterSet.urlQueryAllowed
        allowed.remove(charactersIn: "&=+#?")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }

    public var repository: URL? { URL(string: base) }

    public func branch(_ name: String) -> URL? {
        switch kind {
        case .github: URL(string: "\(base)/tree/\(enc(name))")
        case .gitlab: URL(string: "\(base)/-/tree/\(enc(name))")
        case .bitbucket: URL(string: "\(base)/src/\(enc(name))")
        case .gitea: URL(string: "\(base)/src/branch/\(enc(name))")
        case .azure: URL(string: "\(base)?version=GB\(query(name))")
        case nil: repository
        }
    }

    public func commit(_ hash: String) -> URL? {
        switch kind {
        case .bitbucket: URL(string: "\(base)/commits/\(hash)")
        case .gitlab: URL(string: "\(base)/-/commit/\(hash)")
        default: URL(string: "\(base)/commit/\(hash)")
        }
    }

    public func newPullRequest(from branch: String) -> URL? {
        switch kind {
        case .github: URL(string: "\(base)/compare/\(enc(branch))?expand=1")
        case .gitlab: URL(string: "\(base)/-/merge_requests/new?merge_request%5Bsource_branch%5D=\(query(branch))")
        case .bitbucket: URL(string: "\(base)/pull-requests/new?source=\(query(branch))")
        case .gitea: URL(string: "\(base)/pulls")
        case .azure: URL(string: "\(base)/pullrequestcreate?sourceRef=\(query(branch))")
        case nil: repository
        }
    }

    public var pullRequestsWord: String { kind == .gitlab ? "Merge Request" : "Pull Request" }
}
