using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Services;

namespace RustRconServerManager.Backend.Controllers
{
    /// <summary>
    /// Serves publicly-embeddable images (e.g. the map image used as an &lt;img&gt; source in the
    /// frontend). Map/sleeping-bag/tool-cupboard data itself is pulled from the Rust server over
    /// RCON by RrsmModDataService - see RconController.ServerManagement.cs and
    /// DashboardController.ServerInformation.cs - rather than pushed here via HTTP, so this
    /// controller no longer needs to accept any inbound data from game servers.
    /// </summary>
    [ApiController]
    [Route("api/external")]
    public class ExternalApiController : ControllerBase
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MapStorageService _mapStorageService;

        public ExternalApiController(
            IServiceScopeFactory scopeFactory,
            MapStorageService mapStorageService)
        {
            _scopeFactory = scopeFactory;
            _mapStorageService = mapStorageService;
        }

        /// <summary>
        /// Serves a map image by server ID.
        /// Primary: serves from disk (fast).
        /// Fallback: serves from database and restores to disk.
        /// </summary>
        [HttpGet("map/{serverId}")]
        public async Task<IActionResult> GetMapImage(int serverId)
        {
            // Primary: serve from disk (fastest)
            var filePath = _mapStorageService.GetMapFilePath(serverId);
            if (System.IO.File.Exists(filePath))
            {
                var imageBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                return File(imageBytes, "image/jpeg");
            }

            // Fallback: serve from database and restore to disk
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var mapData = await dbContext.MapData.FirstOrDefaultAsync(m => m.ServerId == serverId);
            if (mapData == null)
            {
                return NotFound("Map image not found.");
            }

            if (mapData.ImageData != null && mapData.ImageData.Length > 0)
            {
                // Restore to disk for next time
                await _mapStorageService.SaveMapToDisk(serverId, mapData.ImageData);
                return File(mapData.ImageData, "image/jpeg");
            }

            return NotFound("Map image not found.");
        }
    }
}
