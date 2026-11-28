import AppKit
#if !APPSTORE
import Sparkle
#endif

/// In-app updates with Sparkle: it checks the update feed, downloads the new version, verifies its EdDSA
/// signature against the public key in Info.plist, replaces the app and relaunches it. Nothing is sent
/// about the user or their repositories. A build run with `swift run` has no feed, so it never checks.
#if APPSTORE
/// The Mac App Store updates the app itself, so there is nothing to check or download here.
@MainActor
enum UpdateManager {
    static let autoKey = "SUEnableAutomaticChecks"
    static var isPackaged: Bool { false }
    static func start() {}
    static func checkForUpdates() {}
    static func setAutomaticChecks(_ on: Bool) {}
}
#else
@MainActor
enum UpdateManager {
    /// Sparkle's own preference for checking automatically (once a day); the Settings toggle writes it.
    static let autoKey = "SUEnableAutomaticChecks"
    private static var controller: SPUStandardUpdaterController?

    /// Only a packaged app has the feed address and public key in its Info.plist.
    static var isPackaged: Bool {
        Bundle.main.object(forInfoDictionaryKey: "SUFeedURL") != nil && Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") != nil
    }

    static func start() {
        guard isPackaged, controller == nil else { return }
        controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)
    }

    static func checkForUpdates() {
        guard let controller else {
            let alert = NSAlert()
            alert.messageText = "This is a development build"
            alert.informativeText = "Updates are only available in the packaged app."
            alert.runModal()
            return
        }
        controller.checkForUpdates(nil)
    }

    static func setAutomaticChecks(_ on: Bool) { controller?.updater.automaticallyChecksForUpdates = on }
}
#endif
