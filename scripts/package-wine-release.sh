#!/bin/bash
# Package Wine runtime for GitHub Release distribution.
# Copies wine/ to a temp directory, removes signatures, strips binaries, and creates a tar.xz archive.
#
# Usage:
#   ./scripts/package-wine-release.sh [version]
#
# Example:
#   ./scripts/package-wine-release.sh v2026.01.24

set -e

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WINE_DIR="$PROJECT_ROOT/wine"
OUTPUT_DIR="$PROJECT_ROOT/Release"

if [ ! -d "$WINE_DIR/bin" ] || [ ! -f "$WINE_DIR/bin/wine64" ]; then
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

# Detect architecture from wine64 binary
ARCH=$(file "$WINE_DIR/bin/wine64" | grep -o 'x86_64\|arm64')
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

# Step 2: Remove code signatures
echo "🔓 Removing code signatures..."
SIGNED_COUNT=0
while IFS= read -r -d '' file; do
  if codesign -dv "$file" 2>/dev/null; then
    codesign --remove-signature "$file" 2>/dev/null && SIGNED_COUNT=$((SIGNED_COUNT + 1))
  fi
done < <(find "$TEMP_WINE" -type f \( -name "*.dylib" -o -name "*.so" -o -perm -111 \) -print0)
echo "   Removed signatures from $SIGNED_COUNT files"

# Step 3: Strip debug symbols
echo "✂️  Stripping debug symbols..."
find "$TEMP_WINE" -type f -name "*.dylib" -exec strip -x {} \; 2>/dev/null || true
find "$TEMP_WINE" -type f -name "*.so" -exec strip -x {} \; 2>/dev/null || true
find "$TEMP_WINE/bin" -type f -perm -111 -exec strip -x {} \; 2>/dev/null || true

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
