#!/bin/bash
# Quick packaging script for production release
# Includes backend build and code signing

set -e

cd "$(dirname "$0")/../frontend"

echo "📦 Production Packaging..."
echo "   1. Build backend (Release)"
echo "   2. Package Electron app"
echo "   3. Code signing enabled"
echo ""

npm run build:prod

PROJECT_ROOT="$(cd .. && pwd)"

# Display results
if [ -d "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app" ]; then
  echo ""
  echo "✅ Build completed!"
  echo "  Path: $PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
  echo "  Size: $(du -sh "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app" | cut -f1)"
  
  # Check code signing status
  echo ""
  echo "🔐 Code Signing Status:"
  if codesign -dv "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app" 2>&1 | grep -q "Signature"; then
    echo "  ✅ App is signed"
    codesign -dv "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app" 2>&1 | grep "Authority" | head -1 | sed 's/^/  /'
  else
    echo "  ❌ App is NOT signed (check your Developer ID certificate)"
  fi
  echo ""
  
  # Ask to launch
  read -p "🚀 Launch for testing? (y/n) " -n 1 -r
  echo
  if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "🚀 Launching application..."
    open "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
  fi
else
  echo "❌ Build failed!"
  exit 1
fi
