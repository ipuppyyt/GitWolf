#!/usr/bin/env bash
# Builds the Mac App Store flavour of GitWolf into build/appstore/GitWolf.app: sandboxed, no Sparkle, with its
# own git inside (Apple's /usr/bin/git cannot run in the sandbox). Prototype: see docs/APPSTORE.md.
#
#   Scripts/bundle-appstore.sh [release|debug]
#
# Environment:
#   SIGN_IDENTITY  codesign identity: "-" (ad hoc, the default, enough to run it locally) or an
#                  "Apple Distribution: …" identity for upload
#   VERSION        marketing version to stamp into Info.plist
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG="${1:-release}"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
IDENTITY="${SIGN_IDENTITY:--}"
TS=(); [ "$IDENTITY" != "-" ] && TS=(--timestamp)

[ -x build/git/bin/git ] || Scripts/build-git.sh
export GITWOLF_APPSTORE=1
SCRATCH=.build-appstore
swift build -c "$CONFIG" --product GitWolf --scratch-path "$SCRATCH"
BIN="$(swift build -c "$CONFIG" --scratch-path "$SCRATCH" --show-bin-path)/GitWolf"

APP=build/appstore/GitWolf.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/GitWolf"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
for lproj in Resources/*.lproj; do [ -d "$lproj" ] && cp -R "$lproj" "$APP/Contents/Resources/"; done

PL="$APP/Contents/Info.plist"
# No update feed: the App Store delivers updates.
/usr/libexec/PlistBuddy -c "Delete :SUFeedURL" "$PL" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Delete :SUPublicEDKey" "$PL" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Add :LSApplicationCategoryType string public.app-category.developer-tools" "$PL" 2>/dev/null || true
# Only standard HTTPS (the system's) is used, which needs no export-compliance paperwork.
/usr/libexec/PlistBuddy -c "Add :ITSAppUsesNonExemptEncryption bool false" "$PL" 2>/dev/null || true
if [ -n "${VERSION:-}" ]; then
  BUILD="$(swift run --scratch-path .build -c "$CONFIG" gitwolf-release build-number "$VERSION")"
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$PL"
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $BUILD" "$PL"
fi

# git and the programs it starts, then the licence that goes with them.
GIT="$APP/Contents/Resources/git"
mkdir -p "$GIT"
ditto build/git/bin "$GIT/bin"
ditto build/git/libexec "$GIT/libexec"
[ -d build/git/share ] && ditto build/git/share "$GIT/share"
cp build/git/COPYING "$APP/Contents/Resources/git-COPYING.txt"
# The sandbox will not run a script the app wrote into its container, so the GIT_ASKPASS helper ships
# inside the signed bundle. Keep in step with GitCredential.askpassScript (Providers.swift).
cat > "$APP/Contents/Resources/askpass.sh" <<'ASK'
#!/bin/sh
case "$1" in
  Username*|username*) printf '%s\n' "$GITWOLF_ASKPASS_USER" ;;
  *) printf '%s\n' "$GITWOLF_ASKPASS_TOKEN" ;;
esac
ASK
chmod 755 "$APP/Contents/Resources/askpass.sh"

# Sign inside-out. Programs the app starts run in its sandbox ("inherit"); the app itself gets the sandbox.
while IFS= read -r f; do
  if file "$f" | grep -q "Mach-O"; then
    codesign --force --sign "$IDENTITY" ${TS[@]+"${TS[@]}"} --entitlements Resources/GitWolf-Helper.entitlements "$f"
  fi
done < <(find "$GIT" -type f)
codesign --force --sign "$IDENTITY" ${TS[@]+"${TS[@]}"} --entitlements Resources/GitWolf-AppStore.entitlements "$APP"
codesign --verify --deep --strict "$APP"
echo "Built $APP (signed as: $IDENTITY)"
