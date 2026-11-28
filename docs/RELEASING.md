# Releasing GitWolf

GitWolf updates itself with [Sparkle](https://sparkle-project.org). Releases are notarised (see Signing and
notarisation below), but you can also build and sign without an Apple account. Two things keep updates safe
and smooth.

- **Update signature (EdDSA).** Every update archive is signed with a private key only you hold. The app
  carries the matching public key (`SUPublicEDKey` in `Resources/Info.plist`) and refuses an update that
  does not verify. This is what protects users, not Apple's notary.
- **A stable code-signing certificate.** Every release is signed with the same certificate (Developer ID for
  published releases; "GitWolf Release", self-signed, for builds without an Apple account), so macOS sees the
  same app after an update and keeps its Keychain access and permission grants. (Ad-hoc signatures change on
  every build and would re-prompt.)

Updates downloaded by the app are not quarantined by macOS, so they open without any Gatekeeper dialog.

## How it fits together

| Piece | Where |
| --- | --- |
| Archive the updater downloads (`GitWolf-<v>.zip`), first-install disk image (`.dmg`), `.sha256` | GitHub Release for tag `v<v>` |
| The feed (`appcast.xml`, newest first, last 20 releases) | GitHub Pages, branch `gh-pages`: `https://ipuppyyt.github.io/GitWolf/appcast.xml` |
| Build, sign, package, feed entry | `apple/Scripts/release.sh`, run by `.github/workflows/release.yml` on a `v*` tag |
| Update key tool | `swift run gitwolf-release` (`keys`, `appcast`, `build-number`) |

Sparkle compares the build number (`CFBundleVersion`), which `bundle.sh` derives from the version
(`1.2.3` becomes `1002003`), so versions must be plain `MAJOR.MINOR.PATCH`.

## One-time setup

1. **Update key.** `cd apple && swift run gitwolf-release keys generate` stores a new Ed25519 key in your
   login Keychain and prints the public key. Put the public key in `Resources/Info.plist` as
   `SUPublicEDKey` (already done for the project's key). **Back the private key up**
   (`swift run gitwolf-release keys export key.txt`, keep it in a password manager). If it is lost, installed
   copies can never be updated again and users must reinstall.
2. **Signing certificate.** `cd apple && Scripts/make-signing-cert.sh` creates the "GitWolf Release"
   certificate in your login keychain and writes `build/signing/certificate.p12.base64` and
   `certificate.password`. Back these up too: a different certificate later changes the app's identity, so
   Keychain items and permissions would be asked for once more.
3. **Repository secrets** (Settings > Secrets and variables > Actions):
   `MACOS_CERTIFICATE_P12` (contents of `certificate.p12.base64`), `MACOS_CERTIFICATE_PASSWORD`
   (contents of `certificate.password`), `GITWOLF_UPDATE_KEY` (contents of the exported key file). Delete
   the exported files afterwards (`build/` is git-ignored, but keep them out of backups you do not trust).
4. **GitHub Pages.** After the first release creates the `gh-pages` branch: Settings > Pages > Deploy from a
   branch > `gh-pages` / root.

## Releasing

Update `CHANGELOG.md` (a `## [1.2.3]` section becomes the notes in the update window), then:

```bash
git tag v1.2.3 && git push origin v1.2.3
```

CI tests, signs, publishes the release assets and updates the feed. Running users are offered the update
within a day (or at once through *GitWolf > Check for Updates…*).

Locally: `cd apple && Scripts/release.sh 1.2.3` writes everything to `apple/build/release/`
(`PREVIOUS_APPCAST` and `RELEASE_NOTES` are optional inputs, see the script header). Secrets are never put in
command lines: the update key is read from the Keychain or from `GITWOLF_UPDATE_KEY`, and CI imports the
certificate with `security -i` reading from stdin.

## Homebrew and the website

`Scripts/make-cask.sh <version>` writes the Homebrew cask for a release (it clears the quarantine flag, so Homebrew users skip the
*Open Anyway* step) and CI attaches it to the release as `gitwolf.rb`. To let people run `brew install --cask ipuppyyt/gitwolf/gitwolf`:

1. Create a public repository named `homebrew-gitwolf` under the same owner.
2. Either copy `gitwolf.rb` from each release into its `Casks/` folder, or let CI do it: create a fine-grained token with
   *Contents: read and write* on that repository only and save it as the secret `HOMEBREW_TAP_TOKEN`. Without the secret the step is skipped.

The website in `site/` is copied to the `gh-pages` branch by the same job, next to the feed, and is served at
`https://ipuppyyt.github.io/GitWolf/`. `assets/social-preview.svg` is the repository's social image; GitHub only accepts PNG or JPEG, so render
it once (1280 x 640) and upload it under Settings > General > Social preview.

## Signing and notarisation

Published releases are signed with a **Developer ID Application** certificate and notarised by Apple, so a
first install opens without any warning. The workflow needs these repository secrets: `MACOS_CERTIFICATE_P12`
and `MACOS_CERTIFICATE_PASSWORD` (the certificate and its key), `NOTARY_KEY_P8`, `NOTARY_KEY_ID` and
`NOTARY_ISSUER_ID` (an App Store Connect API key with the Developer role), and `GITWOLF_UPDATE_KEY`. The
workflow keeps the notarisation profile in its temporary keychain and passes `NOTARY_PROFILE` to `release.sh`,
which builds with the hardened runtime, then notarises and staples the app and the disk image.

Locally, store a profile once with `xcrun notarytool store-credentials gitwolf-notary` and run
`SIGN_IDENTITY="Developer ID Application: …" NOTARY_PROFILE=gitwolf-notary Scripts/release.sh <version>`.

Without `NOTARY_PROFILE` (a build for yourself, or signing with the self-signed certificate) the disk image
carries a *Read Me First* note: macOS says it cannot verify the app, and *Open Anyway* in System Settings >
Privacy & Security opens it once (or `xattr -dr com.apple.quarantine /Applications/GitWolf.app`).

Moving from the self-signed certificate to Developer ID changes the app's code-signing identity once. Updates
still verify, because Sparkle checks the EdDSA signature, but macOS may ask again for Keychain access.

## Version numbers

The version lives only in the tag/argument; `bundle.sh` stamps it into the bundle. Local builds without
`VERSION` keep `0.1.0` from `Resources/Info.plist`. Development builds (no feed in the bundle) say so
when you choose *Check for Updates…* instead of contacting anything.

## App icon

`swift Scripts/make-icon.swift` regenerates `Resources/AppIcon.icns` (flat indigo squircle, white branch
symbol, no gradients).

## Windows

The same tag releases Windows: the `windows` job in `release.yml` runs after the macOS one. No extra secret is
required, because the archive and the feed are signed with the same update key (`GITWOLF_UPDATE_KEY`).

To Authenticode-sign the Windows program, add `WINDOWS_CERTIFICATE_PFX` (base64 of a code-signing `.pfx`) and
`WINDOWS_CERTIFICATE_PASSWORD`. Without them the release is built unsigned, with a warning in the log. This
covers certificates held in a file; one kept on a hardware token or cloud service needs its own signing step.

| Piece | Where |
| --- | --- |
| `GitWolf-<v>-windows-x64-setup.exe` (first install) and `.sha256` | The same GitHub Release |
| `GitWolf-<v>-windows-x64.zip` (updates and portable use) and `.sha256` | The same GitHub Release |
| Installer script (Inno Setup) | `windows/installer/GitWolf.iss` |
| The feed (`appcast-windows.xml`, same format as macOS, plain-text notes) | `gh-pages`: `https://ipuppyyt.github.io/GitWolf/appcast-windows.xml` |
| Signing and feed entry | `windows/tools/GitWolf.Release` |

- The app embeds the public key in `windows/src/GitWolfCore/Updater.cs`. If the key changes, change it there
  and in `Info.plist`; the tool refuses to sign with a key that doesn't match.
- Only builds made with `-p:GitWolfUpdates=true -p:Version=1.2.3` check for updates.
- The feed ends in a signature over its contents; the app ignores a feed without a valid one, so don't edit
  it by hand.
- Nothing is unpacked until the signature verifies. A release is installed only if its version matches the
  feed and is newer, and the old files are renamed aside so that a failed install is undone.
- The zip may be unpacked anywhere the user can write to. Files that a new release no longer ships stay behind.
- First install: run the setup program, or unzip to a folder and run `GitWolf.App.exe`. If they are not
  Authenticode-signed, Windows SmartScreen asks (*More info > Run anyway*).
- The installer needs no administrator rights: it installs for the current user into
  `%LOCALAPPDATA%\Programs\GitWolf`, so in-app updates work. Uninstalling also removes the `gitwolf` command
  and link registration that point there, and keeps settings.
- After an in-app update, *Installed apps* still shows the version first installed, and uninstalling leaves any
  file that only a later release added. The uninstaller itself is not Authenticode-signed.

## For Linux

Mirror the shape: sign the package, publish it as a release asset of the same tag, and update in place
(AppImageUpdate or Flatpak).
