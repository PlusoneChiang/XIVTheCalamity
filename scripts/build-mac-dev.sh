#!/bin/bash
# XIV The Calamity - Build and Test Script
# For development and testing (no code signing)

set -e

echo "======================================"
echo "XIV The Calamity - Build & Test"
echo "======================================"

# Change to project root directory
cd "$(dirname "$0")/.."
PROJECT_ROOT=$(pwd)

# Color definitions
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Check Wine
echo ""
echo "🍷 Wine status: Downloaded at runtime (not bundled)"
echo -e "   ${GREEN}ℹ️${NC}  Wine will be downloaded from GitHub Release on first launch"
echo ""
echo "🧹 Cleaning environment..."
echo "   Note: Close XIVTheCalamity.app before building to avoid errors"

# Clean old build results
echo "   Cleaning old build results..."

# Simple and aggressive cleanup
if [ -d "$PROJECT_ROOT/Release/mac-arm64" ]; then
    chmod -R +w "$PROJECT_ROOT/Release/mac-arm64" 2>/dev/null
    rm -rf "$PROJECT_ROOT/Release/mac-arm64" 2>/dev/null
    
    # If directory still exists, move it out of the way
    if [ -d "$PROJECT_ROOT/Release/mac-arm64" ]; then
        echo "   ⚠️  Moving locked build to mac-arm64.old (will be overwritten)"
        mv "$PROJECT_ROOT/Release/mac-arm64" "$PROJECT_ROOT/Release/mac-arm64.old" 2>/dev/null || true
    fi
fi

if [ -d "$PROJECT_ROOT/Release/temp-backend" ]; then
    rm -rf "$PROJECT_ROOT/Release/temp-backend" 2>/dev/null || true
fi

echo "   ✅ Cleanup complete"

# Change to frontend directory
cd "$PROJECT_ROOT/frontend"

# Build
echo ""
echo "📦 Starting build..."
echo "   1. Build backend (Release)"
echo "   2. Package frontend (no signing)"
echo "   3. Copy resources"
echo ""

# Disable code signing for development builds
CSC_IDENTITY_AUTO_DISCOVERY=false SIGN_WINE=0 npm run pack

# Check results
if [ -d "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app" ]; then
  echo ""
  echo -e "${GREEN}✅ Build successful!${NC}"
  
  # Display bundle info
  echo ""
  echo "📊 Bundle Information:"
  echo "  Path: $PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
  echo "  Size: $(du -sh "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app" | cut -f1)"
  echo ""
  
  # Check backend (NativeAOT)
  if [ -f "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app/Contents/Resources/backend/XIVTheCalamity.Api.NativeAOT" ]; then
    BACKEND_SIZE=$(ls -lh "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app/Contents/Resources/backend/XIVTheCalamity.Api.NativeAOT" | awk '{print $5}')
    echo -e "  ${GREEN}✅${NC} Backend (NativeAOT): $BACKEND_SIZE"
  else
    echo -e "  ${RED}❌${NC} Backend: Not found"
  fi
  
  # Check resources directory
  if [ -d "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app/Contents/Resources/resources" ]; then
    RESOURCES_SIZE=$(du -sh "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app/Contents/Resources/resources" | cut -f1)
    echo -e "  ${GREEN}✅${NC} Resources: $RESOURCES_SIZE (d3dcompiler, dxmt, dxvk, fonts)"
  else
    echo -e "  ${RED}❌${NC} Resources: Not found"
  fi
  
  # Check Wine (now downloaded at runtime, not bundled)
  echo -e "  ${GREEN}ℹ️${NC}  Wine: Downloaded at runtime (not bundled)"
  
  echo ""
  
  # Clean temporary files
  echo "🧹 Cleaning temporary files..."
  rm -rf "$PROJECT_ROOT/Release/temp-backend"
  
  # Ask to launch
  read -p "🚀 Launch for testing? (y/n) " -n 1 -r
  echo
  if [[ $REPLY =~ ^[Yy]$ ]]; then
    # Enable development mode in config
    CONFIG_FILE="$HOME/Library/Application Support/XIVTheCalamity/config.json"
    
    if [ -f "$CONFIG_FILE" ]; then
      echo "🔧 Enabling development mode in config..."
      # Use jq if available, otherwise use simple sed
      if command -v jq &> /dev/null; then
        TMP_FILE=$(mktemp)
        jq '.launcher.developmentMode = true' "$CONFIG_FILE" > "$TMP_FILE" && mv "$TMP_FILE" "$CONFIG_FILE"
      else
        # Simple replacement for basic config structure
        sed -i '' 's/"developmentMode": false/"developmentMode": true/g' "$CONFIG_FILE"
      fi
      echo "✅ Development mode enabled"
    else
      echo "⚠️  Config file not found, will be created with default settings"
      echo "   You can manually enable development mode in settings later"
    fi
    
    echo ""
    echo "🚀 Launching application..."
    open "$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
    
    echo ""
    echo "📝 View logs:"
    echo "   Backend: tail -f ~/Library/Application\ Support/XIVTheCalamity/logs/backend-*.log"
    echo "   Frontend: tail -f ~/Library/Application\ Support/XIVTheCalamity/logs/app-*.log"
    echo ""
    echo "💡 Development mode is enabled - backend will show Debug level logs"
    echo "   To disable: Set launcher.developmentMode = false in config.json"
  fi
else
  echo ""
  echo -e "${RED}❌ Build failed!${NC}"
  exit 1
fi
