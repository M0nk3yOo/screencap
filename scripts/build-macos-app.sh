#!/usr/bin/env bash
# Builds a self-contained Screencap.app for macOS.
# Running as a real app bundle matters: macOS ties the Screen Recording permission
# to the bundle, not to the terminal that launched it.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID=osx-arm64 # Apple Silicon only; there are no Intel builds.

export PATH="$HOME/.dotnet:$PATH"
OUT="$ROOT/artifacts/macos"
APP="$OUT/Screencap.app"
PUBLISH="$OUT/publish-$RID"

rm -rf "$APP" "$PUBLISH"
dotnet publish "$ROOT/src/Screencap.App/Screencap.App.csproj" -c Release -f net10.0 -r "$RID" --self-contained \
  -p:UseAppHost=true -o "$PUBLISH" -v quiet -nologo

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
# App icon (Dock, Finder) from assets/AppIcon.png; regenerate with scripts/render-icon.cs.
ICONSET="$OUT/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$ROOT/assets/AppIcon.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$ROOT/assets/AppIcon.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Screencap</string>
  <key>CFBundleDisplayName</key><string>Screencap</string>
  <key>CFBundleIdentifier</key><string>app.screencap.Screencap</string>
  <key>CFBundleExecutable</key><string>Screencap</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>NSHumanReadableCopyright</key><string>© 2026 Screencap</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <!-- Menu bar app: no Dock icon, no app menu. -->
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# Sign with a stable identity if available, so macOS keeps the Screen Recording
# permission across rebuilds. Create it once with scripts/create-dev-certificate.sh.
# Without it, an ad-hoc signature is used and macOS asks again after every rebuild.
IDENTITY="${SCREENCAP_SIGN_IDENTITY:-Screencap Dev}"
if security find-identity -p codesigning 2>/dev/null | grep -q "\"$IDENTITY\""; then
  codesign --force --deep --sign "$IDENTITY" "$APP"
else
  echo "Signing identity '$IDENTITY' not found, using ad-hoc signature." >&2
  codesign --force --deep --sign - "$APP"
fi
echo "$APP"
