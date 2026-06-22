#!/bin/bash
set -e

# Change directory to the script folder
cd "$(dirname "$0")"

mkdir -p ../shared/resources/bin
swiftc AudioRouter.swift -framework CoreAudio -framework AudioToolbox -o ../shared/resources/bin/XTCAudioRouter
echo "XTCAudioRouter compiled successfully!"
