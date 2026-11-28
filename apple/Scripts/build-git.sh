#!/usr/bin/env bash
# Builds the git that ships inside the Mac App Store build: upstream git from kernel.org, checked against the
# published SHA-256, relocatable (it finds its helper programs next to itself), with no Perl, Python, Tcl or
# gettext. Output: build/git/ (bin/, libexec/, share/). Needs Xcode. git is GPLv2: see docs/APPSTORE.md.
#
#   Scripts/build-git.sh [version]      default 2.56.1;  ARCHS="arm64" for a quick single-architecture build
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"
VERSION="${1:-2.56.1}"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export MACOSX_DEPLOYMENT_TARGET=15.0
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
BASE=https://mirrors.edge.kernel.org/pub/software/scm/git
curl -fsSL "$BASE/git-$VERSION.tar.xz" -o "$WORK/git.tar.xz"
curl -fsSL "$BASE/sha256sums.asc" -o "$WORK/sums.asc"
EXPECTED="$(grep " git-$VERSION.tar.xz" "$WORK/sums.asc" | head -1 | awk '{print $1}')"
ACTUAL="$(shasum -a 256 "$WORK/git.tar.xz" | awk '{print $1}')"
[ -n "$EXPECTED" ] && [ "$EXPECTED" = "$ACTUAL" ] || { echo "checksum mismatch for git-$VERSION" >&2; exit 1; }
tar -xf "$WORK/git.tar.xz" -C "$WORK"
cd "$WORK/git-$VERSION"
ARCHFLAGS=(); for a in ${ARCHS:-arm64 x86_64}; do ARCHFLAGS+=(-arch "$a"); done
FLAGS=(COMPUTE_HEADER_DEPENDENCIES=no prefix=/usr/local RUNTIME_PREFIX=1 NO_RUST=1 NO_GETTEXT=1 NO_TCLTK=1 NO_PERL=1 NO_PYTHON=1 NO_EXPAT=1 NO_INSTALL_HARDLINKS=1)
make -j"$(sysctl -n hw.ncpu)" "${FLAGS[@]}" CFLAGS="-O2 ${ARCHFLAGS[*]}" LDFLAGS="${ARCHFLAGS[*]}" all
make "${FLAGS[@]}" CFLAGS="-O2 ${ARCHFLAGS[*]}" LDFLAGS="${ARCHFLAGS[*]}" DESTDIR="$WORK/stage" install  # same flags, or make rebuilds thin
OUT="$ROOT/build/git"
rm -rf "$OUT"; mkdir -p "$OUT"
cp -Rp "$WORK/stage/usr/local/bin" "$OUT/bin"
cp -Rp "$WORK/stage/usr/local/libexec" "$OUT/libexec"
[ -d "$WORK/stage/usr/local/share/git-core" ] && mkdir -p "$OUT/share" && cp -Rp "$WORK/stage/usr/local/share/git-core" "$OUT/share/git-core"
cp "$WORK/git-$VERSION/COPYING" "$OUT/COPYING"
echo "$VERSION" > "$OUT/VERSION"
echo "Built git $VERSION into $OUT"
