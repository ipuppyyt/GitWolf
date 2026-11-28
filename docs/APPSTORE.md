# Mac App Store build (prototype)

Status: prototype. Not submitted, not committed as a release path.

## Why a separate build

The App Store requires the App Sandbox. In the sandbox `/usr/bin/git` fails, because Apple's stub
calls `xcrun`, which refuses to run there. So the App Store build ships its own git.

## Pieces

- `apple/Scripts/build-git.sh`: downloads upstream git (checksum verified) and builds a relocatable
  copy (`RUNTIME_PREFIX=1`, no Rust, Perl, Python, gettext or Tcl) into `apple/build/git`.
- `apple/Scripts/bundle-appstore.sh`: builds with `GITWOLF_APPSTORE=1` (drops Sparkle, defines
  `APPSTORE`), embeds git in `Contents/Resources/git`, signs git binaries with
  `Resources/GitWolf-Helper.entitlements` (sandbox + inherit) and the app with
  `Resources/GitWolf-AppStore.entitlements`. Ad hoc signed unless `SIGN_IDENTITY` is set.
- `BundledGit.swift`: finds the bundled git; `FolderAccess` keeps security-scoped bookmarks so
  granted folders survive relaunch.
- `askpass.sh` ships inside the bundle: the sandbox refuses to run a script the app writes at runtime.
- `GitRunner.defaultGitPath`: the app sets this at launch so every git call uses the bundled git.
- In the sandbox git uses `GIT_CONFIG_NOSYSTEM=1` and a global config inside the container.

## Verified

- Bundled git builds, links only system libraries, runs inside the sandboxed app.
- The sandboxed app opens a repository passed through Finder/`open`.
- In a sandboxed probe: commit, log, HTTPS clone and ls-remote, and authentication through the askpass helper.
- The git build is universal (arm64 + x86_64).

## Not verified yet

- Staging, commit and push with a real token from the app's own UI in the sandbox; folder access after relaunch.
- Real signing with App Store identities, upload, and review.

## Known limits

- HTTPS remotes only (decided): `~/.ssh` is unreachable in the sandbox, and so is the user's `~/.gitconfig`.
- Every folder needs one grant through the open panel.
- No in-app updates; the App Store delivers them.

## Licence

git is GPLv2. Shipping it inside an App Store app conflicts with the App Store terms (VLC was
removed for the same reason). Get a legal opinion before submitting. The bundle includes
`git-COPYING.txt`.
