import AppKit
import Foundation
import GitWolfCore
import SwiftUI

enum AppearanceChoice: String, CaseIterable, Identifiable {
    case system, light, dark
    var id: String { rawValue }
    var title: String { rawValue.capitalized }
    /// Applies to every window at once; `nil` follows the system appearance.
    @MainActor func apply() {
        switch self {
        case .system: NSApp.appearance = nil
        case .light: NSApp.appearance = NSAppearance(named: .aqua)
        case .dark: NSApp.appearance = NSAppearance(named: .darkAqua)
        }
    }
}

/// Accent colour presets.
enum AccentChoice: String, CaseIterable, Identifiable {
    /// The blue of the logo and the default accent.
    case wolf, indigo, blue, purple, pink, red, orange, green, teal
    var id: String { rawValue }
    var title: String { self == .wolf ? "GitWolf" : rawValue.capitalized }

    var primary: Color {
        switch self {
        // #012AA5 on light backgrounds; the same hue, lighter, on dark ones where the logo blue would sink into the surface.
        case .wolf:
            Color(nsColor: NSColor(name: nil) { appearance in
                appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
                    ? NSColor(srgbRed: 0.20, green: 0.40, blue: 1.0, alpha: 1)
                    : NSColor(srgbRed: 1 / 255, green: 42 / 255, blue: 165 / 255, alpha: 1)
            })
        case .indigo: Color(red: 0.42, green: 0.38, blue: 0.98)
        case .blue: Color(red: 0.12, green: 0.50, blue: 1.0)
        case .purple: Color(red: 0.66, green: 0.36, blue: 0.96)
        case .pink: Color(red: 0.96, green: 0.30, blue: 0.58)
        case .red: Color(red: 0.94, green: 0.30, blue: 0.32)
        case .orange: Color(red: 0.98, green: 0.52, blue: 0.14)
        case .green: Color(red: 0.20, green: 0.70, blue: 0.40)
        case .teal: Color(red: 0.10, green: 0.68, blue: 0.70)
        }
    }
    static var current: AccentChoice {
        AccentChoice(rawValue: UserDefaults.standard.string(forKey: SettingsKey.accent) ?? "") ?? .wolf
    }
}

enum DiffLayout: String, CaseIterable, Identifiable {
    case unified, split
    var id: String { rawValue }
    var title: String { self == .unified ? "Unified" : "Side by Side" }
    var icon: String { self == .unified ? "text.alignleft" : "rectangle.split.2x1" }
}

/// Keys shared between `@AppStorage` in the settings UI and `AppSettings` readers.
enum SettingsKey {
    static let appearance = "settings.appearance"
    static let accent = "settings.accent"
    static let autoFetchMinutes = "settings.autoFetchMinutes"
    static let commitPageSize = "settings.commitPageSize"
    static let diffContext = "settings.diffContext"
    static let syntaxHighlighting = "settings.syntaxHighlighting"
    static let diffLayout = "settings.diffLayout"
    static let ignoreWhitespace = "settings.ignoreWhitespace"
    static let fullFileDiffs = "settings.fullFileDiffs"
    static let gitPath = "settings.gitPath"
    static let pullRebase = "settings.pullRebase"
}

/// Read-side access to user settings for non-view code. Always reads current values, so changes
/// apply immediately without restarting.
struct AppSettings: Sendable {
    static let shared = AppSettings()
    /// The git inside the app (the App Store build has one, because Apple's `/usr/bin/git` cannot run in the
    /// sandbox), otherwise the system one.
    static var defaultGitPath: String { BundledGit.path ?? "/usr/bin/git" }

    private var defaults: UserDefaults { .standard }

    var autoFetchMinutes: Int {
        defaults.object(forKey: SettingsKey.autoFetchMinutes) as? Int ?? 5
    }
    var commitPageSize: Int {
        max(100, defaults.object(forKey: SettingsKey.commitPageSize) as? Int ?? 1000)
    }
    var diffContext: Int {
        defaults.object(forKey: SettingsKey.diffContext) as? Int ?? 4
    }
    /// Pull by rebasing local commits onto the upstream instead of merging.
    var pullRebase: Bool { defaults.bool(forKey: SettingsKey.pullRebase) }
    var gitPath: String {
        let custom = (defaults.string(forKey: SettingsKey.gitPath) ?? "").trimmingCharacters(in: .whitespaces)
        guard !custom.isEmpty, FileManager.default.isExecutableFile(atPath: custom) else { return Self.defaultGitPath }
        return custom
    }

    func configure(_ repo: GitRepository) -> GitRepository {
        var r = repo
        r.gitPath = gitPath
        r.contextLines = diffContext
        r.ignoreWhitespace = defaults.bool(forKey: SettingsKey.ignoreWhitespace)
        r.fullFileDiffs = defaults.bool(forKey: SettingsKey.fullFileDiffs)
        r.credentials = { await AccountStorage.credential(forHost: $0) }
        r.askpassPath = AskPass.path
        return r
    }
}
