#!/bin/bash
# Auto-update checker for RustRconServerManager
# Runs on container startup to check if a new version is available

set -e

# Check if we should skip auto-update (for manual control)
if [ "$SKIP_AUTO_UPDATE" = "true" ]; then
    echo "Auto-update disabled. Starting application..."
    exit 0
fi

GITHUB_REPO="Xenne93/RustRconServerManager"
VERSION_FILE="/app/.version"
UPDATE_MARKER="/app/.update_available"

echo "Checking for updates from ${GITHUB_REPO}..."

# Releases are public, so no auth is required. If GITHUB_TOKEN happens to be set
# (e.g. to avoid GitHub's unauthenticated rate limit), it's used anyway.
AUTH_HEADER=()
if [ -n "$GITHUB_TOKEN" ]; then
    AUTH_HEADER=(-H "Authorization: Bearer ${GITHUB_TOKEN}")
fi

# Fetch latest release info
RELEASE_DATA=$(curl -s "${AUTH_HEADER[@]}" \
    "https://api.github.com/repos/${GITHUB_REPO}/releases/latest" || echo "")

if [ -z "$RELEASE_DATA" ]; then
    echo "Failed to fetch release information. Starting with current version..."
    exit 0
fi

# Get latest version tag
LATEST_VERSION=$(echo "${RELEASE_DATA}" | jq -r '.tag_name' 2>/dev/null || echo "")

if [ -z "$LATEST_VERSION" ] || [ "$LATEST_VERSION" = "null" ]; then
    echo "Could not determine latest version. Starting with current version..."
    exit 0
fi

# Read current version (if exists)
CURRENT_VERSION=""
if [ -f "$VERSION_FILE" ]; then
    CURRENT_VERSION=$(cat "$VERSION_FILE")
fi

echo "Current version: ${CURRENT_VERSION:-unknown}"
echo "Latest version: ${LATEST_VERSION}"

# Compare versions
if [ "$CURRENT_VERSION" = "$LATEST_VERSION" ]; then
    echo "✓ Already running the latest version"
    rm -f "$UPDATE_MARKER"
    exit 0
fi

# New version available
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "⚠️  NEW VERSION AVAILABLE: ${LATEST_VERSION}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Current: ${CURRENT_VERSION:-unknown}"
echo "Latest:  ${LATEST_VERSION}"
echo ""

# Create update marker file
echo "$LATEST_VERSION" > "$UPDATE_MARKER"

# Check if auto-update is enabled. The Panel Settings page in the app writes this flag
# file whenever the toggle there changes, so it's the source of truth once the app has
# started at least once - the AUTO_UPDATE env var is only the fallback for before that
# (e.g. before initial setup has run).
AUTO_UPDATE_FLAG_FILE="/app/data/autoupdate.flag"
if [ -f "$AUTO_UPDATE_FLAG_FILE" ]; then
    AUTO_UPDATE=$(cat "$AUTO_UPDATE_FLAG_FILE")
fi

if [ "$AUTO_UPDATE" = "false" ]; then
    echo "Auto-update is disabled. To update manually:"
    echo "  docker compose pull && docker compose up -d"
    echo ""
    echo "Starting application with current version..."
    exit 0
fi

# Perform auto-update
echo "Auto-update is enabled. Downloading new version..."
echo ""

# Create temporary directory for update
TEMP_DIR="/tmp/rrsm-update-$$"
mkdir -p "$TEMP_DIR"
cd "$TEMP_DIR"

# Get Linux asset URL
ASSET_URL=$(echo "${RELEASE_DATA}" | jq -r '.assets[] | select(.name | contains("Linux-x64")) | .url')
ASSET_NAME=$(echo "${RELEASE_DATA}" | jq -r '.assets[] | select(.name | contains("Linux-x64")) | .name')

if [ -z "$ASSET_URL" ] || [ "$ASSET_URL" = "null" ]; then
    echo "ERROR: No Linux-x64 asset found for version ${LATEST_VERSION}"
    echo "Starting with current version..."
    exit 0
fi

echo "Downloading ${ASSET_NAME}..."

# Download new release
curl -L "${AUTH_HEADER[@]}" \
    -H "Accept: application/octet-stream" \
    -o "${ASSET_NAME}" "${ASSET_URL}"

if [ ! -f "${ASSET_NAME}" ]; then
    echo "ERROR: Failed to download update"
    echo "Starting with current version..."
    rm -rf "$TEMP_DIR"
    exit 0
fi

# Extract new version
echo "Extracting update..."
mkdir -p extracted
tar -xzf "${ASSET_NAME}" -C extracted

# Backup current version (keep only the executable and critical files)
echo "Backing up current version..."
BACKUP_DIR="/app/backup-${CURRENT_VERSION:-old}"
mkdir -p "$BACKUP_DIR"
cp /app/RustRconServerManager.Backend "$BACKUP_DIR/" 2>/dev/null || true
cp /app/appsettings.json "$BACKUP_DIR/" 2>/dev/null || true

# Stop current process gracefully (send signal to parent process if needed)
# Note: This script runs before the app starts, so no need to stop

# Replace application files (preserve data directory and certificates)
echo "Installing update..."
cd extracted

# Remove old binaries and libraries, but keep data, certs, and config
find /app -maxdepth 1 -type f \( -name "*.dll" -o -name "*.so" -o -name "RustRconServerManager.Backend" \) -delete
find /app -maxdepth 1 -type f -name "*.json" ! -name "appsettings.json" -delete

# Copy new files (excluding what should be preserved)
cp -r * /app/
chmod +x /app/RustRconServerManager.Backend

# Update version file
echo "$LATEST_VERSION" > "$VERSION_FILE"

# Cleanup
cd /
rm -rf "$TEMP_DIR"
rm -f "$UPDATE_MARKER"

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ Successfully updated to version ${LATEST_VERSION}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Starting application..."
