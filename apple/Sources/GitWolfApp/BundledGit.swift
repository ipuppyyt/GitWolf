import Foundation

/// A git built for GitWolf and shipped in `Contents/Resources/git` (see Scripts/build-git.sh). Only the
/// Mac App Store build has one; other builds use the system git.
enum BundledGit {
    static let path: String? = {
        guard let url = Bundle.main.resourceURL?.appendingPathComponent("git/bin/git"),
              FileManager.default.isExecutableFile(atPath: url.path) else { return nil }
        return url.path
    }()
}

/// Repository folders in the App Sandbox: the user grants a folder once (open panel, drop, Finder), and a
/// security-scoped bookmark keeps that access across launches. Without the sandbox this does nothing.
enum FolderAccess {
    static var isSandboxed: Bool { ProcessInfo.processInfo.environment["APP_SANDBOX_CONTAINER_ID"] != nil }
    private static let key = "folderBookmarks"
    nonisolated(unsafe) private static var held: [String: URL] = [:]

    /// Keeps access to a folder the user just chose, and remembers it for next time.
    static func remember(_ url: URL) {
        guard isSandboxed else { return }
        let path = url.standardizedFileURL.path
        if held[path] == nil, url.startAccessingSecurityScopedResource() { held[path] = url }
        guard let data = try? url.bookmarkData(options: .withSecurityScope, includingResourceValuesForKeys: nil, relativeTo: nil)
        else { return }
        var all = UserDefaults.standard.dictionary(forKey: key) as? [String: Data] ?? [:]
        all[path] = data
        UserDefaults.standard.set(all, forKey: key)
    }

    /// Regains access to every folder granted earlier. Call before reopening last session's tabs.
    static func restore() {
        guard isSandboxed else { return }
        var all = UserDefaults.standard.dictionary(forKey: key) as? [String: Data] ?? [:]
        for (path, data) in all {
            var stale = false
            guard let url = try? URL(resolvingBookmarkData: data, options: .withSecurityScope, relativeTo: nil, bookmarkDataIsStale: &stale),
                  url.startAccessingSecurityScopedResource() else { all[path] = nil; continue }
            held[path] = url
            if stale, let fresh = try? url.bookmarkData(options: .withSecurityScope, includingResourceValuesForKeys: nil, relativeTo: nil) {
                all[path] = fresh
            }
        }
        UserDefaults.standard.set(all, forKey: key)
    }

    static func forget(_ path: String) {
        guard isSandboxed else { return }
        if let url = held.removeValue(forKey: path) { url.stopAccessingSecurityScopedResource() }
        var all = UserDefaults.standard.dictionary(forKey: key) as? [String: Data] ?? [:]
        all[path] = nil
        UserDefaults.standard.set(all, forKey: key)
    }
}
