import Foundation
import GitWolfCore
import Observation
import Security

/// Secrets live in the login Keychain, never in preferences or on disk elsewhere.
enum Keychain {
    private static let service = "GitWolf Provider Accounts"

    struct Failure: LocalizedError {
        let status: OSStatus
        var errorDescription: String? {
            (SecCopyErrorMessageString(status, nil) as String?) ?? "Keychain error \(status)"
        }
    }

    static func save(_ secret: String, for account: String) throws {
        delete(account)
        let status = SecItemAdd([
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account,
            kSecValueData: Data(secret.utf8),
            kSecAttrAccessible: kSecAttrAccessibleWhenUnlocked,
        ] as CFDictionary, nil)
        guard status == errSecSuccess else { throw Failure(status: status) }
    }

    static func read(_ account: String) -> String? {
        var item: CFTypeRef?
        let status = SecItemCopyMatching([
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account,
            kSecReturnData: true,
            kSecMatchLimit: kSecMatchLimitOne,
        ] as CFDictionary, &item)
        guard status == errSecSuccess, let data = item as? Data else {
            // A token saved before the rename (GitSync): move it over now.
            guard let old = LegacyMigration.legacySecret(for: account) else { return nil }
            if (try? save(old, for: account)) != nil { LegacyMigration.deleteLegacySecret(for: account) }
            return old
        }
        return String(data: data, encoding: .utf8)
    }

    @discardableResult
    static func delete(_ account: String) -> Bool {
        SecItemDelete([
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account,
        ] as CFDictionary) == errSecSuccess
    }
}

/// Thread-safe access to saved accounts (metadata in UserDefaults, secrets in the Keychain), so git
/// operations running off the main actor can look credentials up.
enum AccountStorage {
    private static let key = "providerAccounts"

    static func load() -> [ProviderAccount] {
        guard let data = UserDefaults.standard.data(forKey: key),
              let accounts = try? JSONDecoder().decode([ProviderAccount].self, from: data) else { return [] }
        return accounts
    }

    static func save(_ accounts: [ProviderAccount]) {
        if let data = try? JSONEncoder().encode(accounts) { UserDefaults.standard.set(data, forKey: key) }
    }

    static func token(for account: ProviderAccount) -> String? { Keychain.read(account.id) }

    /// The secret to use right now: a token or password as stored, or, for OAuth accounts, a valid
    /// access token (refreshed first when it is about to expire).
    static func secret(for account: ProviderAccount) async -> String? {
        guard let stored = token(for: account) else { return nil }
        guard account.usesOAuth else { return stored }
        return await OAuthRefresher.shared.accessToken(for: account, stored: stored)
    }

    /// The credential git should use for a remote on `host`, if the user added an account for it.
    static func credential(forHost host: String) async -> GitCredential? {
        guard let account = account(forHost: host), let secret = await secret(for: account) else { return nil }
        let bitbucketCloudToken = account.kind == .bitbucket && account.hostName == "bitbucket.org" && account.usesPassword != true
        let user = account.usesOAuth
            ? GitCredential.oauthUsername(kind: account.kind, fallback: account.username)
            : bitbucketCloudToken ? "x-bitbucket-api-token-auth" : account.username
        return GitCredential(host: account.hostName, username: user, token: secret,
                             useBearerHeader: account.usesOAuth && account.kind == .azure)
    }

    static func account(forHost host: String) -> ProviderAccount? {
        load().first { $0.hostName == host.lowercased() }
    }
}

/// Writes the `GIT_ASKPASS` helper once and hands out its path.
enum AskPass {
    static let path: String? = {
        // The App Store sandbox cannot execute a script written at runtime, so that build ships it in the bundle.
        if FolderAccess.isSandboxed {
            return Bundle.main.url(forResource: "askpass", withExtension: "sh")?.path
        }
        let fm = FileManager.default
        guard let support = try? fm.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        else { return nil }
        let dir = support.appendingPathComponent("GitWolf", isDirectory: true)
        let file = dir.appendingPathComponent("askpass.sh")
        do {
            try fm.createDirectory(at: dir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            if (try? String(contentsOf: file, encoding: .utf8)) != GitCredential.askpassScript {
                try GitCredential.askpassScript.write(to: file, atomically: true, encoding: .utf8)
            }
            try fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: file.path)
            return file.path
        } catch {
            return nil
        }
    }()
}

/// Refreshes OAuth tokens one at a time per account, so two git operations never race on a rotating
/// refresh token.
actor OAuthRefresher {
    static let shared = OAuthRefresher()
    private var inFlight: [String: Task<String?, Never>] = [:]

    func accessToken(for account: ProviderAccount, stored: String) async -> String? {
        guard let tokens = try? JSONDecoder().decode(OAuthTokens.self, from: Data(stored.utf8)) else { return nil }
        guard tokens.needsRefresh() else { return tokens.accessToken }
        if let running = inFlight[account.id] { return await running.value }
        let task = Task<String?, Never> {
            guard let clientID = account.oauthClientID else { return tokens.accessToken }
            let client = OAuthClient(kind: account.kind, origin: account.origin, clientID: clientID, clientSecret: tokens.clientSecret)
            guard let fresh = try? await client.refresh(tokens) else { return tokens.accessToken }
            if let data = try? JSONEncoder().encode(fresh), let json = String(data: data, encoding: .utf8) {
                try? Keychain.save(json, for: account.id)
            }
            return fresh.accessToken
        }
        inFlight[account.id] = task
        let value = await task.value
        inFlight[account.id] = nil
        return value
    }
}

@MainActor @Observable
final class AccountStore {
    static let shared = AccountStore()
    private(set) var accounts: [ProviderAccount] = AccountStorage.load()

    /// Saves (or replaces) an account and its secret.
    func add(_ account: ProviderAccount, token: String) throws {
        var list = accounts
        if let i = list.firstIndex(where: { $0.kind == account.kind && $0.host == account.host && $0.username == account.username }) {
            Keychain.delete(list[i].id)
            list.remove(at: i)
        }
        try Keychain.save(token, for: account.id)
        list.append(account)
        accounts = list
        AccountStorage.save(list)
    }

    func addOAuth(_ account: ProviderAccount, tokens: OAuthTokens) throws {
        let data = try JSONEncoder().encode(tokens)
        try add(account, token: String(decoding: data, as: UTF8.self))
    }

    func remove(_ account: ProviderAccount) {
        Keychain.delete(account.id)
        accounts.removeAll { $0.id == account.id }
        AccountStorage.save(accounts)
    }
}
