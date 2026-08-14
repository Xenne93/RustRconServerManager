# RustRconServerManager standalone launcher (Windows)
#
# Starts the bundled, port-isolated MariaDB instance next to this script (first run:
# initializes it and generates random credentials, including a real root password - it
# never runs passwordless), then starts the app configured to use it. The bundled
# MariaDB only listens on 127.0.0.1:3307 - it is not reachable from outside this machine
# and does not touch any other MariaDB/MySQL install you may already have running on
# the default port. It exists purely so this package doesn't require a separately
# installed database server.
#
# Credentials and the database files persist in standalone.env / mariadb-data next to
# this script, so they survive restarts. Back both up before replacing this folder with
# a newer release.
#
# Note: this deliberately does NOT set $ErrorActionPreference = "Stop". On PowerShell
# 5.1, a native exe writing anything at all to stderr (even a harmless warning) gets
# wrapped into a terminating error under "Stop", which previously crashed this script
# on a benign MariaDB warning. Native tool failures are instead detected explicitly via
# $LASTEXITCODE below.

Set-Location -Path $PSScriptRoot

$GitHubRepo = "Xenne93/RustRconServerManager"

# Checks GitHub Releases for a newer version and, if the auto-update setting (Panel
# Settings page, mirrored to app\data\autoupdate.flag) allows it, replaces the contents
# of app\ with the new release. Only app\ is ever touched - mariadb\, mariadb-data\,
# standalone.env, and this script itself are left alone, so a failed or partial update
# can't take the database or its credentials down with it.
function Test-ForUpdate {
    $versionFile = Join-Path $PSScriptRoot "app\.version"
    if (-not (Test-Path $versionFile)) {
        Write-Host "No .version file found - skipping update check (this release predates auto-update support)."
        return
    }

    $currentVersion = (Get-Content $versionFile -Raw).Trim()

    $autoUpdate = "true"
    $flagFile = Join-Path $PSScriptRoot "app\data\autoupdate.flag"
    if (Test-Path $flagFile) {
        $autoUpdate = (Get-Content $flagFile -Raw).Trim()
    }

    Write-Host "Checking for updates (current: $currentVersion)..."

    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$GitHubRepo/releases/latest" -TimeoutSec 15
    } catch {
        Write-Host "Could not reach GitHub - skipping update check."
        return
    }

    $latestVersion = $release.tag_name
    if ([string]::IsNullOrEmpty($latestVersion)) {
        Write-Host "Could not determine the latest version - skipping update check."
        return
    }

    if ($latestVersion -eq $currentVersion) {
        Write-Host "Already on the latest version."
        return
    }

    Write-Host "New version available: $latestVersion (current: $currentVersion)"

    if ($autoUpdate -ne "true") {
        Write-Host "Auto-update is disabled in Panel Settings - not installing automatically."
        return
    }

    $asset = $release.assets | Where-Object { $_.name -like "*Windows-x64*" } | Select-Object -First 1
    if (-not $asset) {
        Write-Host "Could not find a Windows-x64 asset for $latestVersion - skipping update."
        return
    }

    Write-Host "Downloading $latestVersion..."
    $tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("rrsm-update-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    $zipPath = Join-Path $tmpDir "release.zip"

    try {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -TimeoutSec 300
    } catch {
        Write-Host "Download failed - skipping update."
        Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
        return
    }

    $extractDir = Join-Path $tmpDir "extracted"
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $newAppDir = Join-Path $extractDir "app"
    if (-not (Test-Path $newAppDir)) {
        Write-Host "Downloaded release has an unexpected layout - skipping update."
        Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
        return
    }

    Write-Host "Installing update..."
    $appDir = Join-Path $PSScriptRoot "app"
    Get-ChildItem -Path $appDir -File | Where-Object {
        $_.Extension -eq ".dll" -or $_.Name -eq "RustRconServerManager.Backend.exe" -or
        ($_.Extension -eq ".json" -and $_.Name -ne "appsettings.json")
    } | Remove-Item -Force

    Copy-Item -Path (Join-Path $newAppDir "*") -Destination $appDir -Recurse -Force

    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
    Write-Host "Updated to $latestVersion."
}

Test-ForUpdate

$MariaDbDir = Join-Path $PSScriptRoot "mariadb"
$DataDir = Join-Path $PSScriptRoot "mariadb-data"
$MariaDbPort = 3307
$EnvFile = Join-Path $PSScriptRoot "standalone.env"
$DbName = "rustrconservermanager"
$DbUser = "rustrconservermanager"

function New-Secret {
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

function Test-Port {
    param([int]$Port)
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $client.Connect("127.0.0.1", $Port)
        $client.Close()
        return $true
    } catch {
        return $false
    }
}

function Wait-ForPort {
    param([int]$Port)
    for ($i = 0; $i -lt 30; $i++) {
        if (Test-Port -Port $Port) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

if (-not (Test-Path $EnvFile)) {
    Write-Host "First run - generating credentials..."

    $dbPassword = New-Secret
    $rootPassword = New-Secret
    $jwtKey = (New-Secret) + (New-Secret)
    $rconKey = New-Secret

    @"
DB_PASSWORD=$dbPassword
MARIADB_ROOT_PASSWORD=$rootPassword
JWT_KEY=$jwtKey
RCON_ENCRYPTION_KEY=$rconKey
"@ | Set-Content -Path $EnvFile -Encoding UTF8 -ErrorAction Stop
}

if (-not (Test-Path $DataDir)) {
    Write-Host "Initializing the local database..."
    New-Item -ItemType Directory -Force -Path $DataDir -ErrorAction Stop | Out-Null
    & "$MariaDbDir\bin\mariadb-install-db.exe" --datadir="$DataDir"
    if ($LASTEXITCODE -ne 0) {
        throw "mariadb-install-db.exe failed with exit code $LASTEXITCODE"
    }
}

$envValues = @{}
Get-Content $EnvFile | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        $envValues[$matches[1]] = $matches[2]
    }
}
$dbPassword = $envValues["DB_PASSWORD"]
$rootPassword = $envValues["MARIADB_ROOT_PASSWORD"]
$jwtKey = $envValues["JWT_KEY"]
$rconKey = $envValues["RCON_ENCRYPTION_KEY"]

# Real test runs on Windows kept getting "Access denied" for root no matter which
# combination of --password at install time, passwordless bootstrap, or SET PASSWORD
# was tried - MariaDB's exact host-matching behavior for a 127.0.0.1 connection turned
# out to be inconsistent/unpredictable across attempts. So instead of guessing, every
# run starts MariaDB with --skip-grant-tables (the standard "reset a password you can't
# get in with" technique) and fixes root's password from inside that session (see below
# for exactly how). This is idempotent, so it runs on every start, not just the first
# one, which also means a partially-failed previous attempt can't leave things in a
# broken state.
Write-Host "Starting bundled MariaDB on 127.0.0.1:$MariaDbPort..."
$mariadbProcess = Start-Process -FilePath "$MariaDbDir\bin\mariadbd.exe" -ArgumentList @(
    "--datadir=$DataDir",
    "--port=$MariaDbPort",
    "--bind-address=127.0.0.1",
    "--skip-grant-tables"
) -PassThru -WindowStyle Hidden

try {
    Write-Host "Waiting for the database to be ready..."
    if (-not (Wait-ForPort -Port $MariaDbPort)) {
        throw "MariaDB did not become ready in time. Check the output above for errors."
    }

    Write-Host "Securing the root account..."
    # Neither a direct UPDATE on mysql.user (the classic technique - it's a view on
    # modern MariaDB, "Column 'authentication_string' is not updatable") nor a
    # self-targeting SET PASSWORD (it does its own account lookup keyed on how this
    # connection got resolved, which turned out not to match any real row either -
    # "ERROR 1133: Can't find any matching row in the user table") worked on a real test.
    # mysql.global_priv is what mysql.user is really a view over as of MariaDB 10.4 - a
    # real base table, so a plain UPDATE/JSON_SET on it isn't blocked by either problem.
    # WHERE User='root' with no Host qualifier fixes every root row regardless of host
    # pattern, so there's nothing left to guess.
    $secureSql = @"
UPDATE mysql.global_priv SET Priv = JSON_SET(Priv, '$.plugin', 'mysql_native_password', '$.authentication_string', PASSWORD('$rootPassword')) WHERE User = 'root';
FLUSH PRIVILEGES;
"@
    $secureSql | & "$MariaDbDir\bin\mariadb.exe" -h 127.0.0.1 -P $MariaDbPort -u root
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to secure the root account (exit code $LASTEXITCODE)"
    }

    Write-Host "Creating application database and user..."
    # A fresh connection using the password just set above, rather than continuing on
    # the same --skip-grant-tables session - that session's own claimed identity didn't
    # match a real grant table row (see above), so it's not certain it would still carry
    # real privileges once FLUSH PRIVILEGES re-enables enforcement. A new connection
    # authenticating properly against root's now-corrected row doesn't have that
    # ambiguity. Windows MariaDB has been observed reporting a 127.0.0.1 connection's
    # origin as both '127.0.0.1' and 'localhost' in different situations, so the app
    # user is granted under both host patterns defensively.
    $appSql = @"
CREATE DATABASE IF NOT EXISTS $DbName;
CREATE USER IF NOT EXISTS '$DbUser'@'127.0.0.1' IDENTIFIED BY '$dbPassword';
CREATE USER IF NOT EXISTS '$DbUser'@'localhost' IDENTIFIED BY '$dbPassword';
GRANT ALL PRIVILEGES ON $DbName.* TO '$DbUser'@'127.0.0.1';
GRANT ALL PRIVILEGES ON $DbName.* TO '$DbUser'@'localhost';
FLUSH PRIVILEGES;
"@
    $appSql | & "$MariaDbDir\bin\mariadb.exe" -h 127.0.0.1 -P $MariaDbPort -u root --password="$rootPassword"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create the application database/user (exit code $LASTEXITCODE)"
    }

    $env:ConnectionStrings__DefaultConnection = "Server=127.0.0.1;Port=$MariaDbPort;Database=$DbName;User=$DbUser;Password=$dbPassword;"
    $env:Jwt__Key = $jwtKey
    $env:Jwt__Issuer = "RustRconServerManager"
    $env:Jwt__Audience = "RustRconServerManager"
    $env:RconEncryption__Key = $rconKey
    if (-not $env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS = "http://+:5000" }
    $env:ASPNETCORE_ENVIRONMENT = "Production"

    Write-Host ""
    Write-Host "Starting RustRconServerManager..."
    Write-Host "Open http://localhost:5000 in your browser once it's up."
    Write-Host ""

    # Run from inside app/ - the app looks up some files (wwwroot, appsettings.json)
    # relative to the current directory, not just next to the exe, so launching it from
    # the package root (where this script lives) breaks static file serving.
    Set-Location -Path (Join-Path $PSScriptRoot "app")
    & ".\RustRconServerManager.Backend.exe"
}
finally {
    Write-Host ""
    Write-Host "Shutting down bundled MariaDB..."
    & "$MariaDbDir\bin\mariadb-admin.exe" -h 127.0.0.1 -P $MariaDbPort -u root --password="$rootPassword" shutdown
    if ($mariadbProcess -and -not $mariadbProcess.HasExited) {
        Start-Sleep -Seconds 2
        if (-not $mariadbProcess.HasExited) {
            Stop-Process -Id $mariadbProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
