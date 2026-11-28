// swift-tools-version: 6.0
import PackageDescription
import Foundation

// `GITWOLF_APPSTORE=1 swift build` makes the Mac App Store flavour: no Sparkle (the store updates apps itself)
// and the APPSTORE compile flag. See Scripts/bundle-appstore.sh.
let appStore = ProcessInfo.processInfo.environment["GITWOLF_APPSTORE"] == "1"

let package = Package(
    name: "GitWolf",
    platforms: [.macOS(.v15)],
    products: [
        .executable(name: "GitWolf", targets: ["GitWolfApp"]),
        .library(name: "GitWolfCore", targets: ["GitWolfCore"]),
    ],
    dependencies: appStore ? [] : [
        // In-app updates: downloads, verifies (EdDSA) and installs new releases. See docs/RELEASING.md.
        .package(url: "https://github.com/sparkle-project/Sparkle", from: "2.7.0"),
    ],
    targets: [
        // Git engine: process runner, parsers, graph layout, file watching.
        // Foundation + CoreServices only. No SwiftUI.
        .target(name: "GitWolfCore"),
        // SwiftUI application.
        .executableTarget(name: "GitWolfApp", dependencies: appStore ? ["GitWolfCore"] : [
            "GitWolfCore",
            .product(name: "Sparkle", package: "Sparkle"),
        ], swiftSettings: appStore ? [.define("APPSTORE")] : []),
        // Release tooling: update-signing keys, signing a release archive, writing the appcast.
        .executableTarget(name: "gitwolf-release", dependencies: ["GitWolfCore"], path: "Sources/GitWolfRelease"),
        .testTarget(name: "GitWolfCoreTests", dependencies: ["GitWolfCore"]),
    ]
)
