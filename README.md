# RustRconServerManager

A self-hosted web admin panel for Rust dedicated game servers - RCON console, player management,
bans, scheduling/automation, Discord webhooks, and server protection (VPN/VAC/whitelist checks),
all from a single Docker Compose stack that you run yourself.

## Features

- **Live console & chat** over RCON, via SignalR, for one or many Rust servers from one panel
- **Player management**: player inspect, notes, ban history, cross-server bans within your own fleet
- **Server Protection**: VPN/proxy detection, Steam account age & VAC-ban thresholds, whitelist-only mode, private-profile blocking
- **Scheduler & triggers**: automated/scheduled RCON commands and event-driven automation
- **Discord webhooks** for server events
- **Moderator accounts & permissions**: fine-grained page/server access per moderator
- **Live map** via a companion Oxide/uMod plugin (distributed separately, see below) - the panel pulls map, sleeping bag, and tool cupboard data from it over RCON on demand, so the panel never needs to be reachable from your game server
- **Self-updating**: containers can check GitHub Releases and update themselves on restart (see `docs/AUTO-UPDATE.md`)

## Architecture

- `RustRconServerManager.Backend` - ASP.NET Core 8 API (EF Core + MariaDB, SignalR, JWT auth), also serves the frontend
- `RustRconServerManager.Frontend` - Blazor WebAssembly SPA
- `RustRconServerManager.Shared` / `.Shared.Configuration` - shared DTOs/models/config
- `Xenne.RCON` - the RCON client library used to talk to Rust servers

The companion Oxide plugin (live map/sleeping bags/tool cupboards) and the mobile companion
app are distributed separately from this repo, not bundled here.

## Quick Start (Docker)

No need to clone the full repo - the installer downloads `docker-compose.yml`, generates a `.env`
with random credentials, pulls the images, and starts the stack:

```bash
mkdir rustrconservermanager && cd rustrconservermanager
curl -fsSL https://raw.githubusercontent.com/Xenne93/RustRconServerManager/main/install.sh | bash
```

Or, if you'd rather clone the repo first:

```bash
git clone https://github.com/Xenne93/RustRconServerManager.git
cd RustRconServerManager
./setup.sh
docker compose up -d
```

Open `http://localhost:5000` and create your admin account - the first person to open the panel
gets to do this, so don't expose the port publicly until you've completed it.
See `docs/DOCKER_DEPLOYMENT.md` for the full guide (env vars, backups, reverse proxy, production
notes) and `docs/AUTO-UPDATE.md` for how self-updating works.

### Updating / pulling the latest image directly

If you already have a `docker-compose.yml`/`.env` set up (either install path above), re-run the
installer to pull and redeploy the latest published image, or do it manually:

```bash
docker pull ghcr.io/xenne93/rustrconservermanager:latest
docker compose up -d
```

## Connecting a Rust server

1. Install the companion Oxide plugin on your Rust server (distributed separately from this
   repo). No configuration needed - the plugin has none.
2. In the panel, go to Manage Servers → add your server, then run the "Initialize Panel Mod" wizard -
   it just confirms the mod responds over RCON.
3. Use the "Refresh Map" button on the Dashboard to pull the current map (and sleeping bags/tool
   cupboards) from the server. The panel requests this data over the same RCON connection it uses
   for everything else - the plugin never calls out to the panel, so your panel doesn't need to be
   exposed to the internet (or even reachable from the game server at all) for this to work.

## License

This project is licensed under the [Business Source License 1.1](LICENSE.md): free to self-host
and use, including commercially, but you may not offer it as a competing hosted/managed service
without a separate commercial agreement, and any deployment must keep visible attribution back to
this project. It converts to Apache 2.0 on the date specified in `LICENSE.md`.
