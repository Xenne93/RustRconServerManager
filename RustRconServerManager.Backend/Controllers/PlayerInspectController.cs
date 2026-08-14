using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Shared.PlayerInspect;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlayerInspectController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerInspectController> _logger;
    private readonly IRconBackgroundService _rconService;

    public PlayerInspectController(
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerInspectController> logger,
        IRconBackgroundService rconService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _rconService = rconService;
    }

    [HttpGet("{steamId}")]
    public async Task<IActionResult> GetPlayerData(string steamId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Get server ID from query
            if (!HttpContext.Request.Query.ContainsKey("serverId"))
            {
                return BadRequest(new { message = "Server ID is required" });
            }

            int serverId = int.Parse(HttpContext.Request.Query["serverId"].ToString());

            // Check access
            bool access = await User.HasServerAccess(dbContext, serverId);
            if (!access)
                return Unauthorized();

            // Get player data
            var player = await dbContext.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);

            if (player == null)
            {
                return NotFound(new { message = "Player not found" });
            }

            // Calculate total playtime (difference between first and last seen)
            var totalPlaytime = player.LastSeen - player.FirstSeen;

            // Get ban information
            var ban = await dbContext.PlayerBans
                .Where(b => b.SteamId == steamId && (b.ServerId == serverId || (b.ServerId == -1 && b.IsGlobalBan)) && !b.IsLifted)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            DateTime? banExpiry = null;
            if (ban != null && ban.Expiry.HasValue && ban.Expiry.Value != -1)
            {
                banExpiry = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ban.Expiry.Value);
                // If ban has expired, treat as no active ban
                if (banExpiry.Value < DateTime.UtcNow)
                {
                    ban = null;
                    banExpiry = null;
                }
            }

            // Get chat messages
            var chatMessages = await dbContext.ChatMessages
                .Where(c => c.SteamId == steamId && c.ServerId == serverId)
                .OrderByDescending(c => c.Timestamp)
                .Take(50)
                .Select(c => new PlayerInspect_ChatMessageDTO
                {
                    Id = c.Id,
                    PlayerName = c.PlayerName ?? "Unknown",
                    Message = c.Message ?? "",
                    Channel = c.Channel ?? "Unknown",
                    CreatedAt = c.Timestamp
                })
                .ToListAsync();

            // Get kill logs (where player is killer)
            var kills = await dbContext.PlayerKillLogs
                .Where(k => k.KilledByName == player.Name && k.ServerId == serverId && k.CreatedAt.HasValue)
                .OrderByDescending(k => k.CreatedAt)
                .Take(20)
                .Select(k => new PlayerInspect_KillLogDTO
                {
                    Id = k.Id,
                    KillerName = k.KilledByName ?? "Unknown",
                    VictimName = k.KilledPlayerName ?? "Unknown",
                    CreatedAt = k.CreatedAt!.Value,
                    IsPVP = k.IsPVP
                })
                .ToListAsync();

            // Get death logs (where player is victim)
            var deaths = await dbContext.PlayerKillLogs
                .Where(k => k.KilledPlayerName == player.Name && k.ServerId == serverId && k.CreatedAt.HasValue)
                .OrderByDescending(k => k.CreatedAt)
                .Take(20)
                .Select(k => new PlayerInspect_KillLogDTO
                {
                    Id = k.Id,
                    KillerName = k.KilledByName ?? "Unknown",
                    VictimName = k.KilledPlayerName ?? "Unknown",
                    CreatedAt = k.CreatedAt!.Value,
                    IsPVP = k.IsPVP
                })
                .ToListAsync();

            // Count ALL kills and deaths for tab titles
            var totalKillCount = await dbContext.PlayerKillLogs
                .Where(k => k.KilledByName == player.Name && k.ServerId == serverId)
                .CountAsync();

            var totalDeathCount = await dbContext.PlayerKillLogs
                .Where(k => k.KilledPlayerName == player.Name && k.ServerId == serverId)
                .CountAsync();

            // Get ban history from PlayerBanHistory table
            var banHistory = await dbContext.PlayerBanHistories
                .Where(h => h.SteamId == steamId && (h.ServerId == serverId || (h.ServerId == -1 && h.IsGlobalBan)))
                .OrderByDescending(h => h.BannedAt)
                .Select(h => new PlayerInspect_BanHistoryDTO
                {
                    Id = h.Id,
                    Reason = h.Reason ?? "No reason provided",
                    CreatedAt = h.BannedAt,
                    ExpiryDate = h.ExpiryDate,
                    DurationHours = h.DurationHours,
                    IsActive = h.IsActive,
                    IsGlobalBan = h.IsGlobalBan,
                    BannedBy = h.BannedBy,
                    LiftedBy = h.LiftedBy,
                    LiftedAt = h.LiftedAt,
                    LiftReason = h.LiftReason
                })
                .ToListAsync();

            var response = new PlayerInspect_ResponseDTO
            {
                PlayerData = new PlayerInspect_PlayerDataDTO
                {
                    SteamId = player.SteamId,
                    Name = player.Name ?? "Unknown",
                    Avatar = player.Avatar,
                    Country = player.Country,
                    IsOnline = player.IsOnline,
                    LastIp = player.LastIp,
                    IsVpn = !string.IsNullOrEmpty(player.LastIp) && await dbContext.IpVpnCaches.AnyAsync(c => c.IpAddress == player.LastIp && c.IsVpn && c.ExpiresAt > DateTime.UtcNow),
                    FirstSeen = player.FirstSeen,
                    LastSeen = player.LastSeen,
                    TotalPlaytimeHours = totalPlaytime.TotalHours,
                    CurrentPing = player.LatestPing,
                    CurrentHealth = player.LatestHealth,
                    CurrentTeamId = player.LatestTeamId,
                    CurrentPosition = player.LatestPositionX.HasValue && player.LatestPositionY.HasValue && player.LatestPositionZ.HasValue
                        ? new Position { X = player.LatestPositionX.Value, Y = player.LatestPositionY.Value, Z = player.LatestPositionZ.Value }
                        : null,
                    VACBanned = player.VACBanned,
                    NumberOfVACBans = player.NumberOfVACBans,
                    DaysSinceLastVACBan = player.DaysSinceLastVACBan,
                    SteamAccountCreated = player.SteamAccountCreated,
                    RustPlaytimeMinutes = player.RustPlaytimeMinutes,
                    ProfileVisibility = player.ProfileVisibility,
                    IsBanned = ban != null,
                    BanReason = ban?.Notes,
                    BanExpiry = banExpiry,
                    IsGlobalBan = ban?.IsGlobalBan ?? false,
                    TotalChatMessages = chatMessages.Count,
                    TotalKills = totalKillCount,
                    TotalDeaths = totalDeathCount
                },
                RecentChatMessages = chatMessages,
                RecentKills = kills,
                RecentDeaths = deaths,
                BanHistory = banHistory,

                IpHistory = await dbContext.PlayerIpHistories
                    .Where(h => h.SteamId == steamId && h.ServerId == serverId)
                    .OrderByDescending(h => h.LastUsed)
                    .Select(h => new PlayerInspect_IpHistoryDTO
                    {
                        IpAddress = h.IpAddress,
                        IsVpn = h.IsVpn,
                        Country = h.Country,
                        FirstUsed = h.FirstUsed,
                        LastUsed = h.LastUsed,
                        TimesUsed = h.TimesUsed
                    })
                    .ToListAsync()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching player data for Steam ID {steamId}");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("{steamId}/chat")]
    public async Task<IActionResult> GetPlayerChatMessages(string steamId, [FromQuery] int serverId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check access
            bool access = await User.HasServerAccess(dbContext, serverId);
            if (!access)
                return Unauthorized();

            // Get chat messages with pagination
            var chatMessages = await dbContext.ChatMessages
                .Where(c => c.SteamId == steamId && c.ServerId == serverId)
                .OrderByDescending(c => c.Timestamp)
                .Skip(skip)
                .Take(take)
                .Select(c => new PlayerInspect_ChatMessageDTO
                {
                    Id = c.Id,
                    PlayerName = c.PlayerName ?? "Unknown",
                    Message = c.Message ?? "",
                    Channel = c.Channel ?? "Unknown",
                    CreatedAt = c.Timestamp
                })
                .ToListAsync();

            // Get total count for this player
            var totalCount = await dbContext.ChatMessages
                .Where(c => c.SteamId == steamId && c.ServerId == serverId)
                .CountAsync();

            return Ok(new {
                messages = chatMessages,
                totalCount = totalCount,
                hasMore = (skip + take) < totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching chat messages for Steam ID {steamId}");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("{steamId}/chat/search")]
    public async Task<IActionResult> SearchPlayerChatMessages(string steamId, [FromQuery] int serverId, [FromQuery] string query, [FromQuery] int take = 100)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check access
            bool access = await User.HasServerAccess(dbContext, serverId);
            if (!access)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { message = "Search query is required" });
            }

            // Search chat messages
            var chatMessages = await dbContext.ChatMessages
                .Where(c => c.SteamId == steamId && c.ServerId == serverId &&
                           (c.Message != null && c.Message.Contains(query)))
                .OrderByDescending(c => c.Timestamp)
                .Take(take)
                .Select(c => new PlayerInspect_ChatMessageDTO
                {
                    Id = c.Id,
                    PlayerName = c.PlayerName ?? "Unknown",
                    Message = c.Message ?? "",
                    Channel = c.Channel ?? "Unknown",
                    CreatedAt = c.Timestamp
                })
                .ToListAsync();

            return Ok(new {
                messages = chatMessages,
                totalFound = chatMessages.Count,
                query = query
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error searching chat messages for Steam ID {steamId}");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("{steamId}/kills")]
    public async Task<IActionResult> GetPlayerKills(string steamId, [FromQuery] int serverId, [FromQuery] int skip = 0, [FromQuery] int take = 25)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            bool access = await User.HasServerAccess(dbContext, serverId);
            if (!access)
                return Unauthorized();

            var player = await dbContext.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);
            if (player == null)
                return NotFound(new { message = "Player not found" });

            var kills = await dbContext.PlayerKillLogs
                .Where(k => k.KilledByName == player.Name && k.ServerId == serverId && k.CreatedAt.HasValue)
                .OrderByDescending(k => k.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(k => new PlayerInspect_KillLogDTO
                {
                    Id = k.Id,
                    KillerName = k.KilledByName ?? "Unknown",
                    VictimName = k.KilledPlayerName ?? "Unknown",
                    CreatedAt = k.CreatedAt!.Value,
                    IsPVP = k.IsPVP
                })
                .ToListAsync();

            var totalCount = await dbContext.PlayerKillLogs
                .Where(k => k.KilledByName == player.Name && k.ServerId == serverId && k.CreatedAt.HasValue)
                .CountAsync();

            return Ok(new
            {
                items = kills,
                totalCount,
                hasMore = (skip + take) < totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching kills for Steam ID {steamId}");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("{steamId}/deaths")]
    public async Task<IActionResult> GetPlayerDeaths(string steamId, [FromQuery] int serverId, [FromQuery] int skip = 0, [FromQuery] int take = 25)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            bool access = await User.HasServerAccess(dbContext, serverId);
            if (!access)
                return Unauthorized();

            var player = await dbContext.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);
            if (player == null)
                return NotFound(new { message = "Player not found" });

            var deaths = await dbContext.PlayerKillLogs
                .Where(k => k.KilledPlayerName == player.Name && k.ServerId == serverId && k.CreatedAt.HasValue)
                .OrderByDescending(k => k.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(k => new PlayerInspect_KillLogDTO
                {
                    Id = k.Id,
                    KillerName = k.KilledByName ?? "Unknown",
                    VictimName = k.KilledPlayerName ?? "Unknown",
                    CreatedAt = k.CreatedAt!.Value,
                    IsPVP = k.IsPVP
                })
                .ToListAsync();

            var totalCount = await dbContext.PlayerKillLogs
                .Where(k => k.KilledPlayerName == player.Name && k.ServerId == serverId && k.CreatedAt.HasValue)
                .CountAsync();

            return Ok(new
            {
                items = deaths,
                totalCount,
                hasMore = (skip + take) < totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching deaths for Steam ID {steamId}");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpPost("{steamId}/clear-data")]
    public async Task<IActionResult> ClearPlayerData(string steamId, [FromBody] ClearPlayerDataRequest request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            bool access = await User.HasServerAccess(dbContext, request.ServerId);
            if (!access)
                return Unauthorized();

            var player = await dbContext.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == request.ServerId);
            if (player == null)
                return NotFound(new { message = "Player not found" });

            int deletedChat = 0, deletedKills = 0, deletedDeaths = 0, deletedNotes = 0, deletedReports = 0, deletedBanHistory = 0;

            if (request.ChatMessages || request.All)
            {
                var messages = await dbContext.ChatMessages
                    .Where(c => c.SteamId == steamId && c.ServerId == request.ServerId)
                    .ToListAsync();
                deletedChat = messages.Count;
                dbContext.ChatMessages.RemoveRange(messages);
            }

            if (request.Kills || request.All)
            {
                var kills = await dbContext.PlayerKillLogs
                    .Where(k => k.KilledByName == player.Name && k.ServerId == request.ServerId)
                    .ToListAsync();
                deletedKills = kills.Count;
                dbContext.PlayerKillLogs.RemoveRange(kills);
            }

            if (request.Deaths || request.All)
            {
                var deaths = await dbContext.PlayerKillLogs
                    .Where(k => k.KilledPlayerName == player.Name && k.ServerId == request.ServerId)
                    .ToListAsync();
                deletedDeaths = deaths.Count;
                dbContext.PlayerKillLogs.RemoveRange(deaths);
            }

            if (request.Notes || request.All)
            {
                var notes = await dbContext.PlayerNotes
                    .Where(n => n.SteamId == steamId && n.ServerId == request.ServerId)
                    .ToListAsync();
                deletedNotes = notes.Count;
                dbContext.PlayerNotes.RemoveRange(notes);
            }

            if (request.Reports || request.All)
            {
                var reports = await dbContext.PlayerReports
                    .Where(r => (r.ReportedId == steamId || r.ReporterId == steamId) && r.ServerId == request.ServerId)
                    .ToListAsync();
                deletedReports = reports.Count;
                dbContext.PlayerReports.RemoveRange(reports);
            }

            if (request.BanHistory || request.All)
            {
                var banHistory = await dbContext.PlayerBanHistories
                    .Where(h => h.SteamId == steamId && h.ServerId == request.ServerId)
                    .ToListAsync();
                deletedBanHistory = banHistory.Count;
                dbContext.PlayerBanHistories.RemoveRange(banHistory);
            }

            if (request.DeletePlayer)
            {
                // Remove active bans too
                var activeBans = await dbContext.PlayerBans
                    .Where(b => b.SteamId == steamId && b.ServerId == request.ServerId)
                    .ToListAsync();
                dbContext.PlayerBans.RemoveRange(activeBans);

                dbContext.SteamPlayers.Remove(player);
            }

            await dbContext.SaveChangesAsync();

            _logger.LogInformation("[PlayerInspect] Cleared data for {SteamId} on server {ServerId}: Chat={Chat}, Kills={Kills}, Deaths={Deaths}, Notes={Notes}, Reports={Reports}, BanHistory={BanHistory}, PlayerDeleted={Deleted}",
                steamId, request.ServerId, deletedChat, deletedKills, deletedDeaths, deletedNotes, deletedReports, deletedBanHistory, request.DeletePlayer);

            return Ok(new
            {
                message = request.DeletePlayer ? "Player and all data deleted" : "Selected data cleared",
                deletedChat,
                deletedKills,
                deletedDeaths,
                deletedNotes,
                deletedReports,
                deletedBanHistory,
                playerDeleted = request.DeletePlayer
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing player data for {SteamId}", steamId);
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    public class ClearPlayerDataRequest
    {
        public int ServerId { get; set; }
        public bool ChatMessages { get; set; }
        public bool Kills { get; set; }
        public bool Deaths { get; set; }
        public bool Notes { get; set; }
        public bool Reports { get; set; }
        public bool BanHistory { get; set; }
        public bool All { get; set; }
        public bool DeletePlayer { get; set; }
    }

    [HttpGet("{steamId}/inventory")]
    public async Task<IActionResult> GetPlayerInventory(string steamId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
                return NotFound("No selected server.");

            bool isConnected = await _rconService.IsServerConnected(serverId);
            if (!isConnected)
                return BadRequest("Server is not connected via RCON.");

            string? response = await _rconService.ExecuteCommandWithResponse(
                $"rrsm.getplayerinventory {steamId}", serverId, 10000);

            if (string.IsNullOrEmpty(response))
                return BadRequest("No response from server. Player may be offline.");

            // The mod returns "200-{json}" format
            if (response.StartsWith("200-"))
                return Ok(response.Substring(4));

            return BadRequest(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player inventory for {SteamId}", steamId);
            return StatusCode(500, "An error occurred while fetching the inventory.");
        }
    }
}
