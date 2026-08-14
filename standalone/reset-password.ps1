# Resets an admin account's password from the terminal - for when you're locked out and
# either don't have SMTP configured for the "forgot password" email flow, or just prefer
# not to depend on it. Requires the instance to already be running (started via
# start.ps1/start.bat in another window) since it connects to that same running bundled
# MariaDB rather than starting a second one.

Set-Location -Path $PSScriptRoot

$EnvFile = Join-Path $PSScriptRoot "standalone.env"
if (-not (Test-Path $EnvFile)) {
    Write-Host "standalone.env not found - has this instance been started at least once (start.bat)?"
    exit 1
}

$envValues = @{}
Get-Content $EnvFile | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        $envValues[$matches[1]] = $matches[2]
    }
}

$MariaDbPort = 3307
$DbName = "rustrconservermanager"
$DbUser = "rustrconservermanager"

$env:ConnectionStrings__DefaultConnection = "Server=127.0.0.1;Port=$MariaDbPort;Database=$DbName;User=$DbUser;Password=$($envValues['DB_PASSWORD']);"
$env:Jwt__Key = $envValues["JWT_KEY"]
$env:Jwt__Issuer = "RustRconServerManager"
$env:Jwt__Audience = "RustRconServerManager"
$env:RconEncryption__Key = $envValues["RCON_ENCRYPTION_KEY"]
$env:ASPNETCORE_ENVIRONMENT = "Production"

# Run from inside app/ - same reason as start.ps1: some file lookups are relative to the
# current directory.
Set-Location -Path (Join-Path $PSScriptRoot "app")
& ".\RustRconServerManager.Backend.exe" --reset-password
