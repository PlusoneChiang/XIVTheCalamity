#!/bin/bash
# Quick packaging script for production release
# Includes backend build and code signing

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$PROJECT_ROOT/frontend"

NOTARIZE="${NOTARIZE:-1}"
SIGNING_DEBUG="${SIGNING_DEBUG:-1}"
APPLE_API_KEY_PATH="${APPLE_API_KEY_PATH:-}"
APPLE_API_KEY_ID="${APPLE_API_KEY_ID:-}"
APPLE_API_ISSUER="${APPLE_API_ISSUER:-}"

echo "📦 Production Packaging..."
echo "   1. Build backend (Release)"
echo "   2. Package Electron app"
echo "   3. Code signing enabled (CSC_NAME required)"
if [ "$NOTARIZE" = "1" ]; then
  echo "   4. Notarization enabled"
else
  echo "   4. Notarization skipped (NOTARIZE=0)"
fi
echo "   5. Signing debug: ${SIGNING_DEBUG}"
echo ""

if [ -z "${CSC_NAME:-}" ]; then
  echo "❌ CSC_NAME is required for code signing (Developer ID Application: ...)"
  exit 1
fi

if [ "$SIGNING_DEBUG" = "1" ]; then
  export DEBUG="electron-builder,app-builder,app-builder-lib"
  export CSC_DEBUG=1
fi

if [ -d "$PROJECT_ROOT/Release/mac-arm64" ]; then
  chmod -R +w "$PROJECT_ROOT/Release/mac-arm64" 2>/dev/null || true
  rm -rf "$PROJECT_ROOT/Release/mac-arm64" 2>/dev/null || true
fi

npm run dist

PROJECT_ROOT="$(cd .. && pwd)"
APP_PATH="$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
ZIP_PATH="$PROJECT_ROOT/Release/XIVTheCalamity-mac-arm64.zip"
DMG_PATH="$(ls -1 "$PROJECT_ROOT"/Release/XIVTheCalamity-*-darwin-arm64.dmg 2>/dev/null | tail -n 1)"

# Display results
if [ -d "$APP_PATH" ]; then
  echo ""
  echo "✅ Build completed!"
  echo "  Path: $APP_PATH"
  echo "  Size: $(du -sh "$APP_PATH" | cut -f1)"
  
  # Check code signing status
  echo ""
  echo "🔐 Code Signing Status:"
  if codesign -dv "$APP_PATH" 2>&1 | grep -q "Signature"; then
    echo "  ✅ App is signed"
    codesign -dv "$APP_PATH" 2>&1 | grep "Authority" | head -1 | sed 's/^/  /'
    # Use spctl for Gatekeeper assessment (more accurate for notarization)
    if spctl --assess --verbose=2 --type execute "$APP_PATH" 2>&1; then
      echo "  ✅ Gatekeeper assessment passed"
    else
      echo "  ⚠️  Gatekeeper assessment pending (will pass after notarization)"
    fi
  else
    echo "  ❌ App is NOT signed (check your Developer ID certificate)"
  fi
  echo ""

  if [ "$NOTARIZE" = "1" ]; then
    if [ -z "$APPLE_API_KEY_PATH" ] || [ -z "$APPLE_API_KEY_ID" ] || [ -z "$APPLE_API_ISSUER" ]; then
      echo "❌ Missing notarization credentials."
      echo "   Required: APPLE_API_KEY_PATH, APPLE_API_KEY_ID, APPLE_API_ISSUER"
      exit 1
    fi

    echo "🧾 Creating notarization zip..."
    rm -f "$ZIP_PATH"
    # Remove extended attributes to prevent AppleDouble (._*) files from breaking signatures
    # When zip is extracted, these would create extra files not in the original sealed resources
    echo "   Cleaning extended attributes..."
    xattr -cr "$APP_PATH"
    COPYFILE_DISABLE=1 ditto -c -k --keepParent "$APP_PATH" "$ZIP_PATH"

    echo "☁️  Submitting to Apple notarization..."
    xcrun notarytool submit "$ZIP_PATH" \
      --key "$APPLE_API_KEY_PATH" \
      --key-id "$APPLE_API_KEY_ID" \
      --issuer "$APPLE_API_ISSUER" \
      --wait

    echo "📌 Stapling notarization ticket..."
    xcrun stapler staple "$APP_PATH"
    xcrun stapler validate "$APP_PATH" >/dev/null
    if [ -n "$DMG_PATH" ] && [ -f "$DMG_PATH" ]; then
      xcrun stapler staple "$DMG_PATH"
      xcrun stapler validate "$DMG_PATH" >/dev/null
    fi
    echo "✅ Notarization complete!"
    echo ""
  fi
  
  # Ask to launch
  read -p "🚀 Launch for testing? (y/n) " -n 1 -r
  echo
  if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "🚀 Launching application..."
    open "$APP_PATH"
  fi
else
  echo "❌ Build failed!"
  exit 1
fi
