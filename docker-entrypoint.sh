#!/bin/bash
# Docker entrypoint for RustRconServerManager
# Checks for updates before starting the application

set -e

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "RustRconServerManager Container Starting"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Run update check
if [ -f /app/check-update.sh ]; then
    echo "Running update check..."
    bash /app/check-update.sh || true
    echo ""
fi

# Check if version file exists, if not create it from current build
if [ ! -f /app/.version ] && [ -f /app/.build_version ]; then
    cp /app/.build_version /app/.version
fi

# Display current version
if [ -f /app/.version ]; then
    VERSION=$(cat /app/.version)
    echo "Running version: ${VERSION}"
else
    echo "Running version: unknown"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Starting RustRconServerManager Application"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Execute the application
exec /app/RustRconServerManager.Backend "$@"
