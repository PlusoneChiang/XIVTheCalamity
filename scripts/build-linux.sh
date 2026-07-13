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

# ================== Set up WebKitGTK Compatibility Links ==================
echo ""
echo "🔗 Checking WebKit2GTK compatibility..."
# If old webkit2gtk 4.0 is not in default search directories, check if 4.1 is available to symlink it
if ! ldconfig -p 2>/dev/null | grep -q "libwebkit2gtk-4.0.so.37"; then
  if [ -f "/usr/lib64/libwebkit2gtk-4.1.so.0" ]; then
    ln -sf /usr/lib64/libwebkit2gtk-4.1.so.0 "$OUTPUT_DIR/libwebkit2gtk-4.0.so.37"
    echo "   🔗 Created symlink for libwebkit2gtk-4.0.so.37 -> libwebkit2gtk-4.1.so.0"
  fi
  if [ -f "/usr/lib64/libjavascriptcoregtk-4.1.so.0" ]; then
    ln -sf /usr/lib64/libjavascriptcoregtk-4.1.so.0 "$OUTPUT_DIR/libjavascriptcoregtk-4.0.so.18"
    echo "   🔗 Created symlink for libjavascriptcoregtk-4.0.so.18 -> libjavascriptcoregtk-4.1.so.0"
  fi
fi

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
  echo ""
  
  # ================== Package AppImage ==================
  echo ""
  echo "📦 Packaging AppImage..."
  APPDIR="$RELEASE_DIR/linux-x64/AppDir"
  rm -rf "$APPDIR"
  mkdir -p "$APPDIR/usr/bin"
  mkdir -p "$APPDIR/usr/share/resources"
  
  cp "$OUTPUT_DIR/XIVTheCalamity" "$APPDIR/usr/bin/XIVTheCalamity"
  cp "$OUTPUT_DIR/Photino.Native.so" "$APPDIR/usr/bin/Photino.Native.so"
  cp -R "$PROJECT_ROOT/shared/resources/"* "$APPDIR/usr/share/resources/"
  
  # Copy icon
  if [ -f "$PROJECT_ROOT/frontend/build/icons/256x256.png" ]; then
    cp "$PROJECT_ROOT/frontend/build/icons/256x256.png" "$APPDIR/XIVTheCalamity.png"
  fi
  
  # Desktop entry
  cat << 'EOF' > "$APPDIR/XIVTheCalamity.desktop"
[Desktop Entry]
Type=Application
Name=XIVTheCalamity
Comment=Cross-platform FFXIV Launcher
Exec=XIVTheCalamity %U
Icon=XIVTheCalamity
Categories=Game;
EOF

  # AppRun with WebKitGTK compatibility and local lib search
  cat << 'EOF' > "$APPDIR/AppRun"
#!/bin/sh
SELF=$(readlink -f "$0")
HERE=$(dirname "$SELF")

# Setup WebKitGTK compatibility folder if system lacks WebKit 4.0 but has 4.1
if ! ldconfig -p 2>/dev/null | grep -q "libwebkit2gtk-4.0.so.37"; then
  COMPAT_DIR="/tmp/xivtc-compat-${USER}"
  mkdir -p "$COMPAT_DIR"
  
  # Find libwebkit2gtk-4.1.so.0 path
  WEBKIT_PATH=""
  for p in "/usr/lib64/libwebkit2gtk-4.1.so.0" "/usr/lib/x86_64-linux-gnu/libwebkit2gtk-4.1.so.0" "/usr/lib/libwebkit2gtk-4.1.so.0"; do
    if [ -f "$p" ]; then
      WEBKIT_PATH="$p"
      break
    fi
  done
  
  # Find libjavascriptcoregtk-4.1.so.0 path
  JSC_PATH=""
  for p in "/usr/lib64/libjavascriptcoregtk-4.1.so.0" "/usr/lib/x86_64-linux-gnu/libjavascriptcoregtk-4.1.so.0" "/usr/lib/libjavascriptcoregtk-4.1.so.0"; do
    if [ -f "$p" ]; then
      JSC_PATH="$p"
      break
    fi
  done
  
  if [ -n "$WEBKIT_PATH" ]; then
    ln -sf "$WEBKIT_PATH" "$COMPAT_DIR/libwebkit2gtk-4.0.so.37"
  fi
  if [ -n "$JSC_PATH" ]; then
    ln -sf "$JSC_PATH" "$COMPAT_DIR/libjavascriptcoregtk-4.0.so.18"
  fi
  
  export LD_LIBRARY_PATH="$COMPAT_DIR:$LD_LIBRARY_PATH"
fi

export LD_LIBRARY_PATH="${HERE}/usr/bin:$LD_LIBRARY_PATH"
export PATH="${HERE}/usr/bin:${PATH}"
exec XIVTheCalamity "$@"
EOF
  chmod +x "$APPDIR/AppRun"

  # Find or download appimagetool
  if ! command -v appimagetool &> /dev/null; then
    APPIMAGETOOL="$RELEASE_DIR/appimagetool"
    if [ ! -f "$APPIMAGETOOL" ]; then
      echo "   📥 appimagetool not found, downloading to Release/appimagetool..."
      curl -Lo "$APPIMAGETOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
      chmod +x "$APPIMAGETOOL"
    fi
  else
    APPIMAGETOOL="appimagetool"
  fi

  # Test if appimagetool can run directly (FUSE check)
  APPIMAGETOOL_CMD=("$APPIMAGETOOL")
  FUSE_AVAILABLE=true
  if ! "$APPIMAGETOOL" --version &> /dev/null; then
    # Try with --appimage-extract-and-run
    if "$APPIMAGETOOL" --appimage-extract-and-run --version &> /dev/null; then
      echo "   ⚠️ FUSE is not available or cannot mount. Running appimagetool with --appimage-extract-and-run..."
      APPIMAGETOOL_CMD=("$APPIMAGETOOL" "--appimage-extract-and-run")
      FUSE_AVAILABLE=false
    fi
  fi

  # Build AppImage
  VERSION=$(node -p "require('fs').readFileSync('$PROJECT_ROOT/backend/src/XIVTheCalamity/XIVTheCalamity.csproj', 'utf8').match(/<Version>(.*?)<\/Version>/)[1]")
  export ARCH=x86_64
  "${APPIMAGETOOL_CMD[@]}" "$APPDIR" "$RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage"
  echo ""
  echo "🎉 AppImage packaged successfully!"
  echo "📦 Location: $RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage"
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

    # Run the AppImage if we packaged it, otherwise run the unpacked version
    VERSION=$(node -p "require('fs').readFileSync('$PROJECT_ROOT/backend/src/XIVTheCalamity/XIVTheCalamity.csproj', 'utf8').match(/<Version>(.*?)<\/Version>/)[1]")
    if [ -f "$RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage" ]; then
      echo ""
      echo "🚀 Starting XIVTheCalamity (AppImage)..."
      if [ "$FUSE_AVAILABLE" = "true" ]; then
        "$RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage"
      else
        echo "   ⚠️ FUSE not available, running AppImage with --appimage-extract-and-run..."
        "$RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage" --appimage-extract-and-run
      fi
    else
      echo ""
      echo "🚀 Starting XIVTheCalamity (Unpacked)..."
      LD_LIBRARY_PATH="$OUTPUT_DIR:$LD_LIBRARY_PATH" "$APP_BIN"
    fi
  else
    VERSION=$(node -p "require('fs').readFileSync('$PROJECT_ROOT/backend/src/XIVTheCalamity/XIVTheCalamity.csproj', 'utf8').match(/<Version>(.*?)<\/Version>/)[1]")
    echo ""
    echo "💡 You can run it manually with:"
    echo "   LD_LIBRARY_PATH=\"$OUTPUT_DIR\" $APP_BIN"
    if [ -f "$RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage" ]; then
      echo "   or run the AppImage:"
      echo "   $RELEASE_DIR/XIVTheCalamity-${VERSION}-linux-x64.AppImage"
    fi
  fi
else
  echo "❌ Executable not found at $APP_BIN"
  exit 1
fi
