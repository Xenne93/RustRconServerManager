# Docker Deployment Guide

RustRconServerManager ships as a self-contained Docker Compose stack: the app container plus a
bundled MariaDB container. No external database or reverse proxy is required to get started.

## Prerequisites

- Docker and Docker Compose
- A free port on the host (default `5000`)

## Quick Start

```bash
git clone https://github.com/Xenne93/RustRconServerManager.git
cd RustRconServerManager
./setup.sh          # generates .env with random credentials
docker compose up -d
```

Then open `http://localhost:5000` (or whatever `APP_PORT` you chose) and create your admin
account. The first person to open the panel gets to create it, so don't expose the port publicly
until you've completed this step.

If you'd rather configure `.env` by hand, copy `.env.example` to `.env` and fill in real values
instead of running `setup.sh`.

## What's in the stack

```
docker compose up -d
        │
        ├── app       (RustRconServerManager, port 5000)
        │       depends on ──► mariadb (healthy)
        └── mariadb   (persistent volume: db_data)
```

- `app` pulls the published image from `ghcr.io` by default. To build from source instead, edit
  `docker-compose.yml`: comment out the `image`/`pull_policy` lines and uncomment the `build:` block.
- `mariadb` stores its data in the `db_data` named volume - it survives `docker compose down` and
  image updates. Only `docker compose down -v` removes it.
- Application data (uploaded map images, etc.) lives in the `app_data` named volume.

## Environment Variables

All configuration lives in `.env` (see `.env.example` for the full list with comments). The most
relevant ones:

| Variable | Purpose |
|---|---|
| `IMAGE_TAG` | Which published image tag to run (`latest` or a specific release, e.g. `v2026.08.10.451`) |
| `APP_PORT` | Host port the app is exposed on |
| `PATHBASE` | Optional path prefix if running behind a reverse proxy at a subpath |
| `DB_NAME` / `DB_USER` / `DB_PASSWORD` / `MARIADB_ROOT_PASSWORD` | Bundled MariaDB credentials |
| `JWT_KEY` / `RCON_ENCRYPTION_KEY` | Generate long random values - never reuse the example values |
| `SMTP_*` | Needed for the "forgot password" email flow. Leave `SMTP_HOST` blank to disable it |
| `STEAM_API_KEY` | Optional - player profile/VAC-ban lookups. Can also be set (or overridden) at runtime from Panel Settings > Steam API Key, without a restart |
| `PROXYCHECK_API_KEY` / `PROXYCHECK_HMAC_KEY` | Optional - VPN/proxy detection in Server Protection |
| `AUTO_UPDATE` / `SKIP_AUTO_UPDATE` | Self-update behavior on container restart, see below |

## Updating

```bash
docker compose pull
docker compose up -d
```

The container also checks GitHub Releases on every restart and can self-update in place - see
`docs/AUTO-UPDATE.md`. Either way, the database volume is untouched by updates.

## Database Management

```bash
# Backup
docker compose exec mariadb sh -c 'exec mysqldump -u root -p"$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE"' > backup.sql

# Restore
docker compose exec -T mariadb sh -c 'exec mysql -u root -p"$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE"' < backup.sql

# Interactive shell
docker compose exec mariadb mysql -u ${DB_USER} -p ${DB_NAME}
```

## Password Reset

If an admin is locked out and `SMTP_*` isn't configured (or you'd rather not depend on
email), reset the account's password directly from the terminal:

```bash
docker compose exec app /app/RustRconServerManager.Backend --reset-password
```

This prompts for the account's email address and a new password, then signs out any
existing sessions for that account. The container must already be running - the command
reuses its existing database connection rather than starting a second one.

## Running behind a reverse proxy

Set `PATHBASE=/panel` (or whatever prefix you want) in `.env` and configure your reverse proxy
(Traefik, nginx, Caddy, ...) to forward that path to the app container's port. The app already
disables its own HTTPS redirect so a proxy in front of it can terminate TLS.

Running several independent instances behind one reverse proxy (e.g. for different Rust server
groups) works the same way you'd run any two isolated Compose stacks - give each its own directory,
`.env`, and `PATHBASE`/port, and point your proxy at each.

## Troubleshooting

**App won't start / "database connection failed"**
```bash
docker compose logs mariadb   # wait for "ready for connections"
docker compose logs app
```

**Port already in use** - change `APP_PORT` in `.env`, then `docker compose up -d`.

**Complete reset (⚠️ deletes all data)**
```bash
docker compose down -v
rm .env
./setup.sh
docker compose up -d
```

## Production Recommendations

1. Use long, randomly generated values for `JWT_KEY`, `RCON_ENCRYPTION_KEY`, `DB_PASSWORD`, and
   `MARIADB_ROOT_PASSWORD` (`setup.sh` already does this for you).
2. Configure `SMTP_*` so password recovery actually works.
3. Put a reverse proxy with HTTPS in front of the app.
4. Back up the `db_data` volume regularly.
5. Rotate `STEAM_API_KEY` / `PROXYCHECK_API_KEY` if they were ever committed to a public repo.
