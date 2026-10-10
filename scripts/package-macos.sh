#!/usr/bin/env bash
# Builds Screencap.app for one architecture and packs it into a DMG with an
# Applications shortcut. Usage: scripts/package-macos.sh [arm64|x86_64]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-$(uname -m)}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
case "$ARCH" in arm64) LABEL=apple-silicon ;; x86_64) LABEL=intel ;; *) echo "Unknown arch $ARCH" >&2; exit 1 ;; esac

APP="$("$ROOT/scripts/build-macos-app.sh" "$ARCH" | tail -1)"
OUT="$ROOT/artifacts/release"
STAGE="$ROOT/artifacts/dmg-$ARCH"
DMG="$OUT/Screencap-$VERSION-macos-$LABEL.dmg"
mkdir -p "$OUT"
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Screencap $VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG" >/dev/null
rm -rf "$STAGE"
echo "$DMG"
