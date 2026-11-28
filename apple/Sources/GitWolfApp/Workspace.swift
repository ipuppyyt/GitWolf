import AppKit
import GitWolfCore
import Observation
import SwiftUI

enum RecentRepositories {
    private static let key = "recentRepositories"

    static func load() -> [URL] {
        (UserDefaults.standard.stringArray(forKey: key) ?? [])
            .map { URL(fileURLWithPath: $0, isDirectory: true) }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
    }

    static func remove(_ path: String) {
        let paths = (UserDefaults.standard.stringArray(forKey: key) ?? []).filter { $0 != path }
        UserDefaults.standard.set(paths, forKey: key)
    }

    static func clear() { UserDefaults.standard.removeObject(forKey: key) }

    static func add(_ url: URL) {
        var paths = (UserDefaults.standard.stringArray(forKey: key) ?? []).filter { $0 != url.path }
        paths.insert(url.path, at: 0)
        UserDefaults.standard.set(Array(paths.prefix(12)), forKey: key)
    }
}

enum HomeLayoutStore {
    private static let key = "homeLayout"

    static func load() -> HomeLayout {
        let saved = UserDefaults.standard.data(forKey: key).flatMap { try? JSONDecoder().decode(HomeLayout.self, from: $0) }
        return (saved ?? HomeLayout()).pruned { FileManager.default.fileExists(atPath: $0) }
    }

    static func save(_ layout: HomeLayout) {
        if let data = try? JSONEncoder().encode(layout) { UserDefaults.standard.set(data, forKey: key) }
    }
}

/// All repositories open in one window. Every open repository keeps its own store (and file
/// watcher) alive, so badges and state stay current even when it is not the visible one.
@MainActor @Observable
final class Workspace {
    private(set) var repos: [RepositoryStore] = []
    var selectedID: String?
    var errorMessage: String?
    var showPalette = false
    var sheet: WorkspaceSheet?
    private(set) var recents: [URL] = RecentRepositories.load()
    private(set) var home = HomeLayoutStore.load()
    private(set) var repoIcons = RepoIconStore.load()

    /// The window's workspace, for AppKit hooks (Dock menu) that live outside SwiftUI.
    static weak var current: Workspace?
    private static var didBootstrap = false
    private static let openKey = "openRepositories"
    private static let selectedKey = "selectedRepository"
    private var isRestoring = false

    var selected: RepositoryStore? { repos.first { $0.id == selectedID } }

    /// Files with uncommitted changes across every open repository.
    var totalChanges: Int { repos.reduce(0) { $0 + $1.status.changeCount } }

    /// Registers this workspace with the Dock badge and menu bar icon. Called once from the window's
    /// `.task`, not from `init`: SwiftUI re-runs `Workspace()` whenever the owning view is re-created,
    /// and publishing observed state from there loops forever.
    /// Folders asked for from outside (Finder, the `gitwolf` command, a link) before the window existed.
    static var pendingOpens: [URL] = []
    private static var ready = false

    /// Opens a folder from outside the app now, or as soon as the window is up.
    static func openExternally(_ folders: [URL]) {
        guard let workspace = current, ready else { pendingOpens += folders; return }
        folders.forEach { workspace.open($0) }
    }

    func activate() {
        guard Self.current !== self else { return }
        Self.current = self
        trackDockBadge()
    }

    /// Keeps the Dock icon's badge equal to `totalChanges`, re-arming after every change.
    private func trackDockBadge() {
        let count = withObservationTracking {
            totalChanges
        } onChange: { [weak self] in
            Task { @MainActor in self?.trackDockBadge() }
        }
        NSApp?.dockTile.badgeLabel = BadgeLabel.text(forChanges: count)
        StatusItemController.shared.update(changes: count)
    }

    /// Edits the home layout and saves it.
    func editHome(_ change: (inout HomeLayout) -> Void) {
        change(&home)
        HomeLayoutStore.save(home)
    }

    /// Removes a repository from recents, pins and groups (the folder itself is untouched).
    func forget(_ path: String) {
        RecentRepositories.remove(path)
        FolderAccess.forget(path)
        recents = RecentRepositories.load()
        editHome { $0.remove(path) }
        setIcon(nil, for: path)
    }

    func setTint(_ tint: RepoTint?, for path: String) {
        repoIcons.setTint(tint, for: path)
        RepoIconStore.save(repoIcons)
    }

    func setIcon(_ icon: RepoIcon?, for path: String) {
        if let unused = repoIcons.set(icon, for: path) { RepoIconStore.deleteImage(unused) }
        RepoIconStore.save(repoIcons)
    }

    func setIconImage(from url: URL, for path: String) {
        Task {
            let name = await Task.detached(priority: .userInitiated) { () -> String? in
                let scoped = url.startAccessingSecurityScopedResource()
                defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                return RepoIconStore.importImage(from: url)
            }.value
            guard let name else {
                errorMessage = "That file couldn’t be read as an image."
                return
            }
            setIcon(.image(name), for: path)
        }
    }

    func clearRecents() {
        RecentRepositories.clear()
        recents = []
    }

    // MARK: Opening & closing

    func open(_ url: URL) { Task { await openAsync(url) } }

    func openAsync(_ url: URL) async {
        FolderAccess.remember(url)
        do {
            let repo = try await GitRepository.open(at: url)
            let id = repo.root.path
            if repos.contains(where: { $0.id == id }) { selectedID = id; persist(); return }
            let store = RepositoryStore(repo: repo)
            repos.append(store)
            selectedID = id
            store.start()
            RecentRepositories.add(repo.root)
            recents = RecentRepositories.load()
            persist()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func openPanel() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = true
        panel.prompt = "Open"
        panel.message = "Choose Git repositories"
        if panel.runModal() == .OK { panel.urls.forEach(open) }
    }

    func close(_ store: RepositoryStore) {
        store.stop()
        repos.removeAll { $0.id == store.id }
        if selectedID == store.id { selectedID = repos.last?.id }
        persist()
    }

    func select(_ id: String?) {
        selectedID = id
        persist()
    }

    func selectIndex(_ index: Int) {
        guard repos.indices.contains(index) else { return }
        select(repos[index].id)
    }

    func cycle(_ delta: Int) {
        guard !repos.isEmpty else { return }
        let current = repos.firstIndex { $0.id == selectedID } ?? (delta > 0 ? -1 : 0)
        select(repos[(current + delta + repos.count) % repos.count].id)
    }

    // MARK: Persistence

    private func persist() {
        guard !isRestoring else { return }
        UserDefaults.standard.set(repos.map(\.id), forKey: Self.openKey)
        UserDefaults.standard.set(selectedID, forKey: Self.selectedKey)
    }

    /// Opens repositories given on the command line, otherwise restores last session's tabs.
    func bootstrap() async {
        guard !Self.didBootstrap else { return }
        Self.didBootstrap = true
        // `GitWolf --open=/path/to/repo` (repeatable). A bare path would be treated by AppKit
        // as a document to open and suppress the window, hence the explicit flag.
        let args = CommandLine.arguments.dropFirst().filter { $0.hasPrefix("--open=") }.map { String($0.dropFirst(7)) }
        if !args.isEmpty {
            for path in args { await openAsync(URL(fileURLWithPath: path, isDirectory: true)) }
            drainPendingOpens()
            return
        }
        isRestoring = true
        FolderAccess.restore()
        let paths = UserDefaults.standard.stringArray(forKey: Self.openKey) ?? []
        let selected = UserDefaults.standard.string(forKey: Self.selectedKey)
        for path in paths where FileManager.default.fileExists(atPath: path) {
            await openAsync(URL(fileURLWithPath: path, isDirectory: true))
        }
        isRestoring = false
        if let selected, repos.contains(where: { $0.id == selected }) { selectedID = selected }
        persist()
        drainPendingOpens()
    }

    /// Opens what Finder or the `gitwolf` command asked for while the app was still starting.
    private func drainPendingOpens() {
        Self.ready = true
        let queued = Self.pendingOpens
        Self.pendingOpens = []
        queued.forEach { open($0) }
    }
}

extension FocusedValues {
    @Entry var workspace: Workspace?
}
