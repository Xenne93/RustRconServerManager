using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Helpers;

namespace RustRconServerManager.Backend.Services
{
    /// <summary>
    /// Background service that restores map and server images from database to disk on startup.
    /// This ensures images are available after updates or disaster recovery.
    /// </summary>
    public class MapStorageService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<MapStorageService> _logger;

        public MapStorageService(
            IServiceScopeFactory scopeFactory,
            IWebHostEnvironment environment,
            ILogger<MapStorageService> logger)
        {
            _scopeFactory = scopeFactory;
            _environment = environment;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MapStorageService: Checking image files on startup...");

            try
            {
                await RestoreMapsFromDatabase(cancellationToken);
                await RestoreServerImagesFromDatabase(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MapStorageService: Error restoring images from database");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Restores all map images from database to disk if they don't exist.
        /// </summary>
        private async Task RestoreMapsFromDatabase(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Get all maps that have image data in the database
            var mapsWithData = await dbContext.MapData
                .Where(m => m.ImageData != null && m.ImageData.Length > 0)
                .Select(m => new { m.ServerId, m.ImageData })
                .ToListAsync(cancellationToken);

            if (mapsWithData.Count == 0)
            {
                _logger.LogInformation("MapStorageService: No maps found in database");
                return;
            }

            // Ensure maps directory exists
            var mapsDirectory = Path.Combine(_environment.ContentRootPath, "Storage", "maps");
            Directory.CreateDirectory(mapsDirectory);

            int restoredCount = 0;
            int skippedCount = 0;

            foreach (var map in mapsWithData)
            {
                var filePath = GetMapFilePath(map.ServerId);

                if (File.Exists(filePath))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    await File.WriteAllBytesAsync(filePath, map.ImageData!, cancellationToken);
                    restoredCount++;
                    _logger.LogInformation("MapStorageService: Restored map for server {ServerId} to {Path}",
                        map.ServerId, filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MapStorageService: Failed to restore map for server {ServerId}", map.ServerId);
                }
            }

            _logger.LogInformation("MapStorageService: Completed. Restored: {Restored}, Already existed: {Skipped}",
                restoredCount, skippedCount);
        }

        /// <summary>
        /// Gets the file path for a server's map image.
        /// </summary>
        public string GetMapFilePath(int serverId)
        {
            var mapsDirectory = Path.Combine(_environment.ContentRootPath, "Storage", "maps");
            return Path.Combine(mapsDirectory, $"server_{serverId}.jpg");
        }

        /// <summary>
        /// Saves a map image to disk.
        /// </summary>
        public async Task SaveMapToDisk(int serverId, byte[] imageData)
        {
            var mapsDirectory = Path.Combine(_environment.ContentRootPath, "Storage", "maps");
            Directory.CreateDirectory(mapsDirectory);

            var filePath = GetMapFilePath(serverId);
            await File.WriteAllBytesAsync(filePath, imageData);

            _logger.LogInformation("MapStorageService: Saved map for server {ServerId} to disk ({Size} bytes)",
                serverId, imageData.Length);
        }

        /// <summary>
        /// Restores server header and logo images from database to disk on startup.
        /// </summary>
        private async Task RestoreServerImagesFromDatabase(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Get all servers with their system profile hash for the public URL path
                // Use raw SQL to avoid issues if the columns don't exist yet (migration not applied)
                var serversWithImages = await dbContext.RconServers
                    .Include(s => s.SystemProfile)
                    .Where(s => (s.ServerHeaderImageData != null && s.ServerHeaderImageData.Length > 0) ||
                               (s.ServerLogoImageData != null && s.ServerLogoImageData.Length > 0))
                    .Select(s => new
                    {
                        s.Id,
                        s.SystemProfile.Hash,
                        s.ServerHeaderImageData,
                        s.ServerLogoImageData
                    })
                    .ToListAsync(cancellationToken);

                if (serversWithImages.Count == 0)
                {
                    _logger.LogInformation("MapStorageService: No server images found in database");
                    return;
                }

                int restoredCount = 0;

                foreach (var server in serversWithImages)
                {
                    // Restore header image
                    if (server.ServerHeaderImageData != null && server.ServerHeaderImageData.Length > 0)
                    {
                        var headerPath = GetServerImageFilePath(server.Hash, server.Id, "serverlist_image.jpg");
                        if (!File.Exists(headerPath))
                        {
                            await SaveServerImageToDisk(server.Hash, server.Id, "serverlist_image.jpg", server.ServerHeaderImageData);
                            restoredCount++;
                        }
                    }

                    // Restore logo image
                    if (server.ServerLogoImageData != null && server.ServerLogoImageData.Length > 0)
                    {
                        var logoPath = GetServerImageFilePath(server.Hash, server.Id, "logoimage.jpg");
                        if (!File.Exists(logoPath))
                        {
                            await SaveServerImageToDisk(server.Hash, server.Id, "logoimage.jpg", server.ServerLogoImageData);
                            restoredCount++;
                        }
                    }
                }

                _logger.LogInformation("MapStorageService: Restored {Count} server images from database", restoredCount);
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Message.Contains("Unknown column"))
            {
                // Migration not yet applied, skip restoring server images
                _logger.LogWarning("MapStorageService: Server image columns not yet in database (migration pending), skipping restore");
            }
        }

        /// <summary>
        /// Gets the file path for a server's custom image.
        /// Path format: Storage/public/{instanceHash}/{serverId}/{filename}
        /// Resolves the path and verifies it is still contained within the public images
        /// base directory, since instanceHash is attacker-controlled on the anonymous
        /// image-serving endpoint and naive character blacklisting isn't safe here
        /// (legitimate hashes are Base64 and may contain '/' and '+').
        /// </summary>
        public string GetServerImageFilePath(string instanceHash, int serverId, string filename)
        {
            var baseDirectory = Path.Combine(_environment.ContentRootPath, "Storage", "public");
            var directory = Path.Combine(baseDirectory, instanceHash, serverId.ToString());
            var filePath = Path.Combine(directory, filename);

            var fullBase = Path.GetFullPath(baseDirectory) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(filePath);

            if (!fullPath.StartsWith(fullBase, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid instanceHash, serverId, or filename: resolved path escapes the public images directory.");
            }

            return fullPath;
        }

        /// <summary>
        /// Saves a server image to disk.
        /// </summary>
        public async Task SaveServerImageToDisk(string instanceHash, int serverId, string filename, byte[] imageData)
        {
            var filePath = GetServerImageFilePath(instanceHash, serverId, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await File.WriteAllBytesAsync(filePath, imageData);

            _logger.LogInformation("MapStorageService: Saved server image {Filename} for server {ServerId} to disk ({Size} bytes)",
                filename.Replace("\r", "").Replace("\n", ""), serverId, imageData.Length);
        }

        /// <summary>
        /// Deletes a server image from disk.
        /// </summary>
        public void DeleteServerImageFromDisk(string instanceHash, int serverId, string filename)
        {
            var filePath = GetServerImageFilePath(instanceHash, serverId, filename);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("MapStorageService: Deleted server image {Filename} for server {ServerId}",
                    filename.Replace("\r", "").Replace("\n", ""), serverId);
            }
        }
    }
}
