#!/bin/bash
# XTCAudioRouter Build Script
# Builds the macOS audio routing CLI tool

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$SCRIPT_DIR"

# Default to release build
BUILD_CONFIG="${1:-release}"

echo "Building XTCAudioRouter ($BUILD_CONFIG)..."

if [ "$BUILD_CONFIG" == "release" ]; then
    swift build -c release
    BINARY_PATH=".build/release/XTCAudioRouter"
else
    swift build
    BINARY_PATH=".build/debug/XTCAudioRouter"
fi

if [ -f "$BINARY_PATH" ]; then
    echo "Build successful: $BINARY_PATH"
    
    # Show binary info
    file "$BINARY_PATH"
    ls -lh "$BINARY_PATH"
    
    # Copy to shared resources for bundling
    if [ "$BUILD_CONFIG" == "release" ]; then
        DEST_DIR="$PROJECT_ROOT/shared/resources/bin"
        mkdir -p "$DEST_DIR"
        cp "$BINARY_PATH" "$DEST_DIR/"
        echo "Copied to: $DEST_DIR/XTCAudioRouter"
    fi
else
    echo "Build failed: binary not found"
    exit 1
fi
