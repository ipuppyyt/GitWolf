import AppKit
import GitWolfCore
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    /// Folders dropped on the Dock icon, `open -a GitWolf folder`, the `gitwolf` command and `gitwolf://open?path=` links.
    func application(_ application: NSApplication, open urls: [URL]) {
        let folders = urls.compactMap(LaunchRequest.folder(from:))
        guard !folders.isEmpty else { return }
        Workspace.openExternally(folders)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        GitRunner.defaultGitPath = AppSettings.shared.gitPath
        NSApp.setActivationPolicy(.regular)
        // A packaged app carries AppIcon.icns; a development build has none, so draw the same artwork.
        if !Bundle.main.bundlePath.hasSuffix(".app"), let icon = BrandArt.icon { NSApp.applicationIconImage = icon }
        NSApp.activate(ignoringOtherApps: true)
        Task { @MainActor in UpdateManager.start() }
        StatusItemController.shared.sync()
        CheckNotifier.shared.start()
        NSWindow.allowsAutomaticWindowTabbing = false
        MainMenuTidier.start()
        NotificationCenter.default.addObserver(forName: UserDefaults.didChangeNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in StatusItemController.shared.sync() }
        }
        #if DEBUG
        // GITWOLF_DUMP_MENU=/path.txt writes the main menu's structure, then quits.
        if let path = ProcessInfo.processInfo.environment["GITWOLF_DUMP_MENU"] {
            DispatchQueue.main.asyncAfter(deadline: .now() + (Double(ProcessInfo.processInfo.environment["GITWOLF_DUMP_DELAY"] ?? "") ?? 4)) {
                func dump(_ menu: NSMenu, _ depth: Int) -> String {
                    menu.items.map { item in
                        let line = String(repeating: "  ", count: depth) + (item.isSeparatorItem ? "---" : item.title)
                        return line + (item.submenu.map { "\n" + dump($0, depth + 1) } ?? "")
                    }.joined(separator: "\n")
                }
                let text = dump(NSApp.mainMenu ?? NSMenu(), 0) + "\n\n" + StatusItemController.shared.debugState
                try? text.write(toFile: path, atomically: true, encoding: .utf8)
                exit(0)
            }
        }
        // GITWOLF_SNAPSHOT=/path.png renders the window to a PNG after a delay, then quits.
        // GITWOLF_APPEARANCE=dark|light forces an appearance for the capture.
        if let path = ProcessInfo.processInfo.environment["GITWOLF_SNAPSHOT"] {
            let dark = ProcessInfo.processInfo.environment["GITWOLF_APPEARANCE"] == "dark"
            NSApp.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
            let target = ProcessInfo.processInfo.environment["GITWOLF_SNAPSHOT_WINDOW"]
            let wantSettings = target == "settings"
            let wantSheet = target?.hasPrefix("sheet:") == true
            if wantSettings || wantSheet {
                DispatchQueue.main.asyncAfter(deadline: .now() + (Double(ProcessInfo.processInfo.environment["GITWOLF_SNAPSHOT_DELAY"] ?? "") ?? 3)) {
                    // `settings` opens the first menu's Settings item; `sheet:<Title>` any menu item by title.
                    let title = wantSheet ? String(target!.dropFirst(6)) : "Settings"
                    let menus = wantSheet ? (NSApp.mainMenu?.items.compactMap(\.submenu) ?? []) : [NSApp.mainMenu?.items.first?.submenu].compactMap { $0 }
                    for menu in menus {
                        if let i = menu.items.firstIndex(where: { $0.title.hasPrefix(title) }) {
                            menu.performActionForItem(at: i)
                            break
                        }
                    }
                }
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 6) {
                let main = NSApp.windows.first { $0.canBecomeMain && $0.isVisible && $0.title != "Settings" }
                let window = wantSettings
                    ? NSApp.windows.first { $0.title == "Settings" && $0.isVisible }
                    : (wantSheet ? main?.attachedSheet : main)
                if let view = window?.contentView,
                   let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                    view.cacheDisplay(in: view.bounds, to: rep)
                    do { try rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path)) }
                    catch { NSLog("snapshot write failed: \(error)") }
                } else { NSLog("snapshot: no view/rep") }
                if wantSheet { exit(0) }
                NSApp.terminate(nil)
            }
        }
        #endif
    }
    /// Right-clicking the Dock icon lists recent repositories.
    func applicationDockMenu(_ sender: NSApplication) -> NSMenu? {
        let menu = NSMenu()
        for url in RecentRepositories.load().prefix(8) {
            let item = NSMenuItem(title: url.lastPathComponent, action: #selector(openRecent(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = url
            menu.addItem(item)
        }
        return menu.items.isEmpty ? nil : menu
    }

    @objc private func openRecent(_ item: NSMenuItem) {
        if let url = item.representedObject as? URL { Task { @MainActor in Workspace.current?.open(url) } }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

/// SwiftUI leaves empty View and Format menus behind, and the Edit, View, Format and Window menus aren't wanted (AppKit re-adds View with Full Screen and tab items once a window is focused). Remove them,
/// and again whenever SwiftUI rebuilds the menu.
@MainActor
enum MainMenuTidier {
    private static var pending = false
    private static var monitor: Any?

    static func start() {
        NotificationCenter.default.addObserver(forName: NSMenu.didAddItemNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in schedule() }
        }
        // Without an Edit menu the standard text shortcuts would stop working, so route them to the
        // focused text field ourselves.
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            guard flags.contains(.command), !flags.contains(.option), !flags.contains(.control),
                  let key = event.charactersIgnoringModifiers?.lowercased() else { return event }
            let selector: Selector?
            switch (key, flags.contains(.shift)) {
            case ("c", false): selector = #selector(NSText.copy(_:))
            case ("v", false): selector = #selector(NSText.paste(_:))
            case ("x", false): selector = #selector(NSText.cut(_:))
            case ("a", false): selector = #selector(NSText.selectAll(_:))
            case ("z", false): selector = Selector(("undo:"))
            case ("z", true): selector = Selector(("redo:"))
            case ("m", false): selector = #selector(NSWindow.performMiniaturize(_:))
            default: selector = nil
            }
            guard let selector, NSApp.sendAction(selector, to: nil, from: nil) else { return event }
            return nil
        }
        schedule()
    }

    private static func schedule() {
        guard !pending else { return }
        pending = true
        // Coalesce bursts of menu changes, and never mutate the menu from inside its own notification.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
            pending = false
            tidy()
        }
    }

    /// Removes leading, trailing and doubled separators left behind by deleted items.
    private static func collapseSeparators(in menu: NSMenu) {
        var previousWasSeparator = true
        for entry in menu.items where !entry.isHidden {
            if entry.isSeparatorItem && previousWasSeparator { menu.removeItem(entry) }
            else { previousWasSeparator = entry.isSeparatorItem }
        }
        while let last = menu.items.last, last.isSeparatorItem { menu.removeItem(last) }
    }

    static func tidy() {
        guard let main = NSApp.mainMenu else { return }
        for item in main.items.reversed() {
            guard let sub = item.submenu else { continue }
            let hasContent = sub.items.contains { !$0.isSeparatorItem }
            if !hasContent && item.title != "GitWolf" { main.removeItem(item); continue }
            if ["Edit", "View", "Format", "Window"].contains(item.title) { main.removeItem(item); continue }
            collapseSeparators(in: sub)
        }
    }
}

@main
struct GitWolfApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    init() { LegacyMigration.run() }

    var body: some Scene {
        WindowGroup {
            RootView()
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1440, height: 880)
        .commands { AppCommands() }


        // A regular window (not the `Settings` scene) so it gets the same hidden-title-bar chrome
        // as the main window; the Settings scene re-applies its own opaque title bar.
        Window("Contributors", id: "contributors") { ContributorsView().modifier(AppearanceModifier()) }
            .windowResizability(.contentSize)
            .defaultPosition(.center)

        Window("Settings", id: "settings") { SettingsView() }
            .windowStyle(.hiddenTitleBar)
            .windowResizability(.contentSize)
            .defaultPosition(.center)
    }
}

struct AppearanceModifier: ViewModifier {
    @AppStorage(SettingsKey.appearance) private var appearance = AppearanceChoice.system.rawValue

    @AppStorage(SettingsKey.accent) private var accent = AccentChoice.wolf.rawValue

    func body(content: Content) -> some View {
        content
            .tint(Theme.brand)
            // Theme colours are read from settings, so rebuild the tree when the accent changes.
            .id(accent)
            // Set the app-wide NSAppearance instead of `preferredColorScheme`: going back to
            // "System" with the latter leaves windows (materials, backdrops) half-updated.
            .onChange(of: appearance, initial: true) { _, value in
                AppearanceChoice(rawValue: value)?.apply()
            }
    }
}

struct RootView: View {
    @State private var workspace = Workspace()

    var body: some View {
        HStack(spacing: 0) {
            RepoRail(workspace: workspace)
            ZStack {
                if let store = workspace.selected {
                    RepositoryView(store: store, workspace: workspace).id(store.id)
                } else {
                    DashboardView(workspace: workspace)
                }
            }
        }
        .frame(minWidth: 1080, minHeight: 640)
        .containerBackground(for: .window) { WindowBackdrop() }
        .overlay { if workspace.showPalette { CommandPalette(workspace: workspace) } }
        // The appearance modifier rebuilds its content when the accent changes, so it sits below the sheet:
        // a sheet attached above it keeps its state (the welcome walkthrough would otherwise restart).
        .modifier(AppearanceModifier())
        .sheet(item: Binding(get: { workspace.sheet }, set: { workspace.sheet = $0 })) { sheet in
            switch sheet {
            case .clone: CloneSheet(workspace: workspace)
            case .create: CreateRepositorySheet(workspace: workspace)
            case .account: AddAccountSheet()
            case .welcome: WelcomeSheet(workspace: workspace).tint(Theme.brand)
            case .pullRequests: PullRequestOverviewSheet(workspace: workspace)
            }
        }
        .focusedSceneValue(\.workspace, workspace)
        .task {
            workspace.activate()
            await workspace.bootstrap()
            // First run: a Mac with no repositories and no accounts gets the walkthrough. Anyone who already uses
            // GitWolf is marked as having seen it, so an update never interrupts them.
            if !UserDefaults.standard.bool(forKey: WelcomeSheet.seenKey) {
                if workspace.repos.isEmpty && AccountStore.shared.accounts.isEmpty { workspace.sheet = .welcome }
                else { UserDefaults.standard.set(true, forKey: WelcomeSheet.seenKey) }
            }
            #if DEBUG
            if let name = ProcessInfo.processInfo.environment["GITWOLF_SHEET"] { workspace.sheet = WorkspaceSheet(rawValue: name) }
            // GITWOLF_ICON_IMPORT=/path/to/image imports it as the selected repository's icon.
            if let file = ProcessInfo.processInfo.environment["GITWOLF_ICON_IMPORT"] {
                workspace.setIconImage(from: URL(fileURLWithPath: file), for: workspace.selectedID ?? "/Users/dhanush/Work/GitSync")
            }
            #endif
        }
        .alert("Couldn’t Open Repository", isPresented: Binding(
            get: { workspace.errorMessage != nil },
            set: { if !$0 { workspace.errorMessage = nil } })
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(workspace.errorMessage ?? "")
        }
    }
}

/// Menus that don't apply to GitWolf: extra windows, toolbar and sidebar toggles and font formatting.
/// A separate group because a `Commands` body takes at most ten entries on the toolchains CI uses.
struct UnusedSystemMenus: Commands {
    var body: some Commands {
        CommandGroup(replacing: .systemServices) {}
        CommandGroup(replacing: .appVisibility) {}
        CommandGroup(replacing: .toolbar) {}
        CommandGroup(replacing: .sidebar) {}
        CommandGroup(replacing: .textFormatting) {}
    }
}

struct AppCommands: Commands {
    @FocusedValue(\.workspace) private var workspace
    @Environment(\.openWindow) private var openWindow

    var body: some Commands {
        CommandGroup(replacing: .appInfo) {
            Button("About GitWolf") { AboutPanel.show() }
            Button("Check for Updates…") { UpdateManager.checkForUpdates() }
        }
        CommandGroup(replacing: .appSettings) {
            Button("Settings…") { openWindow(id: "settings") }
                .keyboardShortcut(",")
        }
        UnusedSystemMenus()
        CommandGroup(replacing: .help) {
            Button("Contributors") { openWindow(id: "contributors") }
            Link("GitWolf Documentation", destination: URL(string: "https://github.com/ipuppyyt/GitWolf/tree/main/docs")!)
            Link("Report an Issue", destination: URL(string: "https://github.com/ipuppyyt/GitWolf/issues")!)
        }
        CommandGroup(replacing: .newItem) {
            Button("Open Repository…") { workspace?.openPanel() }
                .keyboardShortcut("o")
            Button("Clone Repository…") { workspace?.sheet = .clone }
                .keyboardShortcut("o", modifiers: [.command, .shift])
            Button("New Repository…") { workspace?.sheet = .create }
                .keyboardShortcut("n", modifiers: [.command, .shift])
            Menu("Open Recent") {
                ForEach(workspace?.recents ?? [], id: \.self) { url in
                    Button(url.lastPathComponent) { workspace?.open(url) }
                }
                Divider()
                Button("Clear Menu") { workspace?.clearRecents() }
                    .disabled(workspace?.recents.isEmpty ?? true)
            }
            Button("Close Repository") { if let s = workspace?.selected { workspace?.close(s) } }
                .keyboardShortcut("w", modifiers: [.command, .shift])
                .disabled(workspace?.selected == nil)
        }
        CommandMenu("Hosting") {
            Button("Pull Requests Across Repositories…") { workspace?.sheet = .pullRequests }
                .keyboardShortcut("p", modifiers: [.command, .option])
            Divider()
            Button("New Pull Request…") { workspace?.selected?.sheet = .newCollab(.pullRequest) }
                .disabled(workspace?.selected?.providerTitle == nil)
            Button("New Issue…") { workspace?.selected?.sheet = .newCollab(.issue) }
                .disabled(workspace?.selected?.providerTitle == nil)
            Button("Notifications…") { workspace?.selected?.sheet = .notifications }.disabled(workspace?.selected == nil)
            Button("Fork Repository…") { workspace?.selected?.sheet = .fork }.disabled(workspace?.selected?.canFork != true)
        }
        CommandMenu("Repository") {
            Button("Jump To…") { workspace?.showPalette.toggle() }
                .keyboardShortcut("k")
            Menu("Switch Repository") {
                Button("Next Repository") { workspace?.cycle(1) }
                    .keyboardShortcut(.tab, modifiers: .control)
                Button("Previous Repository") { workspace?.cycle(-1) }
                    .keyboardShortcut(.tab, modifiers: [.control, .shift])
                Divider()
                ForEach(Array((workspace?.repos ?? []).prefix(9).enumerated()), id: \.element.id) { index, store in
                    Button(store.name) { workspace?.selectIndex(index) }
                        .keyboardShortcut(KeyEquivalent(Character("\(index + 1)")))
                }
            }.disabled(workspace?.repos.isEmpty ?? true)
            Divider()
            Button("Refresh") { workspace?.selected?.requestRefresh() }
                .keyboardShortcut("r").disabled(workspace?.selected == nil)
            Button("Fetch") { workspace?.selected?.fetch() }
                .keyboardShortcut("f", modifiers: [.command, .shift]).disabled(workspace?.selected == nil)
            Button("Pull") { workspace?.selected?.pull() }
                .keyboardShortcut("l", modifiers: [.command, .shift]).disabled(workspace?.selected == nil)
            Button("Push") { workspace?.selected?.push() }
                .keyboardShortcut("p", modifiers: [.command, .shift]).disabled(workspace?.selected == nil)
            if let store = workspace?.selected, store.webLinks != nil {
                Divider()
                Button("Open on \(store.providerTitle ?? "Web")") { store.openOnWeb(.repository) }
                    .keyboardShortcut("g", modifiers: [.command, .shift])
                Button("Open Branch on \(store.providerTitle ?? "Web")") { store.openOnWeb(.branch) }
                Button("Create \(store.webLinks?.pullRequestsWord ?? "Pull Request") on \(store.providerTitle ?? "Web")") { store.openOnWeb(.pullRequest) }
            }
            Divider()
            Button("Undo Last Change…") { workspace?.selected?.undoLastChange() }
                .keyboardShortcut("z", modifiers: [.command, .option]).disabled(workspace?.selected == nil)
            Button("Compare Refs…") { workspace?.selected?.openCompare() }
                .keyboardShortcut("c", modifiers: [.command, .option]).disabled(workspace?.selected == nil)
            Button("Pull (Fast-forward Only)") { workspace?.selected?.pull(strategy: .fastForwardOnly) }.disabled(workspace?.selected == nil)
            Button("Pull (Rebase)") { workspace?.selected?.pull(strategy: .rebase) }.disabled(workspace?.selected == nil)
            Button("Force Push (with Lease)…") { workspace?.selected?.confirmForcePush() }.disabled(workspace?.selected == nil)
            Button("Worktrees & Submodules…") { workspace?.selected?.sheet = .worktrees }.disabled(workspace?.selected == nil)
            Button("Create Patch from Changes…") { workspace?.selected?.createPatchFromChanges() }.disabled(workspace?.selected == nil)
            Button("Apply Patch…") { workspace?.selected?.applyPatches() }.disabled(workspace?.selected == nil)
            Button("Stop Bisect") { workspace?.selected?.stopBisect() }.disabled(workspace?.selected?.bisect.isActive != true)
            Divider()
            Button("Show in Finder") { workspace?.selected?.openInFinder() }
                .keyboardShortcut("r", modifiers: [.command, .shift, .option]).disabled(workspace?.selected == nil)
            Button("Open in Terminal") { workspace?.selected?.openInTerminal() }
                .keyboardShortcut("t", modifiers: [.command, .option]).disabled(workspace?.selected == nil)
            Button("Open in \(CustomActionStore.editorApp)") { workspace?.selected?.openInEditor() }
                .keyboardShortcut("e", modifiers: [.command, .option]).disabled(workspace?.selected == nil)
            Menu("Custom Actions") {
                ForEach(CustomActionStore.load().filter(\.isValid)) { action in
                    Button(action.name) { workspace?.selected?.run(action) }
                }
            }.disabled(workspace?.selected == nil)
            Divider()
            Button("Manage Remotes…") { workspace?.selected?.sheet = .remotes }.disabled(workspace?.selected == nil)
            Divider()
            Button("Clean Up Branches…") { workspace?.selected?.sheet = .cleanup }.disabled(workspace?.selected == nil)
            Button("Reflog…") { workspace?.selected?.sheet = .reflog }.disabled(workspace?.selected == nil)
            Button("Statistics…") { workspace?.selected?.sheet = .insights }.disabled(workspace?.selected == nil)
            Divider()
            Button("Show History") { workspace?.selected?.mode = .history }
                .keyboardShortcut("1", modifiers: [.command, .option]).disabled(workspace?.selected == nil)
            Button("Show Changes") { workspace?.selected?.mode = .changes }
                .keyboardShortcut("2", modifiers: [.command, .option]).disabled(workspace?.selected == nil)
            if let store = workspace?.selected, store.supports(.pullRequest) {
                Button("Show \(store.pullRequestsTitle)") { store.mode = .pullRequests }
            }
            if let store = workspace?.selected, store.supports(.issue) {
                Button("Show Issues") { store.mode = .issues }
            }
        }
    }
}
