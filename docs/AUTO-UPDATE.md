# RustRconServerManager Auto-Update System

## Overview

RustRconServerManager now includes an automatic update system that ensures your instances are always running the latest version from GitHub releases.

## How It Works

### 1. **Build-Time Version Fetching**
When you deploy or update an instance, the Docker build process:
- Fetches the **latest** release from GitHub using the API endpoint `/releases/latest`
- Adds a cache-busting timestamp to prevent cached responses
- Downloads the Linux-x64 release asset
- Records the version number in `.build_version` file

### 2. **Container Restart Auto-Update**
Every time a container restarts (including system reboots, crashes, or manual restarts), it:
- Checks GitHub for the latest release version
- Compares it with the currently installed version
- Automatically downloads and installs updates if a newer version is available
- Preserves your data, certificates, and configuration
- Starts the application with the new version

### 3. **No Cache Build Process**
Rebuilding with `--no-cache` and `--pull` (or `docker compose pull` when using the published image) ensures:
- Docker doesn't use cached layers that might contain old releases
- Base images are always pulled fresh
- The GitHub API is queried for the absolute latest release

This works the same way for the standalone (self-contained Windows/Linux, no Docker)
release - `start.bat`/`start.ps1`/`start.sh` check GitHub on every launch and update the
`app/` folder in place before starting.

## Configuration

### Panel Settings (recommended)

Go to **Panel Settings > Auto-Update** in the app itself to turn automatic updates on or
off. This is the source of truth once the app has run at least once - it writes the
setting to a flag file (`data/autoupdate.flag`, next to the app) that the update-check
script reads before the app process even starts. Takes effect on the *next* start/restart,
not immediately (there's nothing running yet for it to update at the moment you flip the
toggle).

### Environment Variables (Docker only, initial default)

Before the app has run once (so before there's a Panel Settings row to read), or as a
fallback if the flag file is ever missing, Docker falls back to these environment
variables in your `.env` file:

```bash
# Enable/disable automatic updates on container restart (default: true)
AUTO_UPDATE=true

# Skip update check entirely (default: false)
SKIP_AUTO_UPDATE=false

# Optional: GitHub token, only needed to raise the unauthenticated GitHub API rate limit
GITHUB_TOKEN=
```

### Disable Auto-Update for Specific Instance

To disable auto-updates for a specific instance, add to its `.env` file:

```bash
AUTO_UPDATE=false
```

The container will still check for updates and notify you, but won't automatically install them.

### Skip Update Check Entirely

If you want to completely skip the update check (faster startup):

```bash
SKIP_AUTO_UPDATE=true
```

## Usage

### Manual Update to Latest

```bash
docker compose pull
docker compose up -d
```

### Automatic Updates on Restart

Simply restart your container:

```bash
docker compose restart
```

The container will automatically:
1. Check for new releases on startup
2. Download and install if available
3. Start the application

## Version Information

### Check Current Version

View container logs to see the current version:

```bash
docker compose logs app | grep "Running version"
```

### Version Files

- `/app/.version` - Currently running version
- `/app/.build_version` - Version the container was built with
- `/app/.update_available` - Marker file when update is pending (for manual update mode)

## How Updates Are Applied

When an update is detected:

1. **Backup**: Current executable is backed up to `/app/backup-{version}/`
2. **Download**: New release is downloaded to temporary directory
3. **Extract**: Release is extracted
4. **Preserve**: Data directory, certificates, and `appsettings.json` are preserved
5. **Replace**: Old binaries and libraries are replaced with new ones
6. **Start**: Application starts with the new version

## Troubleshooting

### Auto-Update Failing on Restart

**Problem**: Container starts but doesn't update

**Solutions**:
1. Check container logs: `docker compose logs app`
2. Ensure `AUTO_UPDATE=true` in `.env`
3. Verify network connectivity from container to `api.github.com`
4. If you're hitting GitHub's unauthenticated rate limit, set `GITHUB_TOKEN` in `.env`

### Version Shows as "unknown"

**Problem**: Version file is missing

**Solutions**:
1. Rebuild container: `docker compose build --no-cache`
2. Check `/app/.version` and `/app/.build_version` exist
3. Redeploy instance if necessary

### Update Downloaded But Not Applied

**Problem**: Update check runs but application doesn't restart

**Cause**: When `AUTO_UPDATE=false`, updates are detected but not installed

**Solutions**:
1. Set `AUTO_UPDATE=true` in `.env`
2. Or manually update: `docker compose pull && docker compose up -d`

## Best Practices

### Production Deployments

1. **Test updates on a separate instance first** if you can - e.g. a second Compose stack with a
   copy of your `.env` and `AUTO_UPDATE=true`, before rolling out to production with
   `AUTO_UPDATE=false` and updating manually once you've verified it.

2. **Monitor Logs**: Check logs after updates
   ```bash
   docker compose logs -f app
   ```

3. **Backup Data**: Data is preserved, but good practice to backup
   ```bash
   docker compose exec app tar -czf /app/data/backup-$(date +%Y%m%d).tar.gz /app/data
   ```

### Development Instances

1. **Enable Auto-Update**: Always run latest features
   ```bash
   AUTO_UPDATE=true
   ```

2. **Fast Iteration**: For frequent deploys, you might want to disable
   ```bash
   SKIP_AUTO_UPDATE=true
   ```

## Security Considerations

1. **GitHub Token**: Keep your token secure, use repository-scoped tokens
2. **Network**: Update check requires internet access to GitHub API
3. **Verification**: Updates are fetched from official GitHub releases only
4. **Rollback**: Previous version backed up in `/app/backup-{version}/`

## Manual Rollback

If you need to rollback to a previous version:

```bash
# Enter container
docker compose exec app bash

# Check available backups
ls -la /app/backup-*/

# Restore previous version
cd /app
cp backup-v1.2.3/RustRconServerManager.Backend .
chmod +x RustRconServerManager.Backend

# Update version file
echo "v1.2.3" > .version

# Restart container
exit
docker compose restart
```

## Example Scenarios

### Scenario 1: New Release Available

```
Container starts → Check GitHub → New version found → Download → Install → Start app
```

**Output**:
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
⚠️  NEW VERSION AVAILABLE: v1.3.0
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Current: v1.2.0
Latest:  v1.3.0

Auto-update is enabled. Downloading new version...
✓ Successfully updated to version v1.3.0
Starting application...
```

### Scenario 2: Already Latest Version

```
Container starts → Check GitHub → Already latest → Start app
```

**Output**:
```
Checking for updates...
Current version: v1.3.0
Latest version: v1.3.0
✓ Already running the latest version
Running version: v1.3.0
Starting RustRconServerManager Application
```

### Scenario 3: Auto-Update Disabled

```
Container starts → Check GitHub → New version found → Notify only → Start old version
```

**Output**:
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
⚠️  NEW VERSION AVAILABLE: v1.3.0
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Auto-update is disabled. To update manually:
  docker compose pull && docker compose up -d

Starting application with current version...
```

## FAQ

**Q: Will my data be deleted during updates?**
A: No, the `/app/data` directory is preserved. It's also mounted as a Docker volume for extra safety.

**Q: What happens if the update fails?**
A: The container will start with the previous version. Check logs for errors.

**Q: Can I control which version to install?**
A: Currently, auto-update always fetches the latest release. For specific versions, disable auto-update and manually install.

**Q: Does this work offline?**
A: No, the update check requires internet access to GitHub API. Set `SKIP_AUTO_UPDATE=true` for offline environments.

**Q: How often does it check for updates?**
A: Only on container startup/restart, not while running.

**Q: Will configuration files be overwritten?**
A: No, `appsettings.json` and certificates are preserved during updates.
