# RustRconServerManager - standalone build

This package is self-contained: it bundles its own MariaDB database, so you don't
need Docker or a separately installed database server.

## Running it

**Windows:** double-click `start.bat` (or run `start.ps1` in PowerShell).
**Linux:** run `./start.sh`

The first time you run it, it will:
- Initialize a local MariaDB data directory (`mariadb-data/`)
- Generate random database/JWT/RCON credentials and store them in `standalone.env`
- Create the application's database and user

On every run after that, it reuses the same data and credentials.

Once it's running, open **http://localhost:5000** in your browser and create your
admin account - the first person to open the panel gets to do this, so don't expose
the port publicly until you've completed it.

## About the bundled database

The bundled MariaDB instance listens only on `127.0.0.1:3307` (loopback, custom port).
It is not reachable from other machines and does not conflict with any MariaDB/MySQL
server you might already have running locally on the default port (3306). It is
started and stopped automatically together with the app - closing the app (Ctrl+C, or
closing the console window) shuts the database down too.

## Auto-update

Every time you start it, the launcher checks GitHub for a newer release and installs it
automatically before starting the app - only the `app/` folder is touched, so this can't
affect `mariadb-data/` or `standalone.env`. You can turn this off from the app itself, in
**Panel Settings > Auto-Update**. Turning it off doesn't stop the check, just the
automatic install - you'll see a message telling you a new version is available so you
can update manually (see "Upgrading to a newer release" below) whenever you're ready.

## Upgrading to a newer release

If you'd rather update by hand (or auto-update is off), your data lives in
`mariadb-data/` and your generated credentials live in `standalone.env`, both next to
this script. Before replacing this folder with a newer release, copy those two out and
put them back afterwards so you don't lose your data.

## If the first run fails partway through

`standalone.env` is written right at the start of the first run, before the database is
actually initialized. If something fails partway through that first run (a crash, closing
the window, an error), delete both `standalone.env` and `mariadb-data/` before trying
again - otherwise the script will think setup already finished and skip straight to
using credentials that were never actually applied to a working database.

## Advanced: using your own database instead

If you'd rather point this at a MariaDB/MySQL server you already run, set the
`ConnectionStrings__DefaultConnection` environment variable yourself before starting
`app/RustRconServerManager.Backend` (or `.exe` on Windows) directly instead of using
the start script - see `.env.example` in the source repository for the other
environment variables the app expects (JWT key, RCON encryption key, etc.).
