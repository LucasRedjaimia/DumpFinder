#!/usr/bin/env bash
# Publishes a new Dump Finder for Mac version on GitHub: bumps the Mac version, builds and signs the .app,
# commits, tags mac-v<version>, pushes and creates the GitHub release with DumpFinder-mac-<version>.zip.
#
# Usage:  mac/release.sh 0.2.0 [--draft]
#   Release notes are read from mac/release-notes/v<version>.md (Markdown). If the file does not exist yet,
#   a template is created and the script stops so you can fill it in.
#
# Versioning: the Mac app has its own version line and its own tags (mac-v0.2.0), separate from the
# Windows exe (v6.2.0, released by release.ps1). Mac releases are never marked "Latest" on GitHub:
# installed Windows apps check /releases/latest, which must stay the newest Windows release.
set -euo pipefail

VERSION="${1:-}"
DRAFT=""
[[ "${2:-}" == "--draft" ]] && DRAFT="--draft"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Usage: mac/release.sh <major.minor.patch> [--draft]" >&2; exit 1; }

MAC="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$MAC")"
cd "$ROOT"
TAG="mac-v$VERSION"

command -v gh >/dev/null || { echo "GitHub CLI not found. Install it: brew install gh" >&2; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "GitHub CLI is not signed in. Run: gh auth login" >&2; exit 1; }
BRANCH="$(git branch --show-current)"
[[ "$BRANCH" == "main" ]] || { echo "Release from main (current branch: $BRANCH)." >&2; exit 1; }

# 1. Release notes
NOTES="$MAC/release-notes/v$VERSION.md"
if [[ ! -f "$NOTES" ]]; then
    mkdir -p "$MAC/release-notes"
    printf "## What's new\n- \n\n## Fixes\n- \n" > "$NOTES"
    echo "Release notes template created: $NOTES"
    echo "Fill it in (what is new / what is fixed), then run this script again."
    exit 1
fi

# 2. The version must be newer than every existing Mac tag (mac-v*; Windows tags are v*)
LATEST="$(git tag --list 'mac-v*' | sed 's/^mac-v//' | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)"
if [[ -n "$LATEST" ]]; then
    NEWEST="$(printf '%s\n%s\n' "$LATEST" "$VERSION" | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)"
    if [[ "$NEWEST" != "$VERSION" || "$LATEST" == "$VERSION" ]]; then
        echo "Version $VERSION is not newer than the latest Mac tag mac-v$LATEST" >&2; exit 1
    fi
fi

# 3. Bump the version (the Info.plist is generated from it by build.sh)
PROJ="$MAC/src/DumpFinder.Mac/DumpFinder.Mac.csproj"
sed -i '' -E "s|<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>|<Version>$VERSION</Version>|" "$PROJ"
grep -q "<Version>$VERSION</Version>" "$PROJ" || { echo "Version pattern not found in $PROJ" >&2; exit 1; }

# 4. Build, self-test, sign (and notarize when DEVELOPER_ID + NOTARY_PROFILE are set)
"$MAC/build.sh"
ZIP="$MAC/out/DumpFinder-mac-$VERSION.zip"
BUILT="$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$MAC/out/Dump Finder.app/Contents/Info.plist")"
[[ "$BUILT" == "$VERSION" ]] || { echo "Built app reports $BUILT, expected $VERSION" >&2; exit 1; }

# 5. Commit, tag, push (only the Mac folder: Windows files are released by release.ps1)
git add -A mac
git commit -m "Dump Finder for Mac $VERSION"
git tag -a "$TAG" -m "Dump Finder for Mac $VERSION"
git push origin HEAD
git push origin "$TAG"

# 6. GitHub release with the zipped .app, never marked Latest
gh release create "$TAG" "$ZIP" --title "Dump Finder for Mac $VERSION" --notes-file "$NOTES" \
    --verify-tag --latest=false $DRAFT

echo
echo "Released Dump Finder for Mac $VERSION ($TAG)"
echo "SHA-256 of $(basename "$ZIP"): $(shasum -a 256 "$ZIP" | cut -d' ' -f1)"
