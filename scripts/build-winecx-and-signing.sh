#!/bin/bash
# Build Wine, strip, and sign a reusable runtime bundle.

set -e

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WINE_DIR="${WINE_DIR:-$PROJECT_ROOT/wine}"
WINE_BUILDER_DIR="$PROJECT_ROOT/wine-builder"
ENTITLEMENTS_PATH="$PROJECT_ROOT/frontend/build/entitlements.mac.plist"
SIGNED_FLAG="$WINE_DIR/.signed"

if [ -z "${CSC_NAME:-}" ]; then
  echo "❌ CSC_NAME is required to sign Wine."
  exit 1
fi

if [ ! -d "$WINE_BUILDER_DIR" ]; then
  echo "❌ wine-builder not found at $WINE_BUILDER_DIR"
  exit 1
fi

if [ ! -f "$ENTITLEMENTS_PATH" ]; then
  echo "❌ Entitlements not found at $ENTITLEMENTS_PATH"
  exit 1
fi

if [ -f "$SIGNED_FLAG" ]; then
  echo "✅ Wine is already signed ($SIGNED_FLAG exists)."
  echo "   Remove $SIGNED_FLAG to force rebuild/sign."
  exit 0
fi

echo "🍷 Building Wine..."
cd "$WINE_BUILDER_DIR"
./build.sh
cd "$PROJECT_ROOT"

if [ ! -d "$WINE_DIR" ]; then
  echo "❌ Wine build output not found at $WINE_DIR"
  exit 1
fi

echo "✂️  Stripping Wine binaries..."
find "$WINE_DIR" -type f -name "*.dylib" -exec strip -x {} \; || true
find "$WINE_DIR" -type f -name "*.so" -exec strip -x {} \; || true

echo "🔐 Signing Wine binaries..."
CPU_COUNT=$(sysctl -n hw.ncpu 2>/dev/null || echo 4)
find "$WINE_DIR" -type f \( -name "*.dylib" -o -name "*.so" -o -perm -111 \) -print0 \
  | xargs -0 -P "$CPU_COUNT" -n 1 /bin/sh -c 'echo "[Wine Sign] $0"; codesign --force --timestamp=none --options runtime --entitlements "'"$ENTITLEMENTS_PATH"'" --sign "'"$CSC_NAME"'" "$0"'

touch "$SIGNED_FLAG"
echo "✅ Wine build + signing complete."
