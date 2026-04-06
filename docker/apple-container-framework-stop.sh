#!/bin/bash

# --- 1. Remove Containers ---
echo "🛑 Removing containers..."
container rm -f azurite proxy registration 2>/dev/null

# --- 2. Stop Container Framework ---
echo "🛑 Stopping Apple container framework..."
container system stop

echo "✨ Done."
