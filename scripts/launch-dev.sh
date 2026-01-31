#!/bin/bash

# Launch XIV The Calamity with Development mode enabled
# This script enables development mode in config for debug logging

set -e

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PATH="$PROJECT_ROOT/Release/mac-arm64/XIVTheCalamity.app"
CONFIG_FILE="$HOME/Library/Application Support/XIVTheCalamity/config.json"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo ""
echo -e "${BLUE}🚀 Launching XIV The Calamity (Development Mode)${NC}"
echo ""

if [ ! -d "$APP_PATH" ]; then
  echo "❌ Application not found at: $APP_PATH"
  echo "   Please run build-and-test.sh first"
  exit 1
fi

# Enable development mode in config
if [ -f "$CONFIG_FILE" ]; then
  echo "🔧 Enabling development mode in config..."
  
  # Use jq if available, otherwise use simple sed
  if command -v jq &> /dev/null; then
    TMP_FILE=$(mktemp)
    jq '.launcher.developmentMode = true' "$CONFIG_FILE" > "$TMP_FILE" && mv "$TMP_FILE" "$CONFIG_FILE"
    echo -e "${GREEN}✅ Development mode enabled${NC}"
  else
    # Simple replacement for basic config structure
    sed -i '' 's/"developmentMode": false/"developmentMode": true/g' "$CONFIG_FILE"
    echo -e "${GREEN}✅ Development mode enabled (via sed)${NC}"
  fi
else
  echo -e "${YELLOW}⚠️  Config file not found${NC}"
  echo "   Will be created on first launch with default settings"
  echo "   You can enable development mode in settings later"
fi

echo ""
echo "🚀 Launching application..."

# Launch app
open "$APP_PATH"

echo ""
echo -e "${GREEN}✅ Application launched${NC}"
echo ""
echo "📝 Backend will use Development environment with debug logs enabled"
echo ""
echo "View logs:"
echo "  Backend: tail -f ~/Library/Application\ Support/XIVTheCalamity/logs/backend-*.log"
echo "  Frontend: tail -f ~/Library/Application\ Support/XIVTheCalamity/logs/app-*.log"
echo ""
echo "💡 Look for: [DBG] or [Debug] level logs in backend"
echo ""
echo "To disable development mode:"
echo "  Set launcher.developmentMode = false in config.json"
echo ""
