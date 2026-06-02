#!/bin/bash
# Package Wine runtime for GitHub Release distribution.
# Copies wine/ to a temp directory and creates a tar.xz archive.
# Code signatures from build-winecx-and-signing.sh are PRESERVED intentionally:
#   - Developer ID signatures allow loading on any macOS (including Apple Silicon via Rosetta 2)
#   - Hardened Runtime + entitlements (allow-jit, disable-library-validation) travel with the signature
#   - strip is NOT repeated here; it was already done in build-winecx-and-signing.sh before signing
#
# Usage:
#   ./scripts/package-wine-release.sh [version]
#
# Example:
#   ./scripts/package-wine-release.sh v2026.01.24
#
# Prerequisites:
#   Run scripts/build-winecx-and-signing.sh first to build and sign Wine.

set -e

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WINE_DIR="$PROJECT_ROOT/wine"
OUTPUT_DIR="$PROJECT_ROOT/Release"

if [ ! -d "$WINE_DIR/bin" ] || [ ! -f "$WINE_DIR/bin/wine" ]; then
  echo "❌ Wine not found at $WINE_DIR"
  echo "   Please build Wine first with wine-builder/build.sh"
  exit 1
fi

# Determine version from argument or winecx submodule tag
if [ -n "$1" ]; then
  VERSION="$1"
else
  VERSION=$(cd "$PROJECT_ROOT/wine-builder/winecx" 2>/dev/null && git describe --tags --exact-match 2>/dev/null || echo "")
  if [ -z "$VERSION" ]; then
    echo "❌ No version specified and could not detect winecx tag."
    echo "   Usage: $0 <version>  (e.g. v2026.01.24)"
    exit 1
  fi
fi

# Detect architecture from wine binary
ARCH=$(file "$WINE_DIR/bin/wine" | grep -o 'x86_64\|arm64')
if [ -z "$ARCH" ]; then
  echo "❌ Could not detect Wine architecture"
  exit 1
fi

ARCHIVE_NAME="wine-macos-${ARCH}-${VERSION}.tar.xz"
TEMP_DIR=$(mktemp -d)
TEMP_WINE="$TEMP_DIR/wine"

echo "📦 Packaging Wine for GitHub Release"
echo "   Version:  $VERSION"
echo "   Arch:     $ARCH"
echo "   Source:   $WINE_DIR"
echo "   Output:   $OUTPUT_DIR/$ARCHIVE_NAME"
echo ""

# Step 1: Copy to temp directory
echo "📋 Copying Wine to temp directory..."
cp -R "$WINE_DIR" "$TEMP_WINE"
chmod -R u+w "$TEMP_WINE"
# Remove macOS metadata
find "$TEMP_WINE" -name ".DS_Store" -delete 2>/dev/null || true
find "$TEMP_WINE" -name "._*" -delete 2>/dev/null || true

# Step 1.5: Verify Wine binaries are signed before packaging
echo "🔏 Verifying Wine code signatures..."
VERIFY_FAILED=0
for CHECK_FILE in "$TEMP_WINE/bin/wine" "$TEMP_WINE/bin/wineserver"; do
  if [ -f "$CHECK_FILE" ]; then
    CODESIGN_OUT=$(codesign -dv "$CHECK_FILE" 2>&1)
    # Accept any real certificate signature (TeamIdentifier present and not "not set")
    # Ad-hoc signatures show "TeamIdentifier=not set"; unsigned show "code object is not signed"
    if echo "$CODESIGN_OUT" | grep -q "TeamIdentifier=" && \
       ! echo "$CODESIGN_OUT" | grep -q "TeamIdentifier=not set"; then
      TEAM=$(echo "$CODESIGN_OUT" | grep "TeamIdentifier=" | head -1)
      echo "   ✅ Signed: $(basename "$CHECK_FILE") ($TEAM)"
    else
      echo "❌ Not signed with a Developer ID certificate: $CHECK_FILE"
      VERIFY_FAILED=1
    fi
  fi
done
if [ "$VERIFY_FAILED" -eq 1 ]; then
  echo ""
  echo "❌ One or more Wine binaries are not signed with a Developer ID."
  echo "   Please run scripts/build-winecx-and-signing.sh (with CSC_NAME set) first."
  rm -rf "$TEMP_DIR"
  exit 1
fi

# Report size after processing
PROCESSED_SIZE=$(du -sh "$TEMP_WINE" | cut -f1)
echo "   Processed size: $PROCESSED_SIZE"

# Step 4: Create tar.xz archive
echo "📦 Creating archive..."
mkdir -p "$OUTPUT_DIR"
tar -C "$TEMP_DIR" -cJf "$OUTPUT_DIR/$ARCHIVE_NAME" wine

ARCHIVE_SIZE=$(du -sh "$OUTPUT_DIR/$ARCHIVE_NAME" | cut -f1)
echo "   Archive size: $ARCHIVE_SIZE"

# Step 5: Generate SHA256 checksum
CHECKSUM=$(shasum -a 256 "$OUTPUT_DIR/$ARCHIVE_NAME" | awk '{print $1}')
echo "$CHECKSUM  $ARCHIVE_NAME" > "$OUTPUT_DIR/$ARCHIVE_NAME.sha256"
echo "   SHA256: $CHECKSUM"

# Cleanup
rm -rf "$TEMP_DIR"

echo ""
echo "✅ Package complete: $OUTPUT_DIR/$ARCHIVE_NAME"
echo "   Checksum file:   $OUTPUT_DIR/$ARCHIVE_NAME.sha256"
echo ""
echo "📤 To upload to GitHub Release:"
echo "   gh release create wine-$VERSION $OUTPUT_DIR/$ARCHIVE_NAME $OUTPUT_DIR/$ARCHIVE_NAME.sha256 \\"
echo "     --repo PlusoneChiang/XIVTheCalamity \\"
echo "     --title \"Wine $VERSION\" \\"
echo "     --notes \"Wine runtime for macOS ($ARCH)\""
