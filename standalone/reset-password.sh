#!/bin/bash
# Resets an admin account's password from the terminal - for when you're locked out and
# either don't have SMTP configured for the "forgot password" email flow, or just prefer
# not to depend on it. Requires the instance to already be running (started via
# ./start.sh in another terminal/session) since it connects to that same running bundled
# MariaDB rather than starting a second one.

set -e
cd "$(dirname "$0")"

ENV_FILE="$(pwd)/standalone.env"
if [ ! -f "$ENV_FILE" ]; then
    echo "standalone.env not found - has this instance been started at least once (./start.sh)?"
    exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

MARIADB_PORT=3307
DB_NAME="rustrconservermanager"
DB_USER="rustrconservermanager"

export ConnectionStrings__DefaultConnection="Server=127.0.0.1;Port=${MARIADB_PORT};Database=${DB_NAME};User=${DB_USER};Password=${DB_PASSWORD};"
export Jwt__Key="${JWT_KEY}"
export Jwt__Issuer="RustRconServerManager"
export Jwt__Audience="RustRconServerManager"
export RconEncryption__Key="${RCON_ENCRYPTION_KEY}"
export ASPNETCORE_ENVIRONMENT="Production"

# Run from inside app/ - same reason as start.sh: some file lookups are relative to the
# current directory.
cd app
./RustRconServerManager.Backend --reset-password
