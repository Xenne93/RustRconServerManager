#!/bin/bash
# RustRconServerManager standalone launcher (Linux)
#
# Starts the bundled, port-isolated MariaDB instance next to this script (first run:
# initializes it and generates random credentials, including a real root password - it
# never runs passwordless after startup), then starts the app configured to use it. The
# bundled MariaDB only listens on 127.0.0.1:3307 - it is not reachable from outside this
# machine and does not touch any other MariaDB/MySQL install you may already have
# running on the default port. It exists purely so this package doesn't require a
# separately installed database server.
#
# Credentials and the database files persist in standalone.env / mariadb-data next to
# this script, so they survive restarts. Back both up before replacing this folder with
# a newer release.

set -e
cd "$(dirname "$0")"

GITHUB_REPO="Xenne93/RustRconServerManager"

# Checks GitHub Releases for a newer version and, if the auto-update setting (Panel
# Settings page, mirrored to app/data/autoupdate.flag) allows it, replaces the contents
# of app/ with the new release. Only app/ is ever touched - mariadb/, mariadb-data/,
# standalone.env, and this script itself are left alone, so a failed or partial update
# can't take the database or its credentials down with it.
check_for_update() {
    local version_file="./app/.version"
    if [ ! -f "$version_file" ]; then
        echo "No .version file found - skipping update check (this release predates auto-update support)."
        return
    fi

    local current_version
    current_version=$(cat "$version_file")

    local auto_update="true"
    if [ -f "./app/data/autoupdate.flag" ]; then
        auto_update=$(cat "./app/data/autoupdate.flag")
    fi

    echo "Checking for updates (current: ${current_version})..."

    local release_data
    release_data=$(curl -fsSL --max-time 15 "https://api.github.com/repos/${GITHUB_REPO}/releases/latest" 2>/dev/null || echo "")
    if [ -z "$release_data" ]; then
        echo "Could not reach GitHub - skipping update check."
        return
    fi

    local latest_version
    latest_version=$(echo "$release_data" | grep -o '"tag_name": *"[^"]*"' | head -1 | sed -E 's/.*"([^"]+)"$/\1/')

    if [ -z "$latest_version" ]; then
        echo "Could not determine the latest version - skipping update check."
        return
    fi

    if [ "$latest_version" = "$current_version" ]; then
        echo "Already on the latest version."
        return
    fi

    echo "New version available: ${latest_version} (current: ${current_version})"

    if [ "$auto_update" != "true" ]; then
        echo "Auto-update is disabled in Panel Settings - not installing automatically."
        return
    fi

    local asset_url
    asset_url=$(echo "$release_data" | grep -o '"browser_download_url": *"[^"]*Linux-x64[^"]*"' | head -1 | sed -E 's/.*"(https[^"]+)"/\1/')

    if [ -z "$asset_url" ]; then
        echo "Could not find a Linux-x64 asset for ${latest_version} - skipping update."
        return
    fi

    echo "Downloading ${latest_version}..."
    local tmp_dir
    tmp_dir=$(mktemp -d)

    if ! curl -fsSL --max-time 300 "$asset_url" -o "${tmp_dir}/release.tar.gz"; then
        echo "Download failed - skipping update."
        rm -rf "$tmp_dir"
        return
    fi

    mkdir -p "${tmp_dir}/extracted"
    tar -xzf "${tmp_dir}/release.tar.gz" -C "${tmp_dir}/extracted"

    if [ ! -d "${tmp_dir}/extracted/app" ]; then
        echo "Downloaded release has an unexpected layout - skipping update."
        rm -rf "$tmp_dir"
        return
    fi

    echo "Installing update..."
    find ./app -maxdepth 1 -type f \( -name "*.dll" -o -name "RustRconServerManager.Backend" \) -delete
    find ./app -maxdepth 1 -type f -name "*.json" ! -name "appsettings.json" -delete
    cp -r "${tmp_dir}/extracted/app/." ./app/

    rm -rf "$tmp_dir"
    echo "Updated to ${latest_version}."
}

check_for_update

MARIADB_DIR="$(pwd)/mariadb"
DATA_DIR="$(pwd)/mariadb-data"
MARIADB_PORT=3307
PID_FILE="$DATA_DIR/mysqld.pid"
ENV_FILE="$(pwd)/standalone.env"
CURRENT_USER="$(id -un)"
DB_NAME="rustrconservermanager"
DB_USER="rustrconservermanager"

generate_secret() {
    tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 32
}

port_is_open() {
    timeout 1 bash -c "echo > /dev/tcp/127.0.0.1/${MARIADB_PORT}" 2>/dev/null
}

wait_for_port() {
    for i in $(seq 1 30); do
        if port_is_open; then
            return 0
        fi
        sleep 1
    done
    return 1
}

if [ ! -f "$ENV_FILE" ]; then
    echo "First run - generating credentials..."

    DB_PASSWORD=$(generate_secret)
    MARIADB_ROOT_PASSWORD=$(generate_secret)
    JWT_KEY=$(generate_secret)$(generate_secret)
    RCON_ENCRYPTION_KEY=$(generate_secret)

    cat > "$ENV_FILE" << EOF
DB_PASSWORD=${DB_PASSWORD}
MARIADB_ROOT_PASSWORD=${MARIADB_ROOT_PASSWORD}
JWT_KEY=${JWT_KEY}
RCON_ENCRYPTION_KEY=${RCON_ENCRYPTION_KEY}
EOF
    chmod 600 "$ENV_FILE"
fi

if [ ! -d "$DATA_DIR" ]; then
    echo "Initializing the local database..."
    mkdir -p "$DATA_DIR"
    "$MARIADB_DIR/scripts/mariadb-install-db" \
        --basedir="$MARIADB_DIR" \
        --datadir="$DATA_DIR" \
        --skip-test-db \
        --user="$CURRENT_USER"
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

# Real Windows test runs kept getting "Access denied" for root no matter which
# combination of a password flag at install time, a passwordless bootstrap, or SET
# PASSWORD was tried - MariaDB's exact host-matching behavior for a 127.0.0.1 connection
# turned out to be inconsistent/unpredictable across attempts. So instead of guessing,
# every run starts MariaDB with --skip-grant-tables (the standard "reset a password you
# can't get in with" technique) and fixes root's password from inside that session (see
# below for exactly how). This is idempotent, so it runs on every start, not just the
# first one, which also means a partially-failed previous attempt can't leave things in
# a broken state.
echo "Starting bundled MariaDB on 127.0.0.1:${MARIADB_PORT}..."
"$MARIADB_DIR/bin/mariadbd" \
    --basedir="$MARIADB_DIR" \
    --datadir="$DATA_DIR" \
    --socket="$DATA_DIR/mysqld.sock" \
    --port="$MARIADB_PORT" \
    --bind-address=127.0.0.1 \
    --pid-file="$PID_FILE" \
    --user="$CURRENT_USER" \
    --skip-grant-tables &
MARIADB_PID=$!

cleanup() {
    echo ""
    echo "Shutting down..."
    "$MARIADB_DIR/bin/mariadb-admin" -h 127.0.0.1 -P "$MARIADB_PORT" -u root "--password=${MARIADB_ROOT_PASSWORD}" shutdown 2>/dev/null || kill "$MARIADB_PID" 2>/dev/null
    wait "$MARIADB_PID" 2>/dev/null
}
trap cleanup EXIT INT TERM

echo "Waiting for the database to be ready..."
if ! wait_for_port; then
    echo "MariaDB did not become ready in time. Check the output above for errors."
    exit 1
fi

echo "Securing the root account..."
# Neither a direct UPDATE on mysql.user (the classic technique - it's a view on modern
# MariaDB, "Column 'authentication_string' is not updatable") nor a self-targeting SET
# PASSWORD (it does its own account lookup keyed on how this connection got resolved,
# which turned out not to match any real row either - "ERROR 1133: Can't find any
# matching row in the user table") worked on a real test. mysql.global_priv is what
# mysql.user is really a view over as of MariaDB 10.4 - a real base table, so a plain
# UPDATE/JSON_SET on it isn't blocked by either problem. WHERE User='root' with no Host
# qualifier fixes every root row regardless of host pattern, so there's nothing left to
# guess.
"$MARIADB_DIR/bin/mariadb" -h 127.0.0.1 -P "$MARIADB_PORT" -u root << SQL
UPDATE mysql.global_priv SET Priv = JSON_SET(Priv, '\$.plugin', 'mysql_native_password', '\$.authentication_string', PASSWORD('${MARIADB_ROOT_PASSWORD}')) WHERE User = 'root';
FLUSH PRIVILEGES;
SQL

echo "Creating application database and user..."
# A fresh connection using the password just set above, rather than continuing on the
# same --skip-grant-tables session - that session's own claimed identity didn't match a
# real grant table row (see above), so it's not certain it would still carry real
# privileges once FLUSH PRIVILEGES re-enables enforcement. A new connection
# authenticating properly against root's now-corrected row doesn't have that ambiguity.
# MariaDB has been observed reporting a 127.0.0.1 connection's origin as both
# '127.0.0.1' and 'localhost' in different situations, so the app user is granted under
# both host patterns defensively.
"$MARIADB_DIR/bin/mariadb" -h 127.0.0.1 -P "$MARIADB_PORT" -u root "--password=${MARIADB_ROOT_PASSWORD}" << SQL
CREATE DATABASE IF NOT EXISTS ${DB_NAME};
CREATE USER IF NOT EXISTS '${DB_USER}'@'127.0.0.1' IDENTIFIED BY '${DB_PASSWORD}';
CREATE USER IF NOT EXISTS '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASSWORD}';
GRANT ALL PRIVILEGES ON ${DB_NAME}.* TO '${DB_USER}'@'127.0.0.1';
GRANT ALL PRIVILEGES ON ${DB_NAME}.* TO '${DB_USER}'@'localhost';
FLUSH PRIVILEGES;
SQL

export ConnectionStrings__DefaultConnection="Server=127.0.0.1;Port=${MARIADB_PORT};Database=${DB_NAME};User=${DB_USER};Password=${DB_PASSWORD};"
export Jwt__Key="${JWT_KEY}"
export Jwt__Issuer="RustRconServerManager"
export Jwt__Audience="RustRconServerManager"
export RconEncryption__Key="${RCON_ENCRYPTION_KEY}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://+:5000}"
export ASPNETCORE_ENVIRONMENT="Production"

echo ""
echo "Starting RustRconServerManager..."
echo "Open http://localhost:5000 in your browser once it's up."
echo ""

# Run from inside app/ - the app looks up some files (wwwroot, appsettings.json)
# relative to the current directory, not just next to the executable, so launching it
# from the package root (where this script lives) breaks static file serving.
cd app
./RustRconServerManager.Backend
