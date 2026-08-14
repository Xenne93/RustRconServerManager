#!/bin/bash
# RustRconServerManager installer.
# Downloads docker-compose.yml (if not already present), generates a .env with random
# credentials, then pulls and starts the published Docker images. Works standalone -
# you do NOT need to clone the full source repo, just this one script.
#
# Usage (interactive):
#   ./install.sh
#
# Usage (non-interactive):
#   ./install.sh --port <port> --pathbase <path>
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/Xenne93/RustRconServerManager/main/install.sh | bash

set -e

REPO_RAW_BASE="https://raw.githubusercontent.com/Xenne93/RustRconServerManager/main"

generate_password() {
    # Alphanumeric only, to avoid breaking connection strings / env files
    tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 32
}

APP_PORT=""
PATHBASE=""
INTERACTIVE=true

while [[ $# -gt 0 ]]; do
    case $1 in
        --port)
            APP_PORT="$2"; INTERACTIVE=false; shift 2 ;;
        --pathbase)
            PATHBASE="$2"; INTERACTIVE=false; shift 2 ;;
        --help)
            echo "RustRconServerManager installer"
            echo ""
            echo "Usage: $0 [--port <port>] [--pathbase <path>]"
            echo "Run with no arguments for an interactive prompt."
            exit 0 ;;
        *)
            echo "Unknown parameter: $1"
            echo "Use --help for usage information"
            exit 1 ;;
    esac
done

echo "=== RustRconServerManager Installer ==="
echo ""

# --- Prerequisites ---
if ! command -v docker &> /dev/null; then
    echo "Docker is not installed. Install Docker first: https://docs.docker.com/get-docker/"
    exit 1
fi

if ! docker compose version &> /dev/null; then
    echo "The 'docker compose' plugin is not available. Install/update Docker to get it."
    exit 1
fi

# --- Fetch docker-compose.yml if needed ---
if [ -f docker-compose.yml ]; then
    echo "Found existing docker-compose.yml, leaving it as is."
else
    echo "Downloading docker-compose.yml..."
    curl -fsSL "${REPO_RAW_BASE}/docker-compose.yml" -o docker-compose.yml
fi

# --- Generate .env ---
if [ -f .env ]; then
    echo "A .env file already exists here."
    read -p "Overwrite it with newly generated credentials? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Keeping the existing .env."
    else
        rm -f .env
    fi
fi

if [ ! -f .env ]; then
    if [ "$INTERACTIVE" = true ]; then
        read -p "HTTP port to expose (default: 5000): " APP_PORT
        read -p "PathBase, if running behind a reverse proxy at a subpath (leave empty for root): " PATHBASE
    fi

    APP_PORT=${APP_PORT:-5000}

    echo ""
    echo "Generating random credentials..."
    DB_NAME="rustrconservermanager"
    DB_USER="rustrconservermanager"
    DB_PASSWORD=$(generate_password)
    MARIADB_ROOT_PASSWORD=$(generate_password)
    JWT_KEY=$(generate_password)$(generate_password)
    RCON_ENCRYPTION_KEY=$(generate_password)

    cat > .env << EOF
# ==============================================
# RustRconServerManager configuration
# Generated: $(date)
# ==============================================

APP_PORT=${APP_PORT}
PATHBASE=${PATHBASE}

# Database (bundled MariaDB container - see docker-compose.yml)
DB_NAME=${DB_NAME}
DB_USER=${DB_USER}
DB_PASSWORD=${DB_PASSWORD}
MARIADB_ROOT_PASSWORD=${MARIADB_ROOT_PASSWORD}

# JWT
JWT_KEY=${JWT_KEY}
JWT_ISSUER=RustRconServerManager
JWT_AUDIENCE=RustRconServerManager

# RCON connection encryption
RCON_ENCRYPTION_KEY=${RCON_ENCRYPTION_KEY}

# SMTP (required for the "forgot password" email flow - leave blank to disable it)
SMTP_HOST=
SMTP_PORT=587
SMTP_USER=
SMTP_PASSWORD=
SMTP_FROM=

# Optional integrations
STEAM_API_KEY=
PROXYCHECK_API_KEY=
PROXYCHECK_HMAC_KEY=

# Auto-update
AUTO_UPDATE=true
SKIP_AUTO_UPDATE=false

# Which published image tag to run
IMAGE_TAG=latest
EOF

    chmod 600 .env
    echo "✓ Configuration written to .env"
fi

# Pick up APP_PORT from an existing .env if we didn't just generate one
APP_PORT=$(grep -E '^APP_PORT=' .env | cut -d= -f2)
APP_PORT=${APP_PORT:-5000}

# --- Pull and deploy ---
echo ""
echo "Pulling the latest images..."
docker compose pull

echo ""
echo "Starting the stack..."
docker compose up -d

echo ""
echo "✓ RustRconServerManager is starting up."
echo ""
echo "  View logs:    docker compose logs -f"
echo "  Stop:         docker compose down"
echo ""
echo "Open http://localhost:${APP_PORT} and create your admin account - the first person"
echo "to open the panel gets to do this, so don't expose the port publicly until you've"
echo "completed it."
