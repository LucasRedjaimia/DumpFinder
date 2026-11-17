#!/usr/bin/env bash
# Builds "Dump Finder.app" (Apple Silicon, self-contained: no .NET install needed) and a zip of it.
#
# Usage:  mac/build.sh            → mac/out/Dump Finder.app + mac/out/DumpFinder-mac-<version>.zip
#
# The version is read from mac/src/DumpFinder.Mac/DumpFinder.Mac.csproj (<Version>): the Mac app has its
# own version line, independent of the Windows exe.
#
# Signing: ad-hoc by default (runs on this Mac; a downloaded copy needs right-click › Open the first time).
# With a "Developer ID Application" certificate, set DEVELOPER_ID="Developer ID Application: Name (TEAMID)";
# add NOTARY_PROFILE=<keychain profile created with `xcrun notarytool store-credentials`> to notarize too.
set -euo pipefail

MAC="$(cd "$(dirname "$0")" && pwd)"
PROJ="$MAC/src/DumpFinder.Mac/DumpFinder.Mac.csproj"
OUT="$MAC/out"
APP="$OUT/Dump Finder.app"

DOTNET="$(command -v dotnet || true)"
[[ -z "$DOTNET" && -x "$HOME/.dotnet/dotnet" ]] && DOTNET="$HOME/.dotnet/dotnet"
[[ -z "$DOTNET" ]] && { echo "dotnet not found: install the .NET 10 SDK" >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

VERSION="$(sed -nE 's|.*<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>.*|\1|p' "$PROJ" | head -1)"
[[ -z "$VERSION" ]] && { echo "No <Version> in $PROJ" >&2; exit 1; }
echo "Dump Finder for Mac $VERSION"

# 1. Self-test (parsers and rules) before packaging anything
"$DOTNET" build "$PROJ" -c Release -nologo -v:quiet -warnaserror
BIN="$MAC/src/DumpFinder.Mac/bin/Release/net10.0/osx-arm64/DumpFinder"
LOG="$(mktemp)"
DOTNET_ROOT="$(dirname "$DOTNET")" "$BIN" --selftest > "$LOG" || { cat "$LOG"; echo "Self-test failed" >&2; exit 1; }
rm -f "$LOG"

# 2. Self-contained publish into the bundle
rm -rf "$OUT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
"$DOTNET" publish "$PROJ" -c Release -r osx-arm64 --self-contained true -nologo -v:quiet \
    -p:UseAppHost=true -p:DebugType=none -p:DebugSymbols=false -o "$APP/Contents/MacOS"
cp "$MAC/branding/DumpFinder.icns" "$APP/Contents/Resources/DumpFinder.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Dump Finder</string>
  <key>CFBundleDisplayName</key><string>Dump Finder</string>
  <key>CFBundleIdentifier</key><string>io.github.lucasredjaimia.dumpfinder</string>
  <key>CFBundleExecutable</key><string>DumpFinder</string>
  <key>CFBundleIconFile</key><string>DumpFinder</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSArchitecturePriority</key><array><string>arm64</string></array>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>MIT License</string>
</dict>
</plist>
PLIST

# 3. Sign (hardened runtime is required for notarization; .NET needs JIT entitlements under it)
ENT="$OUT/entitlements.plist"
cat > "$ENT" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
PLIST

IDENTITY="${DEVELOPER_ID:--}"
TS="--timestamp=none"; [[ "$IDENTITY" != "-" ]] && TS="--timestamp"
# .NET keeps its managed assemblies next to the executable in Contents/MacOS: every file there is signed
# one by one (dylibs, dlls, json), then the executable, then the bundle (codesign --deep is deprecated for signing).
find "$APP/Contents/MacOS" -type f ! -name DumpFinder -print0 |
    xargs -0 -I{} codesign --force $TS --options runtime --entitlements "$ENT" --sign "$IDENTITY" "{}" 2>/dev/null
codesign --force $TS --options runtime --entitlements "$ENT" --sign "$IDENTITY" "$APP/Contents/MacOS/DumpFinder" 2>/dev/null
codesign --force $TS --options runtime --entitlements "$ENT" --sign "$IDENTITY" "$APP" 2>/dev/null
codesign --verify --strict "$APP"
rm -f "$ENT"

# 4. Zip (ditto keeps the bundle's metadata and signature)
ZIP="$OUT/DumpFinder-mac-$VERSION.zip"
ditto -c -k --keepParent "$APP" "$ZIP"

if [[ "$IDENTITY" != "-" && -n "${NOTARY_PROFILE:-}" ]]; then
    xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP"
    rm -f "$ZIP" && ditto -c -k --keepParent "$APP" "$ZIP"
    echo "Notarized."
elif [[ "$IDENTITY" == "-" ]]; then
    echo "Ad-hoc signed (not notarized): a downloaded copy needs right-click › Open the first time."
fi

echo "Built: $APP"
echo "       $ZIP ($(du -h "$ZIP" | cut -f1 | tr -d ' '))"
