#!/bin/bash
# XIV The Calamity - Build and Test Script (Photino Version)
# For development and testing (no code signing)

set -e

echo "======================================"
echo "XIV The Calamity - Photino Build & Test"
echo "======================================"

# Change to project root directory
cd "$(dirname "$0")/.."
PROJECT_ROOT=$(pwd)

# Color definitions
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo ""
echo "🧹 Cleaning environment..."
echo "   Note: Close XIVTheCalamity before building to avoid locked file errors"

# Clean old build results
echo "   Cleaning old build results..."

if [ -d "$PROJECT_ROOT/Release/mac-arm64" ]; then
    chmod -R +w "$PROJECT_ROOT/Release/mac-arm64" 2>/dev/null
    rm -rf "$PROJECT_ROOT/Release/mac-arm64" 2>/dev/null
fi

if [ -d "$PROJECT_ROOT/backend/src/XIVTheCalamity/wwwroot" ]; then
    rm -rf "$PROJECT_ROOT/backend/src/XIVTheCalamity/wwwroot" 2>/dev/null
fi

# Ensure directories exist
mkdir -p "$PROJECT_ROOT/backend/src/XIVTheCalamity/wwwroot"
mkdir -p "$PROJECT_ROOT/shared/resources/bin"

echo "   ✅ Cleanup complete"

# Build Frontend
echo ""
echo "📦 Building Frontend (Vite)..."
cd "$PROJECT_ROOT/frontend"
npm run build:renderer

# Read version from XIVTheCalamity.csproj
VERSION=$(grep -oE '<Version>[^<]+</Version>' "$PROJECT_ROOT/backend/src/XIVTheCalamity/XIVTheCalamity.csproj" | sed -E 's/<\/?Version>//g' | tr -d '[:space:]')
echo "   📦 Current version from XIVTheCalamity.csproj: $VERSION"

# Copy static assets to C# project
echo "   Copying frontend assets to C# wwwroot..."
cp -R dist/* "$PROJECT_ROOT/backend/src/XIVTheCalamity/wwwroot/"

# Build Audio Router CLI
echo ""
echo "📦 Building Swift Audio Router CLI..."
swiftc "$PROJECT_ROOT/XTCAudioRouter/AudioRouter.swift" \
  -framework CoreAudio \
  -framework AudioToolbox \
  -o "$PROJECT_ROOT/shared/resources/bin/XTCAudioRouter"

# Build C# Entrypoint (NativeAOT)
echo ""
echo "📦 Building C# Photino Application (NativeAOT)..."
cd "$PROJECT_ROOT/backend"
dotnet publish src/XIVTheCalamity/XIVTheCalamity.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  /p:PublishAot=true \
  -o "$PROJECT_ROOT/backend/src/XIVTheCalamity/bin/Release/publish"

# Structure macOS App Bundle
echo ""
echo "📦 Structuring macOS App Bundle..."
APP_DIR="$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources/resources"

# Copy C# binary and native dynamic libraries
cp "$PROJECT_ROOT/backend/src/XIVTheCalamity/bin/Release/publish/XIVTheCalamity" "$APP_DIR/Contents/MacOS/XIVTheCalamity"
chmod +x "$APP_DIR/Contents/MacOS/XIVTheCalamity"
cp "$PROJECT_ROOT/backend/src/XIVTheCalamity/bin/Release/publish/"*.dylib "$APP_DIR/Contents/MacOS/" 2>/dev/null || true

# Copy wwwroot web assets
cp -R "$PROJECT_ROOT/backend/src/XIVTheCalamity/bin/Release/publish/wwwroot" "$APP_DIR/Contents/Resources/"

# Copy Info.plist and Icon
cp "$PROJECT_ROOT/backend/src/XIVTheCalamity/Info.plist" "$APP_DIR/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP_DIR/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$VERSION" "$APP_DIR/Contents/Info.plist"
cp "$PROJECT_ROOT/frontend/build/XIVTC.icns" "$APP_DIR/Contents/Resources/"

# Copy shared resources (including XTCAudioRouter in bin/)
cp -R "$PROJECT_ROOT/shared/resources/"* "$APP_DIR/Contents/Resources/resources/"

# Codesign app bundle — 優先使用本機開發憑證，若無則回退至 Ad-hoc 簽名
# 這可以讓 macOS TCC 以穩定的認證主體識別 App，避免每次重新編譯後重新要求 Documents 等目錄授權
echo ""
echo "🔏 Code signing app bundle..."

SIGNING_IDENTITY="-"
if command -v security &> /dev/null; then
    # 自動尋找金鑰圈中有效的本機開發憑證（如 Apple Development 或 Mac Developer）
    LOCAL_ID=$(security find-identity -v -p codesigning | grep -E "Apple Development|Mac Developer" | head -n 1 | grep -oE '"[^"]+"' | tr -d '"')
    if [ ! -z "$LOCAL_ID" ]; then
        SIGNING_IDENTITY="$LOCAL_ID"
        echo "   🔍 Detected local developer certificate: $SIGNING_IDENTITY"
    else
        echo "   ℹ️ No Apple Development certificate found in Keychain, using Ad-hoc (-)"
    fi
fi

# 1. 先強制簽名內嵌的 dynamic libraries 與執行檔
find "$APP_DIR/Contents/MacOS" -name "*.dylib" -exec codesign --force --sign "$SIGNING_IDENTITY" {} \; 2>/dev/null || true
# 2. 對整個 .app Bundle 進行深層遞迴簽名
codesign --force --deep --sign "$SIGNING_IDENTITY" "$APP_DIR"
echo "   ✅ Code sign complete"

# Check results
if [ -d "$APP_DIR" ]; then
  echo ""
  echo -e "${GREEN}✅ Photino Build successful!${NC}"
  
  # Display bundle info
  echo ""
  echo "📊 Bundle Information:"
  echo "  Path: $APP_DIR"
  echo "  Size: $(du -sh "$APP_DIR" | cut -f1)"
  echo ""
  
  # Check backend executable
  if [ -f "$APP_DIR/Contents/MacOS/XIVTheCalamity" ]; then
    BINARY_SIZE=$(ls -lh "$APP_DIR/Contents/MacOS/XIVTheCalamity" | awk '{print $5}')
    echo -e "  ${GREEN}✅${NC} NativeAOT Binary: $BINARY_SIZE"
  else
    echo -e "  ${RED}❌${NC} NativeAOT Binary: Not found"
  fi
  
  # Check resources directory
  if [ -d "$APP_DIR/Contents/Resources/resources" ]; then
    RESOURCES_SIZE=$(du -sh "$APP_DIR/Contents/Resources/resources" | cut -f1)
    echo -e "  ${GREEN}✅${NC} Resources: $RESOURCES_SIZE (XTCAudioRouter, d3dcompiler, dxmt, dxvk, fonts)"
  else
    echo -e "  ${RED}❌${NC} Resources: Not found"
  fi
  
  echo ""
  
  # Ask to launch
  read -p "🚀 Launch for testing? (y/n) " -n 1 -r
  echo
  if [[ $REPLY =~ ^[Yy]$ ]]; then
    # Enable development mode in config
    CONFIG_FILE="$HOME/Library/Application Support/XIVTheCalamity/config.json"
    
    if [ -f "$CONFIG_FILE" ]; then
      echo "🔧 Enabling development mode in config..."
      if command -v jq &> /dev/null; then
        TMP_FILE=$(mktemp)
        jq '.launcher.developmentMode = true' "$CONFIG_FILE" > "$TMP_FILE" && mv "$TMP_FILE" "$CONFIG_FILE"
      else
        sed -i '' 's/"developmentMode": false/"developmentMode": true/g' "$CONFIG_FILE"
      fi
      echo "✅ Development mode enabled"
    fi
    
    echo ""
    echo "🚀 Launching application..."
    open "$APP_DIR"
    
    echo ""
    echo "📝 View logs:"
    echo "   tail -f ~/Library/Application\ Support/XIVTheCalamity/logs/backend-*.log"
    echo ""
  fi
else
  echo ""
  echo -e "${RED}❌ Build failed!${NC}"
  exit 1
fi
