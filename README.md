# Rust RCON Server Manager

A self-hosted web admin panel for Rust dedicated game servers - RCON console, player
management, bans, scheduling/automation, Discord webhooks, and server protection
(VPN/VAC/whitelist checks). Runs entirely on your own infrastructure, no external
service required.

> **Work in progress**: this panel is under active development. Features can be added,
> changed, or removed between releases.

## Features

- **Live console & chat** over RCON, via SignalR, for one or many Rust servers from one panel
- **Player management**: player inspect, notes, ban history, cross-server bans within your own fleet
- **Server Protection**: VPN/proxy detection, Steam account age & VAC-ban thresholds, whitelist-only mode, private-profile blocking
- **Scheduler & triggers**: automated/scheduled RCON commands and event-driven automation
- **Discord webhooks** for server events
- **Moderator accounts & permissions**: fine-grained page/server access per moderator
- **Live map** via a companion Oxide/uMod plugin (distributed separately, see below) - the panel pulls map, sleeping bag, and tool cupboard data from it over RCON on demand, so the panel never needs to be reachable from your game server
- **Dark / light theme**, remembered per browser
- **Developer mode**: assign a fake VAC-ban profile to a SteamID to test your Server Protection rules without needing a real banned account
- **Self-updating**: both the Docker container and the standalone launcher can check GitHub Releases and update themselves on startup (see `docs/AUTO-UPDATE.md`)

## Getting Started

There are two ways to run the panel - pick whichever fits your setup:

| | Docker | Standalone |
|---|---|---|
| Best for | Servers that already run Docker | Anywhere you'd rather not install Docker |
| Requirements | Docker + Docker Compose | Nothing - Windows/Linux binary, bundles its own database |
| Database | Bundled MariaDB container | Bundled MariaDB, isolated to `127.0.0.1:3307` |

### Option 1: Docker

No need to clone the repo - the installer downloads `docker-compose.yml`, generates a
`.env` with random credentials, pulls the image, and starts the stack:

```bash
mkdir rust-rcon-server-manager && cd rust-rcon-server-manager
curl -fsSL https://raw.githubusercontent.com/Xenne93/RustRconServerManager/main/install.sh | bash
```

Or, if you'd rather clone the repo first:

```bash
git clone https://github.com/Xenne93/RustRconServerManager.git
cd RustRconServerManager
./setup.sh          # generates .env with random credentials
docker compose up -d
```

Open `http://localhost:5000` and create your admin account - the first person to open
the panel gets to do this, so don't expose the port publicly until you've completed it.

See `docs/DOCKER_DEPLOYMENT.md` for the full guide (environment variables, backups,
reverse proxy, production recommendations).

**Updating:**

```bash
docker compose pull
docker compose up -d
```

The container also checks GitHub Releases on every restart and can update itself in
place - see `docs/AUTO-UPDATE.md`, or toggle it from **Panel Settings > Auto-Update**
once it's running.

### Option 2: Standalone (no Docker)

Download the latest release for your OS from the
[Releases page](https://github.com/Xenne93/RustRconServerManager/releases/latest):

- **Windows**: `RustRconServerManager-Windows-x64-v*.zip` - extract, then double-click `start.bat`
- **Linux**: `RustRconServerManager-Linux-x64-v*.tar.gz` - extract, then run `./start.sh`

Both are fully self-contained: they bundle their own MariaDB (listening only on
`127.0.0.1:3307`, isolated from anything else you may have running) and don't require
.NET or Docker to be installed. The first run generates random database/JWT/RCON
credentials and initializes the bundled database; every run after that reuses them.

Open `http://localhost:5000` and create your admin account, same as above.

See `standalone/README.md` (included as `README.md` in the downloaded archive) for
details on upgrading, using your own database instead of the bundled one, and
troubleshooting a failed first run.

**Updating:** the launcher checks GitHub for a newer release on every start and
updates itself in place automatically (toggle this from **Panel Settings >
Auto-Update**), or download and extract a newer release yourself - just copy your
`mariadb-data/` and `standalone.env` out first and put them back afterwards so you
don't lose your data.

## Connecting a Rust server

1. Install the companion Oxide plugin (`oxide-mods/RustRconServerManager.cs` in this
   repo) on your Rust server. No configuration needed - the plugin has none.
2. In the panel, go to Manage Servers → add your server, then run the "Initialize Panel
   Mod" wizard - it just confirms the mod responds over RCON.
3. Use the "Refresh Map" button on the Dashboard to pull the current map (and sleeping
   bags/tool cupboards) from the server. The panel requests this data over the same
   RCON connection it uses for everything else - the plugin never calls out to the
   panel, so your panel doesn't need to be exposed to the internet (or even reachable
   from the game server at all) for this to work.

## Architecture

- `RustRconServerManager.Backend` - ASP.NET Core 8 API (EF Core + MariaDB, SignalR, JWT auth), also serves the frontend
- `RustRconServerManager.Frontend` - Blazor WebAssembly SPA
- `RustRconServerManager.Shared` / `.Shared.Configuration` - shared DTOs/models/config
- `Xenne.RCON` - the RCON client library used to talk to Rust servers

The companion Oxide plugin (live map/sleeping bags/tool cupboards) lives in
`oxide-mods/` in this repo. The mobile companion app is distributed separately.

## Documentation

- `docs/DOCKER_DEPLOYMENT.md` - environment variables, backups, reverse proxy, production notes
- `docs/AUTO-UPDATE.md` - how self-updating works for both Docker and standalone
- `standalone/README.md` - standalone-specific details (ships as `README.md` inside the release download)

## License

This project is source-available under the [Business Source License 1.1](LICENSE.md) - not
an OSI-approved open source license. It's free to self-host and use, including
commercially, but you may not offer it as a competing hosted/managed service without a
separate commercial agreement, and any deployment must keep visible attribution back to
this project. It converts to Apache 2.0 on the date specified in `LICENSE.md`.
