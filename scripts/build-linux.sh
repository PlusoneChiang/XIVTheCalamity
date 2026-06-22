#!/bin/bash

# XIVTheCalamity Linux Build Script
# This script will:
# 1. Clean Release directory
# 2. Compile backend (.NET)
# 3. Package frontend (Electron)
# 4. Create AppImage

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BACKEND_DIR="$PROJECT_ROOT/backend"
FRONTEND_DIR="$PROJECT_ROOT/frontend"
RELEASE_DIR="$PROJECT_ROOT/Release"

echo "🚀 XIVTheCalamity Linux Build Script"
echo ""

# ================== Check Dependencies ==================
echo "🔍 Checking dependencies..."

check_command() {
  if command -v $1 &> /dev/null; then
    echo "   ✅ $1 $($1 --version 2>&1 | head -n1)"
  else
    echo "   ❌ $1 not installed"
    echo ""
    echo "Please install $1:"
    if [ "$1" == "node" ] || [ "$1" == "npm" ]; then
      echo "  curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -"
      echo "  sudo apt-get install -y nodejs"
    elif [ "$1" == "dotnet" ]; then
      echo "  https://dotnet.microsoft.com/download"
    fi
    exit 1
  fi
}

check_command node
check_command npm
check_command dotnet

# ================== Clean Release Directory ==================
echo ""
echo "🧹 Cleaning Release directory..."

if [ -d "$RELEASE_DIR" ]; then
  # Remove old AppImage files
  rm -f "$RELEASE_DIR"/*.AppImage
  # Remove old unpacked directory
  rm -rf "$RELEASE_DIR/linux-unpacked"
  # Remove old temp backend
  rm -rf "$RELEASE_DIR/temp-backend-linux"
  echo "   ✅ Cleaned release directory"
else
  mkdir -p "$RELEASE_DIR"
  echo "   ✅ Created Release directory"
fi

# ================== Compile Backend ==================
echo ""
echo "🔨 Compiling backend (NativeAOT linux-x64)..."

cd "$BACKEND_DIR"

dotnet publish src/XIVTheCalamity.Api.NativeAOT/XIVTheCalamity.Api.NativeAOT.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o "$RELEASE_DIR/temp-backend-linux"

if [ $? -eq 0 ]; then
  echo "   ✅ Backend compiled successfully"
else
  echo "   ❌ Backend compilation failed"
  exit 1
fi

# ================== Read Version ==================
echo ""

cd "$FRONTEND_DIR"

# Read version from package.json
VERSION=$(node -e "console.log(require('./package.json').version)")
echo "   📦 Current version: $VERSION"

# ================== Install Frontend Dependencies ==================
echo ""
echo "📦 Installing frontend dependencies..."

if [ -d "node_modules" ]; then
  echo "   ✅ Dependencies already installed"
else
  npm install > /dev/null 2>&1
  echo "   ✅ Dependencies installed"
fi

# ================== Build AppImage ==================
echo ""
echo "📦 Building AppImage (version $VERSION)..."
echo "   (This may take 5-10 minutes...)"
echo ""

# Build renderer (Vite)
echo "   Build renderer..."
npm run build:renderer

# Use npx electron-builder (linux extraResources is already configured in package.json)
npx electron-builder --linux --x64

# Expected filename pattern
EXPECTED_APPIMAGE="XIVTheCalamity-${VERSION}-linux-x86_64.AppImage"
APPIMAGE="$RELEASE_DIR/$EXPECTED_APPIMAGE"

# Fallback: find any AppImage with version
if [ ! -f "$APPIMAGE" ]; then
  APPIMAGE=$(ls -t "$RELEASE_DIR"/XIVTheCalamity-*-linux-*.AppImage 2>/dev/null | head -1)
fi

# Last resort: find any AppImage
if [ ! -f "$APPIMAGE" ]; then
  APPIMAGE=$(ls -t "$RELEASE_DIR"/*.AppImage 2>/dev/null | head -1)
fi

if [ -f "$APPIMAGE" ]; then
  chmod +x "$APPIMAGE"
  echo ""
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "✅ Build completed successfully!"
  echo ""
  echo "📦 AppImage: $(basename "$APPIMAGE")"
  echo "📏 Size: $(du -h "$APPIMAGE" | cut -f1)"
  echo "📂 Location: $APPIMAGE"
  echo ""
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo ""
  read -p "🧪 Do you want to run the AppImage now? [y/N]: " run_choice
  
  if [[ "$run_choice" =~ ^[Yy]$ ]]; then
    echo ""
    echo "🚀 Starting AppImage..."
    "$APPIMAGE"
  else
    echo ""
    echo "💡 You can run it manually with:"
    echo "   $APPIMAGE"
  fi
else
  echo ""
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "❌ Build failed - AppImage not found"
  echo ""
  echo "Please check the error messages above."
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  exit 1
fi

echo ""
