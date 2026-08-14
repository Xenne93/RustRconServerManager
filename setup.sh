#!/bin/bash
# RustRconServerManager self-hosted setup script
# Generates a .env file with random credentials, then you're ready for `docker compose up -d`.
#
# Usage (interactive):
#   ./setup.sh
#
# Usage (non-interactive):
#   ./setup.sh --port <port> --pathbase <path>

set -e

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
            echo "RustRconServerManager self-hosted setup"
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

echo "=== RustRconServerManager Setup ==="
echo ""

if [ -f .env ]; then
    echo "A .env file already exists here."
    read -p "Overwrite it with newly generated credentials? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Setup cancelled - your existing .env was left untouched."
        exit 0
    fi
fi

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

echo ""
echo "✓ Configuration written to .env"
echo ""
echo "Next steps:"
echo "  docker compose up -d"
echo "  docker compose logs -f"
echo ""
echo "Then open http://localhost:${APP_PORT} and create your admin account - the first"
echo "person to open the panel gets to do this, so don't expose the port publicly until"
echo "you've completed it."
