#!/bin/bash

# XIVTheCalamity Linux Build Script (Photino Version)
# This script will:
# 1. Clean Release directory
# 2. Compile frontend (Vite)
# 3. Copy frontend assets to backend wwwroot
# 4. Compile backend (.NET NativeAOT) to Release/linux-unpacked
# 5. Copy shared resources to Release/linux-unpacked

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BACKEND_DIR="$PROJECT_ROOT/backend"
FRONTEND_DIR="$PROJECT_ROOT/frontend"
RELEASE_DIR="$PROJECT_ROOT/Release"
OUTPUT_DIR="$RELEASE_DIR/linux-unpacked"

echo "🚀 XIVTheCalamity Linux Build Script (Photino Version)"
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

if [ -d "$OUTPUT_DIR" ]; then
  rm -rf "$OUTPUT_DIR"
  echo "   ✅ Cleaned release directory"
else
  mkdir -p "$RELEASE_DIR"
fi

# ================== Install Frontend Dependencies ==================
echo ""
echo "📦 Checking frontend dependencies..."
cd "$FRONTEND_DIR"

if [ -d "node_modules" ]; then
  echo "   ✅ Dependencies already installed"
else
  npm install
  echo "   ✅ Dependencies installed"
fi

# ================== Build Frontend ==================
echo ""
echo "📦 Building Frontend (Vite)..."
npm run build:renderer

# ================== Copy Frontend to Backend ==================
echo ""
echo "   Copying frontend assets to C# wwwroot..."
mkdir -p "$BACKEND_DIR/src/XIVTheCalamity/wwwroot"
cp -R dist/* "$BACKEND_DIR/src/XIVTheCalamity/wwwroot/"

# ================== Compile Backend ==================
echo ""
echo "🔨 Compiling C# Photino Application (NativeAOT linux-x64)..."
cd "$BACKEND_DIR"

dotnet publish src/XIVTheCalamity/XIVTheCalamity.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  /p:PublishAot=true \
  -o "$OUTPUT_DIR"

if [ $? -eq 0 ]; then
  echo "   ✅ Backend compiled successfully"
else
  echo "   ❌ Backend compilation failed"
  exit 1
fi

# ================== Copy Shared Resources ==================
echo ""
echo "📦 Copying shared resources..."
cp -R "$PROJECT_ROOT/shared/resources" "$OUTPUT_DIR/"
echo "   ✅ Resources copied"

# ================== Run Test ==================
APP_BIN="$OUTPUT_DIR/XIVTheCalamity"
if [ -f "$APP_BIN" ]; then
  chmod +x "$APP_BIN"
  echo ""
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "✅ Build completed successfully!"
  echo ""
  echo "📦 Location: $OUTPUT_DIR"
  echo "📏 Size: $(du -sh "$OUTPUT_DIR" | cut -f1)"
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo ""
  
  read -p "🧪 Do you want to run the application now? [y/N]: " run_choice
  
  if [[ "$run_choice" =~ ^[Yy]$ ]]; then
    # Enable development mode in config
    CONFIG_FILE="$HOME/.config/XIVTheCalamity/config.json"
    if [ -f "$CONFIG_FILE" ]; then
      echo "🔧 Enabling development mode in config..."
      if command -v jq &> /dev/null; then
        TMP_FILE=$(mktemp)
        jq '.launcher.developmentMode = true' "$CONFIG_FILE" > "$TMP_FILE" && mv "$TMP_FILE" "$CONFIG_FILE"
      else
        sed -i 's/"developmentMode": false/"developmentMode": true/g' "$CONFIG_FILE"
      fi
      echo "✅ Development mode enabled"
    fi

    echo ""
    echo "🚀 Starting XIVTheCalamity..."
    "$APP_BIN"
  else
    echo ""
    echo "💡 You can run it manually with:"
    echo "   $APP_BIN"
  fi
else
  echo "❌ Executable not found at $APP_BIN"
  exit 1
fi
