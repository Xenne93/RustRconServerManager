using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.Account;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Shared.Dashboard;

namespace RustRconServerManager.Backend.Controllers
{

    public partial class DashboardController
    {
        
        
        // Sends base information about the server via the Dashboard_ServerBaseInformationDTO to the
        // dashboard frontend on Get request. Used by 'Dashboard' page.
        // The server id comes from the users selected server id value.
        [HttpGet("GetServerBaseInformation")]
        public async Task<IActionResult> GetServerBaseInformation()
        {
            var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            int serverid = User.GetUser(dbContext).Result.SelectedServerId ?? 0;

            if (serverid == null)
            {
                return NotFound("No selected server id present in the users account.");
            }

            if (await User.HasServerAccess(dbContext, serverid))
            {
                SystemProfile userSystemProfile = await User.GetUserSystemProfile(dbContext);
               
                RconServer server = await dbContext.RconServers
                    .SingleOrDefaultAsync(x => x.Id == serverid && x.SystemProfileId == userSystemProfile.Id);

                if (server == null)
                {
                    return NotFound("Server not found.");
                }
               
                Dashboard_ServerBaseInformationDTO serverBaseInformation = new Dashboard_ServerBaseInformationDTO();
                serverBaseInformation.ServerName = server.Name;
                serverBaseInformation.GamePort = server.GamePort;
                serverBaseInformation.QueryPort = server.QueryPort;
                serverBaseInformation.RconPort = server.RconPort;
                serverBaseInformation.ServerId = server.Id;
                serverBaseInformation.ServerAddress = !string.IsNullOrEmpty(server.EncryptedHost)
                    ? _rconPasswordsCryptoService.Decrypt(server.EncryptedHost)
                    : string.Empty;
                serverBaseInformation.LatestFpsCount = server.LatestFpsCount;
                serverBaseInformation.LatestPlayerCount = server.LatestPlayerCount;
                serverBaseInformation.ServerHostname = server.ServerHostname;
                serverBaseInformation.LatestEntityCount = server.LatestEntityCount;
                serverBaseInformation.LatestUptime = server.LatestUptime;
                serverBaseInformation.LatestMemory = server.LatestMemoryUsage;

                return Ok(serverBaseInformation);

            }
            else
            {
                return Unauthorized("You are not authorized to see this server information, or the server does not exist.");
            }

        }

        /// <summary>
        /// Gets the map data for the currently selected server.
        /// Returns map image, size, seed, and whether the panel mod is initialized.
        /// </summary>
        [HttpGet("GetMapData")]
        public async Task<IActionResult> GetMapData()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
            {
                return NotFound("No selected server.");
            }

            if (!await User.HasServerAccess(dbContext, serverId))
            {
                return Unauthorized("You do not have access to this server.");
            }

            var server = await dbContext.RconServers.FindAsync(serverId);
            if (server == null)
            {
                return NotFound("Server not found.");
            }

            var mapData = await dbContext.MapData.FirstOrDefaultAsync(m => m.ServerId == serverId);

            // Get online players with their positions
            var onlinePlayers = await dbContext.SteamPlayers
                .Where(p => p.ServerId == serverId && p.IsOnline && p.LatestPositionX != null && p.LatestPositionZ != null)
                .Select(p => new MapPlayerDTO
                {
                    SteamId = p.SteamId,
                    Name = p.Name ?? "Unknown",
                    X = p.LatestPositionX ?? 0,
                    Y = p.LatestPositionY ?? 0,
                    Z = p.LatestPositionZ ?? 0
                })
                .ToListAsync();

            // Get sleeping bags/beds for this server
            var sleepingBags = await dbContext.SleepingBags
                .Where(s => s.ServerId == serverId)
                .Select(s => new MapSleepingBagDTO
                {
                    OwnerName = s.OwnerName,
                    Name = s.Name,
                    X = s.PositionX,
                    Y = s.PositionY,
                    Z = s.PositionZ,
                    Type = s.Type
                })
                .ToListAsync();

            // Get tool cupboards for this server
            var toolCupboards = await dbContext.ToolCupboards
                .Where(t => t.ServerId == serverId)
                .Select(t => new MapToolCupboardDTO
                {
                    OwnerName = t.OwnerName,
                    X = t.PositionX,
                    Y = t.PositionY,
                    Z = t.PositionZ,
                    AuthorizedPlayers = t.AuthorizedPlayers
                })
                .ToListAsync();

            var dto = new Dashboard_MapDataDTO
            {
                ServerId = serverId,
                RrsmModInitialized = server.RrsmModInitialized,
                HasMapData = mapData != null && mapData.ImageData != null && mapData.ImageData.Length > 0,
                ImageUrl = mapData != null ? $"/api/external/map/{serverId}" : null,
                MapSize = mapData?.MapSize,
                MapSeed = mapData?.MapSeed,
                ImageWidth = mapData?.ImageWidth,
                ImageHeight = mapData?.ImageHeight,
                UpdatedAt = mapData?.UpdatedAt,
                Players = onlinePlayers,
                SleepingBags = sleepingBags,
                ToolCupboards = toolCupboards
            };

            return Ok(dto);
        }

        /// <summary>
        /// Requests map data from the server via the panel mod, over RCON, and stores it
        /// immediately - the mod replies with the data directly instead of calling back to the
        /// panel, so this completes in a single request/response instead of a fire-and-wait.
        /// This will cause the server to lag for a few seconds while generating the map.
        /// </summary>
        [HttpPost("RequestMapData")]
        public async Task<IActionResult> RequestMapData([FromQuery] string? scale = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
            {
                return NotFound("No selected server.");
            }

            if (!await User.HasServerAccess(dbContext, serverId))
            {
                return Unauthorized("You do not have access to this server.");
            }

            var server = await dbContext.RconServers.FindAsync(serverId);
            if (server == null)
            {
                return NotFound("Server not found.");
            }

            if (!server.RrsmModInitialized)
            {
                return BadRequest("Panel mod is not initialized on this server.");
            }

            bool isConnected = await _rconService.IsServerConnected(serverId);
            if (!isConnected)
            {
                return BadRequest("Server is not connected.");
            }

            var (success, error) = await _rrsmModDataService.RequestAndStoreMapDataAsync(serverId, scale);
            if (!success)
            {
                return BadRequest(error);
            }

            return Ok(new { Success = true, Message = "Map data updated." });
        }

        /// <summary>
        /// Requests sleeping bag data from the server via the panel mod, over RCON, and stores
        /// it immediately.
        /// </summary>
        [HttpPost("RequestSleepingBags")]
        public async Task<IActionResult> RequestSleepingBags()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
                return NotFound("No selected server.");

            if (!await User.HasServerAccess(dbContext, serverId))
                return Unauthorized("You do not have access to this server.");

            var server = await dbContext.RconServers.FindAsync(serverId);
            if (server == null)
                return NotFound("Server not found.");

            if (!server.RrsmModInitialized)
                return BadRequest("Panel mod is not initialized on this server.");

            bool isConnected = await _rconService.IsServerConnected(serverId);
            if (!isConnected)
                return BadRequest("Server is not connected.");

            var (success, error, count) = await _rrsmModDataService.RequestAndStoreSleepingBagsAsync(serverId);
            if (!success)
                return BadRequest(error);

            return Ok(new { Success = true, Count = count, Message = "Sleeping bag data updated." });
        }

        /// <summary>
        /// Requests tool cupboard data from the server via the panel mod, over RCON, and stores
        /// it immediately.
        /// </summary>
        [HttpPost("RequestToolCupboards")]
        public async Task<IActionResult> RequestToolCupboards()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
                return NotFound("No selected server.");

            if (!await User.HasServerAccess(dbContext, serverId))
                return Unauthorized("You do not have access to this server.");

            var server = await dbContext.RconServers.FindAsync(serverId);
            if (server == null)
                return NotFound("Server not found.");

            if (!server.RrsmModInitialized)
                return BadRequest("Panel mod is not initialized on this server.");

            bool isConnected = await _rconService.IsServerConnected(serverId);
            if (!isConnected)
                return BadRequest("Server is not connected.");

            var (success, error, count) = await _rrsmModDataService.RequestAndStoreToolCupboardsAsync(serverId);
            if (!success)
                return BadRequest(error);

            return Ok(new { Success = true, Count = count, Message = "Tool cupboard data updated." });
        }

    }
}
