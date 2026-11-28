import AppKit
import GitWolfCore
import SwiftUI

/// The first-run walkthrough, also reachable from Help > Welcome to GitWolf: check Git, add an account, open
/// repositories, pick a look. Every step can be skipped.
struct WelcomeSheet: View {
    let workspace: Workspace
    @Environment(\.dismiss) private var dismiss
    @State private var step = Step.welcome
    @State private var addingAccount = false
    @State private var accounts = AccountStore.shared
    @State private var scanned: [URL] = []
    @State private var chosen: Set<URL> = []
    @State private var scanning = false
    @State private var scannedFolder: URL?
    @AppStorage(SettingsKey.appearance) private var appearance = AppearanceChoice.system.rawValue
    @AppStorage(SettingsKey.accent) private var accent = AccentChoice.wolf.rawValue
    @State private var gitReady = WelcomeSheet.gitIsAvailable()

    static let seenKey = "welcomeSeen"

    enum Step: Int, CaseIterable { case welcome, accounts, repositories, look }

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch step {
                case .welcome: welcome
                case .accounts: accountsStep
                case .repositories: repositoriesStep
                case .look: lookStep
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding(.horizontal, 36).padding(.top, 30)
            .transition(.opacity)
            .id(step)

            footer
        }
        .frame(width: 540, height: 560)
        .sheet(isPresented: $addingAccount) { AddAccountSheet() }
        .onDisappear { UserDefaults.standard.set(true, forKey: Self.seenKey) }
    }

    // MARK: Steps

    private var welcome: some View {
        VStack(spacing: 16) {
            if let art = BrandArt.icon { Image(nsImage: art).resizable().scaledToFit().frame(width: 112, height: 112).accessibilityHidden(true) }
            Text("Welcome to GitWolf").font(.system(size: 28, weight: .bold, design: .rounded))
            Text("A fast, clean, native Git client.").font(.title3).foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 12) {
                point("arrow.triangle.2.circlepath", "Always in sync", "Changes made anywhere, even in a terminal, show up at once.")
                point("bolt.fill", "Fast and native", "Built for the Mac, with no web view and no jank.")
                point("terminal.fill", "Uses your Git", "Every action runs the Git you already have, with your config and hooks.")
            }
            .padding(.top, 8)
            Spacer(minLength: 0)
            gitStatus
        }
    }

    private var gitStatus: some View {
        HStack(spacing: 8) {
            Image(systemName: gitReady ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                .foregroundStyle(gitReady ? Color.green : Color.orange)
            if gitReady {
                Text("Git is installed.").font(.callout)
            } else {
                Text("GitWolf needs Git, which comes with Apple's command line tools.").font(.callout)
                Button("Install") { installCommandLineTools() }.controlSize(.small)
                Button("Check Again") { gitReady = Self.gitIsAvailable() }.controlSize(.small)
            }
        }
        .padding(.bottom, 14)
    }

    private var accountsStep: some View {
        VStack(spacing: 14) {
            header("person.crop.circle.badge.checkmark", "Connect your accounts",
                   "Sign in to GitHub, GitLab or another host to clone private repositories, see checks and open pull requests. You can do this later in Settings.")
            VStack(spacing: 0) {
                if accounts.accounts.isEmpty {
                    Text("No accounts yet.").font(.callout).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading).padding(14)
                }
                ForEach(Array(accounts.accounts.enumerated()), id: \.element.id) { index, account in
                    if index > 0 { Divider().padding(.leading, 54) }
                    HStack(spacing: 12) {
                        ProviderTile(kind: account.kind)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(account.kind.title).fontWeight(.medium)
                            Text(account.username.isEmpty ? account.host : "@\(account.username) · \(account.host)")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                    }
                    .padding(.horizontal, 12).padding(.vertical, 9)
                }
            }
            .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.primary.opacity(0.08)))
            Button { addingAccount = true } label: {
                Label(accounts.accounts.isEmpty ? "Add an Account…" : "Add Another Account…", systemImage: "plus")
                    .font(.body.weight(.semibold)).padding(.horizontal, 6).frame(height: 28)
            }
            .buttonStyle(.borderedProminent).controlSize(.large)
            Text("Tokens are kept in your macOS Keychain and never written into URLs or config files.")
                .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
            Spacer(minLength: 0)
        }
    }

    private var repositoriesStep: some View {
        VStack(spacing: 14) {
            header("folder.badge.plus", "Add your repositories",
                   "Open one, clone one, or pick a folder and GitWolf finds the repositories inside it.")
            if scanning {
                ProgressView("Looking for repositories…").frame(maxHeight: .infinity)
            } else if let folder = scannedFolder {
                scanResults(in: folder)
            } else {
                VStack(spacing: 10) {
                    actionButton("Find Repositories in a Folder…", "magnifyingglass", prominent: true) { chooseFolderToScan() }
                    HStack(spacing: 10) {
                        actionButton("Open Repository…", "folder") { finish(); workspace.openPanel() }
                        actionButton("Clone…", "square.and.arrow.down") { finish(); workspace.sheet = .clone }
                    }
                }
                Spacer(minLength: 0)
            }
        }
    }

    private func scanResults(in folder: URL) -> some View {
        VStack(spacing: 10) {
            HStack {
                Text(scanned.isEmpty ? "No repositories found in \(folder.lastPathComponent)." : "Found \(scanned.count) in \(folder.lastPathComponent)")
                    .font(.callout.weight(.medium))
                Spacer()
                if !scanned.isEmpty {
                    Button(chosen.count == scanned.count ? "Select None" : "Select All") {
                        chosen = chosen.count == scanned.count ? [] : Set(scanned)
                    }.buttonStyle(.link)
                }
            }
            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(scanned, id: \.self) { url in
                        Toggle(isOn: Binding(get: { chosen.contains(url) }, set: { on in
                            if on { chosen.insert(url) } else { chosen.remove(url) }
                        })) {
                            VStack(alignment: .leading, spacing: 0) {
                                Text(url.lastPathComponent).fontWeight(.medium)
                                Text(url.deletingLastPathComponent().path.replacingOccurrences(of: NSHomeDirectory(), with: "~"))
                                    .font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.head)
                            }
                        }
                        .toggleStyle(.checkbox)
                        .padding(.horizontal, 12).padding(.vertical, 6)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
            .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            HStack {
                Button("Choose Another Folder…") { chooseFolderToScan() }
                Spacer()
                Button("Open \(chosen.count) \(chosen.count == 1 ? "Repository" : "Repositories")") {
                    openChosen()
                }
                .buttonStyle(.borderedProminent).disabled(chosen.isEmpty)
            }
        }
    }

    private var lookStep: some View {
        VStack(spacing: 22) {
            header("paintpalette.fill", "Make it yours", "Pick how GitWolf looks. You can change both in Settings any time.")

            HStack(spacing: 14) {
                ForEach(AppearanceChoice.allCases) { choice in
                    AppearanceCard(choice: choice, selected: appearance == choice.rawValue) {
                        appearance = choice.rawValue
                        choice.apply()
                    }
                }
            }

            VStack(spacing: 10) {
                HStack(spacing: 10) {
                    ForEach(AccentChoice.allCases) { choice in
                        let on = accent == choice.rawValue
                        Button { withAnimation(.snappy(duration: 0.15)) { accent = choice.rawValue } } label: {
                            Circle().fill(choice.primary).frame(width: 26, height: 26)
                                .overlay {
                                    Image(systemName: "checkmark").font(.system(size: 11, weight: .heavy)).foregroundStyle(.white)
                                        .opacity(on ? 1 : 0)
                                }
                                .padding(4)
                                .overlay(Circle().strokeBorder(choice.primary, lineWidth: 2).opacity(on ? 1 : 0))
                                .contentShape(Circle())
                        }
                        .buttonStyle(.plain).help(choice.title).accessibilityLabel(choice.title)
                        .accessibilityAddTraits(on ? .isSelected : [])
                    }
                }
                Text((AccentChoice(rawValue: accent) ?? .wolf).title).font(.callout.weight(.medium)).foregroundStyle(.secondary)
            }

            Spacer(minLength: 0)
            Text("You’re all set. Open the command palette any time with ⌘K.").font(.callout).foregroundStyle(.secondary)
                .padding(.bottom, 14)
        }
    }

    // MARK: Pieces

    private var footer: some View {
        VStack(spacing: 0) {
            Divider()
            HStack {
                if step != .welcome { Button("Back") { go(-1) } } else { Button("Skip") { finish() }.buttonStyle(.link) }
                Spacer()
                HStack(spacing: 6) {
                    ForEach(Step.allCases, id: \.rawValue) { s in
                        Circle().fill(s == step ? Theme.brand : Color.primary.opacity(0.18)).frame(width: 7, height: 7)
                    }
                }
                Spacer()
                if step == .look {
                    Button("Get Started") { finish() }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
                } else {
                    Button("Continue") { go(1) }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
                }
            }
            .controlSize(.large)
            .padding(.horizontal, 20).frame(height: 62)
        }
    }

    private func header(_ symbol: String, _ title: String, _ detail: String) -> some View {
        VStack(spacing: 8) {
            Image(systemName: symbol).font(.system(size: 30, weight: .medium)).foregroundStyle(Theme.brand).frame(height: 40)
            Text(title).font(.title.weight(.bold))
            Text(detail).font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center).fixedSize(horizontal: false, vertical: true)
        }
        .padding(.bottom, 4)
    }

    private func point(_ symbol: String, _ title: String, _ detail: String) -> some View {
        HStack(alignment: .top, spacing: 14) {
            Image(systemName: symbol).font(.system(size: 17)).foregroundStyle(Theme.brand).frame(width: 26)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).fontWeight(.semibold)
                Text(detail).font(.callout).foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: 400, alignment: .leading)
    }

    private func actionButton(_ title: String, _ symbol: String, prominent: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label(title, systemImage: symbol).font(.body.weight(.semibold)).padding(.horizontal, 6)
                .frame(maxWidth: .infinity).frame(height: 28)
        }
        .controlSize(.large)
        .modifier(ProminenceStyle(prominent: prominent))
    }

    // MARK: Actions

    private func go(_ delta: Int) {
        guard let next = Step(rawValue: step.rawValue + delta) else { return }
        withAnimation(.snappy(duration: 0.2)) { step = next }
    }

    private func finish() {
        UserDefaults.standard.set(true, forKey: Self.seenKey)
        dismiss()
    }

    private func chooseFolderToScan() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.prompt = "Search"
        panel.message = "Choose a folder that contains your repositories"
        panel.directoryURL = FileManager.default.urls(for: .developerDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        guard panel.runModal() == .OK, let folder = panel.url else { return }
        scanning = true
        Task {
            let found = await Task.detached { RepositoryScanner.find(in: folder) }.value
            scanned = found
            chosen = Set(found)
            scannedFolder = folder
            scanning = false
        }
    }

    private func openChosen() {
        let urls = scanned.filter { chosen.contains($0) }
        urls.forEach { workspace.open($0) }
        finish()
    }

    private func installCommandLineTools() {
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/bin/xcode-select")
        task.arguments = ["--install"]
        try? task.run()
    }

    /// Git is there when Apple's command line tools are installed, or a custom Git was set. This does not run
    /// `/usr/bin/git`, which would pop up the installer on a Mac without the tools.
    static func gitIsAvailable() -> Bool {
        if BundledGit.path != nil { return true }
        let custom = (UserDefaults.standard.string(forKey: SettingsKey.gitPath) ?? "").trimmingCharacters(in: .whitespaces)
        if !custom.isEmpty, FileManager.default.isExecutableFile(atPath: custom) { return true }
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/bin/xcode-select")
        task.arguments = ["-p"]
        task.standardOutput = FileHandle.nullDevice
        task.standardError = FileHandle.nullDevice
        guard (try? task.run()) != nil else { return false }
        task.waitUntilExit()
        return task.terminationStatus == 0
    }
}

/// Prominent (filled) or regular (bordered) button, chosen at runtime.
private struct ProminenceStyle: ViewModifier {
    let prominent: Bool
    func body(content: Content) -> some View {
        if prominent { content.buttonStyle(.borderedProminent) } else { content.buttonStyle(.bordered) }
    }
}

/// A flat miniature window in light, dark or the system's look, to choose an appearance by sight.
private struct AppearanceCard: View {
    let choice: AppearanceChoice
    let selected: Bool
    let action: () -> Void
    @State private var hovering = false

    private func mock(dark: Bool) -> some View {
        let bg = dark ? Color(white: 0.13) : Color(white: 0.97)
        let panel = dark ? Color(white: 0.20) : Color(white: 0.89)
        let line = dark ? Color(white: 0.34) : Color(white: 0.74)
        return ZStack(alignment: .topLeading) {
            bg
            RoundedRectangle(cornerRadius: 3).fill(panel).frame(width: 22, height: 48).padding(.leading, 8).padding(.top, 20)
            VStack(alignment: .leading, spacing: 5) {
                RoundedRectangle(cornerRadius: 2).fill(Theme.brand).frame(width: 34, height: 6)
                RoundedRectangle(cornerRadius: 2).fill(line).frame(width: 56, height: 4)
                RoundedRectangle(cornerRadius: 2).fill(line).frame(width: 44, height: 4)
                RoundedRectangle(cornerRadius: 2).fill(line).frame(width: 50, height: 4)
            }
            .padding(.leading, 38).padding(.top, 26)
            HStack(spacing: 3) { ForEach(0..<3, id: \.self) { _ in Circle().fill(line).frame(width: 5, height: 5) } }
                .padding(.leading, 8).padding(.top, 7)
        }
        .environment(\.colorScheme, dark ? .dark : .light)
    }

    var body: some View {
        Button(action: action) {
            VStack(spacing: 8) {
                Group {
                    switch choice {
                    case .light: mock(dark: false)
                    case .dark: mock(dark: true)
                    case .system:
                        // Light on the left, dark on the right.
                        mock(dark: false).overlay(alignment: .trailing) {
                            mock(dark: true).frame(width: 60, alignment: .trailing).clipped()
                        }
                    }
                }
                .frame(width: 120, height: 76)
                .clipShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .strokeBorder(selected ? Theme.brand : Color.primary.opacity(hovering ? 0.28 : 0.14), lineWidth: selected ? 2.5 : 1))
                Text(choice.title).font(.callout.weight(selected ? .semibold : .regular))
                    .foregroundStyle(selected ? Color.primary : Color.secondary)
            }
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .accessibilityLabel("\(choice.title) appearance")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}
