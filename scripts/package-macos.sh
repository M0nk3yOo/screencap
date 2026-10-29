#!/usr/bin/env bash
# Builds Screencap.app for Apple Silicon and packs it into a DMG with an
# Applications shortcut. Usage: scripts/package-macos.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH=arm64
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
LABEL=apple-silicon

APP="$("$ROOT/scripts/build-macos-app.sh" | tail -1)"
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
