
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Shared.Account;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.Dashboard;
using RustRconServerManager.Shared.PlayerList;
using System.Linq;

namespace RustRconServerManager.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public partial class DashboardController : ControllerBase
    {

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IRconBackgroundService _rconService;
        private readonly IRconPasswordsCryptoService _rconPasswordsCryptoService;
        private readonly RrsmModDataService _rrsmModDataService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IServiceScopeFactory scopeFactory,
            IRconBackgroundService rconService,
            IRconPasswordsCryptoService rconPasswordsCryptoService,
            RrsmModDataService rrsmModDataService,
            ILogger<DashboardController> logger)
        {

            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _rconService = rconService ?? throw new ArgumentNullException(nameof(rconService));
            _rconPasswordsCryptoService = rconPasswordsCryptoService ?? throw new ArgumentNullException(nameof(rconPasswordsCryptoService));
            _rrsmModDataService = rrsmModDataService ?? throw new ArgumentNullException(nameof(rrsmModDataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        }
        
        
        
        
        
        
        
        // Checks if the user is allowed to view that server.
        // Used by Dashboard page.
       [HttpGet("CheckSelectedServer/{serverid}")]
       public async Task<IActionResult> CheckSelectedServer(int serverid)
       {
           var scope = _scopeFactory.CreateScope();
           var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

           bool access = await User.HasServerAccess(dbContext, serverid);

           if (access)
               return Ok();
           else
               return Unauthorized();

       }

       // Get playerlist with online/offline players
       // Used by PlayerList page
       [HttpGet("GetPlayerList/{serverid}")]
       public async Task<IActionResult> GetPlayerList(
           int serverid,
           [FromQuery] string? search = null,
           [FromQuery] int offlinePage = 1,
           [FromQuery] int offlinePageSize = 100)
       {
           var scope = _scopeFactory.CreateScope();
           var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

           bool access = await User.HasServerAccess(dbContext, serverid);

           if (!access)
               return Unauthorized();

           // Clamp pagination params to sane values
           if (offlinePage < 1) offlinePage = 1;
           if (offlinePageSize != 100 && offlinePageSize != 200 && offlinePageSize != 500)
               offlinePageSize = 100;

           // Fetch all players for this server with their ban information
           // Exclude entries without a SteamId (stale/invalid records)
           var allPlayers = await dbContext.SteamPlayers
               .Where(p => p.ServerId == serverid && p.SteamId != null && p.SteamId != "")
               .ToListAsync();

           // Get all active bans for this server (including global bans), exclude lifted bans
           var bans = await dbContext.PlayerBans
               .Where(b => (b.ServerId == serverid || (b.ServerId == -1 && b.IsGlobalBan)) && !b.IsLifted)
               .ToListAsync();

           // Get report counts for all players on this server
           var reportCounts = await dbContext.PlayerReports
               .Where(r => r.ServerId == serverid)
               .GroupBy(r => r.ReportedId)
               .Select(g => new { SteamId = g.Key, Count = g.Count() })
               .ToDictionaryAsync(x => x.SteamId, x => x.Count);

           // Get all player IPs and check VPN cache
           var playerIps = allPlayers
               .Where(p => !string.IsNullOrEmpty(p.LastIp))
               .Select(p => p.LastIp!)
               .Distinct()
               .ToList();

           var vpnCache = await dbContext.IpVpnCaches
               .Where(c => playerIps.Contains(c.IpAddress) && c.ExpiresAt > DateTime.UtcNow)
               .ToDictionaryAsync(c => c.IpAddress, c => c.IsVpn);

           var playerListDto = new PlayerList_PlayerListDTO();

           foreach (var player in allPlayers)
           {
               // Check if player has an active (non-expired, non-lifted) ban
               var playerBan = bans.FirstOrDefault(b => b.SteamId == player.SteamId);
               var isBanned = false;
               string? banReason = null;
               if (playerBan != null)
               {
                   // Check if ban is expired
                   if (playerBan.Expiry.HasValue && playerBan.Expiry.Value != -1)
                   {
                       var expiryDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(playerBan.Expiry.Value);
                       isBanned = expiryDate > DateTime.UtcNow;
                   }
                   else
                   {
                       isBanned = true; // Permanent ban
                   }
                   if (isBanned) banReason = playerBan.Notes;
               }

               // Get report count for this player
               var reportCount = reportCounts.ContainsKey(player.SteamId) ? reportCounts[player.SteamId] : 0;

               var playerDto = new PlayerList_PlayerDTO
               {
                   SteamId = player.SteamId,
                   Name = player.Name ?? "Unknown",
                   Avatar = player.Avatar,
                   Country = player.Country,
                   IsOnline = player.IsOnline,
                   CurrentPing = player.LatestPing,
                   Health = player.LatestHealth,
                   TeamId = player.LatestTeamId,
                   IsBanned = isBanned,
                   BanReason = banReason,
                   ReportCount = reportCount,
                   VACBanned = player.VACBanned,
                   NumberOfVACBans = player.NumberOfVACBans,
                   DaysSinceLastVACBan = player.DaysSinceLastVACBan,
                   LastIp = player.LastIp,
                   FirstSeen = player.FirstSeen,
                   LastSeen = player.LastSeen,
                   ConnectedSeconds = (DateTime.UtcNow - player.LastSeen).TotalSeconds,
                   SteamAccountCreated = player.SteamAccountCreated,
                   RustPlaytimeMinutes = player.RustPlaytimeMinutes,
                   ProfileVisibility = player.ProfileVisibility,
                   IsVpn = !string.IsNullOrEmpty(player.LastIp) && vpnCache.TryGetValue(player.LastIp, out var isVpn) && isVpn
               };

               // Banned players are treated as offline (they'll be kicked by the server)
               if (playerDto.IsOnline && !playerDto.IsBanned)
                   playerListDto.OnlinePlayers.Add(playerDto);
               else
                   playerListDto.OfflinePlayers.Add(playerDto);
           }

           playerListDto.TotalOnline = playerListDto.OnlinePlayers.Count;
           playerListDto.TotalOffline = playerListDto.OfflinePlayers.Count;
           playerListDto.TotalBanned =
               playerListDto.OnlinePlayers.Count(p => p.IsBanned) +
               playerListDto.OfflinePlayers.Count(p => p.IsBanned);

           // Sort online by name (always returned in full)
           playerListDto.OnlinePlayers = playerListDto.OnlinePlayers.OrderBy(p => p.Name).ToList();

           // Server-side search filter + pagination for offline players
           var offlineSorted = playerListDto.OfflinePlayers.OrderBy(p => p.Name).AsEnumerable();

           if (!string.IsNullOrWhiteSpace(search))
           {
               var term = search.Trim().ToLowerInvariant();
               offlineSorted = offlineSorted.Where(p =>
                   (p.Name ?? string.Empty).ToLowerInvariant().Contains(term) ||
                   (p.SteamId ?? string.Empty).ToLowerInvariant().Contains(term) ||
                   (p.Country ?? string.Empty).ToLowerInvariant().Contains(term) ||
                   (p.LastIp ?? string.Empty).ToLowerInvariant().Contains(term));
           }

           var offlineFiltered = offlineSorted.ToList();
           playerListDto.OfflineFilteredTotal = offlineFiltered.Count;
           playerListDto.OfflinePage = offlinePage;
           playerListDto.OfflinePageSize = offlinePageSize;

           playerListDto.OfflinePlayers = offlineFiltered
               .Skip((offlinePage - 1) * offlinePageSize)
               .Take(offlinePageSize)
               .ToList();

           return Ok(playerListDto);
       }

       // Ban a player
       // Used by PlayerList page
       [HttpPost("ban-player")]
       public async Task<IActionResult> BanPlayer([FromBody] BanPlayerRequest request)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Verify user has access to this server
               bool access = await User.HasServerAccess(dbContext, request.ServerId);
               if (!access)
                   return Unauthorized();

               // Calculate ban expiration
               var banExpiredAt = DateTime.UtcNow.AddHours(request.DurationHours);
               var now = DateTime.UtcNow;
               DateTime? expiryDate = banExpiredAt == now ? null : banExpiredAt;

               // Get current user email for audit trail
               var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "System";

               // Create PlayerBan record (single source of truth for bans)
               // If global ban, use ServerId = -1
               PlayerBan banRecord;
               if (request.IsGlobalBan)
               {
                   banRecord = new PlayerBan
                   {
                       ServerId = -1, // -1 indicates global ban
                       SteamId = request.SteamId,
                       Username = request.PlayerName,
                       Notes = request.Reason,
                       InternalNote = request.InternalNote,
                       Expiry = expiryDate.HasValue ? ConvertDateTimeToUnixTimestamp(expiryDate.Value) : null,
                       IsGlobalBan = true,
                       CreatedAt = now,
                       UpdatedAt = now
                   };

                   dbContext.PlayerBans.Add(banRecord);
                   await dbContext.SaveChangesAsync();
               }
               else
               {
                   // For server-specific bans, also create a PlayerBan record
                   banRecord = new PlayerBan
                   {
                       ServerId = request.ServerId,
                       SteamId = request.SteamId,
                       Username = request.PlayerName,
                       Notes = request.Reason,
                       InternalNote = request.InternalNote,
                       Expiry = expiryDate.HasValue ? ConvertDateTimeToUnixTimestamp(expiryDate.Value) : null,
                       IsGlobalBan = false,
                       CreatedAt = now,
                       UpdatedAt = now
                   };

                   dbContext.PlayerBans.Add(banRecord);
                   await dbContext.SaveChangesAsync();
               }

               // Create history record
               var historyRecord = new PlayerBanHistory
               {
                   PlayerBanId = banRecord.Id,
                   SteamId = request.SteamId,
                   PlayerName = request.PlayerName,
                   ServerId = request.IsGlobalBan ? -1 : request.ServerId,
                   IsGlobalBan = request.IsGlobalBan,
                   Reason = request.Reason,
                   BannedAt = now,
                   ExpiryDate = expiryDate,
                   DurationHours = request.DurationHours,
                   IsActive = true,
                   BannedBy = userEmail,
                   CreatedAt = now
               };

               dbContext.PlayerBanHistories.Add(historyRecord);
               await dbContext.SaveChangesAsync();

               // Send ban command to the Rust server(s) and create server-specific ban records
               // Format: banid <steamid> <playername> <reason> <duration_in_hours>
               if (request.IsGlobalBan)
               {
                   // For global bans, ban the player on ALL servers
                   var allServers = await dbContext.RconServers.ToListAsync();
                   foreach (var server in allServers)
                   {
                       // Create server-specific ban record (copy of global ban)
                       var existingServerBan = await dbContext.PlayerBans
                           .FirstOrDefaultAsync(b => b.SteamId == request.SteamId && b.ServerId == server.Id);

                       if (existingServerBan == null)
                       {
                           var serverBan = new PlayerBan
                           {
                               ServerId = server.Id,
                               SteamId = request.SteamId,
                               Username = request.PlayerName,
                               Notes = $"[GLOBAL BAN ENFORCEMENT] {request.Reason}",
                               Expiry = expiryDate.HasValue ? ConvertDateTimeToUnixTimestamp(expiryDate.Value) : null,
                               IsGlobalBan = false, // This is the server-specific copy
                               CreatedAt = now,
                               UpdatedAt = now
                           };

                           dbContext.PlayerBans.Add(serverBan);
                           await dbContext.SaveChangesAsync(); // Save to get the ID

                           // Create history record for this server-specific ban
                           var serverHistoryRecord = new PlayerBanHistory
                           {
                               PlayerBanId = serverBan.Id,
                               SteamId = request.SteamId,
                               PlayerName = request.PlayerName,
                               ServerId = server.Id,
                               IsGlobalBan = false,
                               Reason = $"[GLOBAL BAN ENFORCEMENT] {request.Reason}",
                               BannedAt = now,
                               ExpiryDate = expiryDate,
                               DurationHours = request.DurationHours,
                               IsActive = true,
                               BannedBy = userEmail,
                               CreatedAt = now
                           };

                           dbContext.PlayerBanHistories.Add(serverHistoryRecord);
                           await dbContext.SaveChangesAsync(); // Save history
                       }

                       // Send ban command to server
                       try
                       {
                               string banCommand = $"banid {request.SteamId} \"{request.PlayerName}\" \"[GLOBAL BAN] {request.Reason}\" {request.DurationHours}";
                           _rconService.SendRconCommand(banCommand, server.Id);
                           _logger.LogInformation("Sent global ban command to server {ServerName} (ID: {ServerId}) for player {PlayerName}", server.Name, server.Id, request.PlayerName);
                       }
                       catch (Exception ex)
                       {
                           // Log but don't fail if server is offline - ban is still tracked in database
                           _logger.LogWarning(ex, "Could not send global ban command to server {ServerName}", server.Name);
                       }
                   }
               }
               else
               {
                   // For server-specific bans, only ban on the requested server
                   try
                   {
                       string banCommand = $"banid {request.SteamId} \"{request.PlayerName}\" \"{request.Reason}\" {request.DurationHours}";
                       _rconService.SendRconCommand(banCommand, request.ServerId);
                   }
                   catch (Exception ex)
                   {
                       // Log but don't fail if server is offline - ban is still tracked in database
                       _logger.LogWarning(ex, "Could not send ban command to server");
                   }
               }

               // Send Discord webhook for player ban (async, don't wait for response)
               var banDurationMinutes = request.DurationHours * 60;
               var banServerId = request.IsGlobalBan ? -1 : request.ServerId;
               var banPlayerName = request.PlayerName;
               var banSteamId = request.SteamId;
               var banReason = request.Reason;
               _ = Task.Run(async () =>
               {
                   try
                   {
                       using var webhookScope = _scopeFactory.CreateScope();
                       var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();

                       if (request.IsGlobalBan)
                       {
                           // For global bans, send webhook for each server
                           using var dbScope = _scopeFactory.CreateScope();
                           var db = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();
                           var servers = await db.RconServers.ToListAsync();
                           foreach (var server in servers)
                           {
                               await webhookService.SendPlayerBanAsync(server.Id, banPlayerName, banSteamId, $"[GLOBAL BAN] {banReason}", (int)banDurationMinutes);
                           }
                       }
                       else
                       {
                           // For server-specific bans
                           await webhookService.SendPlayerBanAsync(banServerId, banPlayerName, banSteamId, banReason, (int)banDurationMinutes);
                       }
                   }
                   catch (Exception ex)
                   {
                       _logger.LogWarning(ex, "Failed to send Discord ban webhook");
                   }
               });

               return Ok(new { message = $"Player {request.PlayerName} has been banned successfully" });
           }
           catch (Exception ex)
           {
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // Unban a player (removes PlayerBan record)
       // Used by PlayerList page
       [HttpPost("unban-player")]
       public async Task<IActionResult> UnbanPlayer([FromBody] UnbanPlayerRequest request)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Verify user has access to this server
               bool access = await User.HasServerAccess(dbContext, request.ServerId);
               if (!access)
                   return Unauthorized();

               // Find the ban record — check server-specific first, then global
               var banRecord = await dbContext.PlayerBans
                   .FirstOrDefaultAsync(b => b.SteamId == request.SteamId && b.ServerId == request.ServerId);

               var globalBan = await dbContext.PlayerBans
                   .FirstOrDefaultAsync(b => b.SteamId == request.SteamId && b.ServerId == -1 && b.IsGlobalBan);

               if (banRecord == null && globalBan == null)
                   return NotFound("Ban record not found");

               var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "System";
               var now = DateTime.UtcNow;

               if (globalBan != null)
               {
                   // Global ban: remove from all servers (same as DeleteBan for global)
                   var relatedServerBans = await dbContext.PlayerBans
                       .Where(b => b.SteamId == request.SteamId && b.ServerId != -1)
                       .ToListAsync();

                   foreach (var serverBan in relatedServerBans)
                   {
                       var historyRecords = await dbContext.PlayerBanHistories
                           .Where(h => h.SteamId == serverBan.SteamId && h.ServerId == serverBan.ServerId && h.IsActive)
                           .ToListAsync();

                       foreach (var history in historyRecords)
                       {
                           history.IsActive = false;
                           history.LiftedAt = now;
                           history.LiftedBy = userEmail;
                           history.LiftReason = "Manually unbanned via panel";
                           history.UpdatedAt = now;
                       }

                       try
                       {
                           _rconService.SendRconCommand($"unban {request.SteamId}", serverBan.ServerId);
                       }
                       catch (Exception ex)
                       {
                           _logger.LogWarning(ex, "[UNBAN] Could not send unban command to server {ServerId}", serverBan.ServerId);
                       }

                       dbContext.PlayerBans.Remove(serverBan);
                   }

                   // Update global ban history
                   var globalHistoryRecords = await dbContext.PlayerBanHistories
                       .Where(h => h.PlayerBanId == globalBan.Id && h.IsActive)
                       .ToListAsync();

                   foreach (var history in globalHistoryRecords)
                   {
                       history.IsActive = false;
                       history.LiftedAt = now;
                       history.LiftedBy = userEmail;
                       history.LiftReason = "Manually unbanned via panel";
                       history.UpdatedAt = now;
                   }

                   dbContext.PlayerBans.Remove(globalBan);
                   await dbContext.SaveChangesAsync();

                   return Ok(new { message = $"Global ban for {request.PlayerName} has been removed from all servers" });
               }
               else
               {
                   // Server-specific ban
                   var historyRecord = await dbContext.PlayerBanHistories
                       .Where(h => h.PlayerBanId == banRecord.Id && h.IsActive)
                       .FirstOrDefaultAsync();

                   if (historyRecord != null)
                   {
                       historyRecord.IsActive = false;
                       historyRecord.LiftedBy = userEmail;
                       historyRecord.LiftedAt = now;
                       historyRecord.LiftReason = "Manually unbanned via panel";
                       historyRecord.UpdatedAt = now;
                   }

                   try
                   {
                       _rconService.SendRconCommand($"unban {request.SteamId}", request.ServerId);
                   }
                   catch (Exception ex)
                   {
                       _logger.LogWarning(ex, "[UNBAN] Could not send unban command to server");
                   }

                   dbContext.PlayerBans.Remove(banRecord);
                   await dbContext.SaveChangesAsync();

                   return Ok(new { message = $"Player {request.PlayerName} has been unbanned successfully" });
               }
           }
           catch (Exception ex)
           {
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // Kick a player
       // Used by PlayerList page
       [HttpPost("kick-player")]
       public async Task<IActionResult> KickPlayer([FromBody] KickPlayerRequest request)
       {
           try
           {
               using var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Send kick command to the Rust server
               // Format: kick <steamid> "<reason>"
               try
               {
                   string kickCommand = string.IsNullOrWhiteSpace(request.Reason)
                       ? $"kick {request.SteamId}"
                       : $"kick {request.SteamId} \"{request.Reason}\"";

                   _rconService.SendRconCommand(kickCommand, request.ServerId);
               }
               catch (Exception ex)
               {
                   // Log but don't fail if server is offline
                   _logger.LogWarning(ex, "Could not send kick command to server");
                   return BadRequest(new { error = ApiErrorHelper.FormatError("Failed to kick player", ex) });
               }

               // Send Discord webhook for player kick (async, don't wait for response)
               var kickPlayerName = request.PlayerName;
               var kickSteamId = request.SteamId;
               var kickReason = string.IsNullOrWhiteSpace(request.Reason) ? "No reason provided" : request.Reason;
               var kickServerId = request.ServerId;
               _ = Task.Run(async () =>
               {
                   try
                   {
                       using var webhookScope = _scopeFactory.CreateScope();
                       var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                       await webhookService.SendPlayerKickAsync(kickServerId, kickPlayerName, kickSteamId, kickReason);
                   }
                   catch (Exception ex)
                   {
                       _logger.LogWarning(ex, "Failed to send Discord kick webhook");
                   }
               });

               return Ok(new { message = $"Player {request.PlayerName} has been kicked successfully" });
           }
           catch (Exception ex)
           {
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // DTO for ban request
       public class BanPlayerRequest
       {
           public string SteamId { get; set; }
           public string PlayerName { get; set; }
           public string Reason { get; set; }
           public long DurationHours { get; set; }
           public bool IsGlobalBan { get; set; }
           public int ServerId { get; set; }
           public string? InternalNote { get; set; }
       }

       // DTO for unban request
       public class UnbanPlayerRequest
       {
           public string SteamId { get; set; }
           public string PlayerName { get; set; }
           public int ServerId { get; set; }
       }

       // DTO for kick request
       public class KickPlayerRequest
       {
           public string SteamId { get; set; }
           public string PlayerName { get; set; }
           public string Reason { get; set; }
           public int ServerId { get; set; }
       }

       // Get all bans for a server (includes global bans)
       // Used by Banlist page
       [HttpGet("GetServerBans/{serverid}")]
       public async Task<IActionResult> GetServerBans(int serverid)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               bool access = await User.HasServerAccess(dbContext, serverid);
               if (!access)
                   return Unauthorized();

               // Get all bans (server-specific and global)
               var allBans = await dbContext.PlayerBans
                   .Where(b => b.ServerId == serverid || (b.ServerId == -1 && b.IsGlobalBan))
                   .OrderByDescending(b => b.CreatedAt)
                   .ToListAsync();

               // Build server name lookup
               var serverIds = allBans.Select(b => b.ServerId).Where(id => id > 0).Distinct().ToList();
               var serverNames = await dbContext.RconServers
                   .Where(s => serverIds.Contains(s.Id))
                   .ToDictionaryAsync(s => s.Id, s => s.Name);

               // Filter out server-specific bans if a global ban exists for the same player
               var globalBannedSteamIds = allBans
                   .Where(b => b.IsGlobalBan && b.ServerId == -1)
                   .Select(b => b.SteamId)
                   .ToHashSet();

               var bans = allBans
                   .Where(b => !b.IsLifted && (b.IsGlobalBan || !globalBannedSteamIds.Contains(b.SteamId)))
                   .Select(b => MapBanToDto(b, serverNames))
                   .ToList();

               return Ok(bans);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error getting server bans");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       [HttpGet("GetServerReports/{serverid}")]
       public async Task<IActionResult> GetServerReports(int serverid)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               bool access = await User.HasServerAccess(dbContext, serverid);
               if (!access)
                   return Unauthorized();

               // Get all reports for this server
               var reports = await dbContext.PlayerReports
                   .Where(r => r.ServerId == serverid)
                   .OrderByDescending(r => r.CreatedAt)
                   .Select(r => new PlayerReportDto
                   {
                       Id = r.Id,
                       ServerId = r.ServerId,
                       ReporterId = r.ReporterId,
                       ReporterName = r.ReporterName,
                       ReportedId = r.ReportedId,
                       ReportedName = r.ReportedName,
                       Subject = r.Subject,
                       Message = r.Message,
                       Type = r.Type,
                       Status = r.Status,
                       AdminNotes = r.AdminNotes,
                       ReviewedByUserId = r.ReviewedByUserId,
                       ReviewedAt = r.ReviewedAt,
                       IsArchived = r.IsArchived,
                       CreatedAt = r.CreatedAt,
                       UpdatedAt = r.UpdatedAt
                   })
                   .ToListAsync();

               return Ok(reports);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error getting server reports");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // Get all reports for a specific player
       // Used by PlayerInspect page
       [HttpGet("GetPlayerReports/{steamId}")]
       public async Task<IActionResult> GetPlayerReports(string steamId, [FromQuery] int serverId)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               bool access = await User.HasServerAccess(dbContext, serverId);
               if (!access)
                   return Unauthorized();

               // Get all reports where this player is the reported player
               var reports = await dbContext.PlayerReports
                   .Where(r => r.ServerId == serverId && r.ReportedId == steamId)
                   .OrderByDescending(r => r.CreatedAt)
                   .Select(r => new PlayerReportDto
                   {
                       Id = r.Id,
                       ServerId = r.ServerId,
                       ReporterId = r.ReporterId,
                       ReporterName = r.ReporterName,
                       ReportedId = r.ReportedId,
                       ReportedName = r.ReportedName,
                       Subject = r.Subject,
                       Message = r.Message,
                       Type = r.Type,
                       Status = r.Status,
                       AdminNotes = r.AdminNotes,
                       ReviewedByUserId = r.ReviewedByUserId,
                       ReviewedAt = r.ReviewedAt,
                       IsArchived = r.IsArchived,
                       CreatedAt = r.CreatedAt,
                       UpdatedAt = r.UpdatedAt
                   })
                   .ToListAsync();

               return Ok(reports);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error getting player reports");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // Update a player report (status and admin notes)
       // Used by Banlist page
       [HttpPut("UpdateReport/{reportId}")]
       public async Task<IActionResult> UpdateReport(int reportId, [FromBody] UpdateReportRequest request)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Get the report
               var report = await dbContext.PlayerReports.FindAsync(reportId);
               if (report == null)
                   return NotFound(new { error = "Report not found" });

               // Check if user has access to the server
               bool access = await User.HasServerAccess(dbContext, report.ServerId);
               if (!access)
                   return Unauthorized();

               // Get current user ID
               var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

               // Update report
               report.Status = request.Status;
               report.AdminNotes = request.AdminNotes;
               report.IsArchived = request.IsArchived;
               report.ReviewedByUserId = userId;
               report.ReviewedAt = DateTime.UtcNow;
               report.UpdatedAt = DateTime.UtcNow;

               await dbContext.SaveChangesAsync();

               return Ok(new { message = "Report updated successfully" });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error updating report");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // DTO for updating report
       public class UpdateReportRequest
       {
           public string Status { get; set; }
           public string AdminNotes { get; set; }
           public bool IsArchived { get; set; }
       }

       // Delete a player report
       // Used by Banlist page and PlayerInspect page
       [HttpDelete("DeleteReport/{reportId}")]
       public async Task<IActionResult> DeleteReport(int reportId)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Get the report
               var report = await dbContext.PlayerReports.FindAsync(reportId);
               if (report == null)
                   return NotFound(new { error = "Report not found" });

               // Check if user has access to the server
               bool access = await User.HasServerAccess(dbContext, report.ServerId);
               if (!access)
                   return Unauthorized();

               // Delete the report
               dbContext.PlayerReports.Remove(report);
               await dbContext.SaveChangesAsync();

               return Ok(new { message = "Report deleted successfully" });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error deleting report");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // Toggle global ban status for a player ban
       // Used by Banlist page
       [HttpPost("ToggleGlobalBan/{banId}")]
       public async Task<IActionResult> ToggleGlobalBan(int banId, [FromBody] ToggleGlobalBanRequest request)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Get the ban record
               var ban = await dbContext.PlayerBans.FirstOrDefaultAsync(b => b.Id == banId);
               if (ban == null)
                   return NotFound("Ban not found");

               // Verify user has access to this server (skip for global bans with ServerId = -1)
               if (ban.ServerId != -1)
               {
                   bool access = await User.HasServerAccess(dbContext, ban.ServerId);
                   if (!access)
                       return Unauthorized();
               }

               // If toggling OFF a global ban, unban from all servers
               if (ban.IsGlobalBan && !request.IsGlobalBan)
               {
                   _logger.LogInformation("[GLOBAL UNBAN] Toggling off global ban for {SteamId}. Unbanning from all servers...", ban.SteamId);

                   // Get current user email for audit trail
                   var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "System";
                   var now = DateTime.UtcNow;

                   // Find all bans for this player with the note indicating they're from a global ban
                   var allBansForPlayer = await dbContext.PlayerBans
                       .Where(b => b.SteamId == ban.SteamId)
                       .ToListAsync();

                   foreach (var playerBan in allBansForPlayer)
                   {
                       // Update ban history to mark as inactive
                       var historyRecords = await dbContext.PlayerBanHistories
                           .Where(h => h.SteamId == playerBan.SteamId && h.ServerId == playerBan.ServerId && h.IsActive)
                           .ToListAsync();

                       foreach (var history in historyRecords)
                       {
                           history.IsActive = false;
                           history.LiftedAt = now;
                           history.LiftedBy = userEmail;
                           history.LiftReason = "Global ban toggled off via panel";
                           history.UpdatedAt = now;
                       }

                       // Send unban command to each server
                       try
                       {
                           string unbanCommand = $"unban {playerBan.SteamId}";
                           _rconService.SendRconCommand(unbanCommand, playerBan.ServerId);
                           _logger.LogInformation("[GLOBAL UNBAN] Sent unban command for {SteamId} on server {ServerId}", ban.SteamId, playerBan.ServerId);
                       }
                       catch (Exception ex)
                       {
                           _logger.LogWarning(ex, "[GLOBAL UNBAN] Could not send unban command for server {ServerId}", playerBan.ServerId);
                       }

                       // Remove the ban record
                       dbContext.PlayerBans.Remove(playerBan);
                   }

                   await dbContext.SaveChangesAsync();
                   _logger.LogInformation("[GLOBAL UNBAN] Removed all bans for {SteamId} from database", ban.SteamId);

                   return Ok(new { message = $"Player {ban.SteamId} has been unbanned from all servers" });
               }

               // Toggle the global ban status
               ban.IsGlobalBan = request.IsGlobalBan;
               ban.UpdatedAt = DateTime.UtcNow;

               dbContext.PlayerBans.Update(ban);
               await dbContext.SaveChangesAsync();

               return Ok(new { message = $"Ban global status updated to {request.IsGlobalBan}" });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "[GLOBAL UNBAN] Error toggling global ban");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // Delete a ban record (unban player on server or remove global ban)
       // Used by Banlist page
       [HttpPost("DeleteBan/{banId}")]
       public async Task<IActionResult> DeleteBan(int banId, [FromBody] DeleteBanRequest? request = null)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               // Get the ban record
               var ban = await dbContext.PlayerBans.FirstOrDefaultAsync(b => b.Id == banId);
               if (ban == null)
                   return NotFound("Ban not found");

               // For global bans, we don't need to check server access (ServerId = -1)
               if (ban.ServerId != -1)
               {
                   // Verify user has access to this server
                   bool access = await User.HasServerAccess(dbContext, ban.ServerId);
                   if (!access)
                       return Unauthorized();
               }

               // Get current user email for audit trail
               var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "System";
               var now = DateTime.UtcNow;

               // If this is a global ban, unban from all servers
               if (ban.IsGlobalBan && ban.ServerId == -1)
               {
                   _logger.LogInformation("[GLOBAL UNBAN] Removing global ban for {SteamId}", ban.SteamId);

                   // Find all server-specific bans created by this global ban
                   var relatedServerBans = await dbContext.PlayerBans
                       .Where(b => b.SteamId == ban.SteamId && b.ServerId != -1)
                       .ToListAsync();

                   foreach (var serverBan in relatedServerBans)
                   {
                       // Update ban history to mark as inactive
                       var historyRecords = await dbContext.PlayerBanHistories
                           .Where(h => h.SteamId == serverBan.SteamId && h.ServerId == serverBan.ServerId && h.IsActive)
                           .ToListAsync();

                       foreach (var history in historyRecords)
                       {
                           history.IsActive = false;
                           history.LiftedAt = now;
                           history.LiftedBy = userEmail;
                           history.LiftReason = "Global ban deleted via panel";
                           history.UpdatedAt = now;
                       }

                       // Send unban command to each server
                       try
                       {
                           string unbanCommand = $"unban {serverBan.SteamId}";
                           _rconService.SendRconCommand(unbanCommand, serverBan.ServerId);
                           _logger.LogInformation("[GLOBAL UNBAN] Sent unban command for {SteamId} on server {ServerId}", ban.SteamId, serverBan.ServerId);
                       }
                       catch (Exception ex)
                       {
                           _logger.LogWarning(ex, "[GLOBAL UNBAN] Could not send unban command to server {ServerId}", serverBan.ServerId);
                       }

                       // Remove the server-specific ban record
                       dbContext.PlayerBans.Remove(serverBan);
                   }

                   // Update global ban history record
                   var globalHistoryRecords = await dbContext.PlayerBanHistories
                       .Where(h => h.PlayerBanId == ban.Id && h.IsActive)
                       .ToListAsync();

                   foreach (var history in globalHistoryRecords)
                   {
                       history.IsActive = false;
                       history.LiftedAt = now;
                       history.LiftedBy = userEmail;
                       history.LiftReason = "Global ban deleted via panel";
                       history.UpdatedAt = now;
                   }

                   // Remove the global ban record
                   dbContext.PlayerBans.Remove(ban);
                   await dbContext.SaveChangesAsync();
                   _logger.LogInformation("[GLOBAL UNBAN] Removed global ban for {SteamId} from database", ban.SteamId);

                   return Ok(new { message = "Global ban removed successfully from all servers" });
               }
               else
               {
                   // Handle server-specific ban deletion
                   // First check if there's a global ban for this player
                   var globalBan = await dbContext.PlayerBans
                       .FirstOrDefaultAsync(b => b.SteamId == ban.SteamId && b.ServerId == -1 && b.IsGlobalBan);

                   if (globalBan != null)
                   {
                       return BadRequest(new { error = "Cannot delete this ban because the player has an active global ban. Please delete the global ban first." });
                   }

                   // Update ban history to mark as inactive
                   var historyRecords = await dbContext.PlayerBanHistories
                       .Where(h => h.PlayerBanId == ban.Id && h.IsActive)
                       .ToListAsync();

                   foreach (var history in historyRecords)
                   {
                       history.IsActive = false;
                       history.LiftedAt = now;
                       history.LiftedBy = userEmail;
                       history.LiftReason = "Ban deleted via panel";
                       history.UpdatedAt = now;
                   }

                   // Send unban command to the Rust server
                   try
                   {
                       string unbanCommand = $"unban {ban.SteamId}";
                       _rconService.SendRconCommand(unbanCommand, ban.ServerId);
                       _logger.LogInformation("[UNBAN] Sent unban command for SteamID {SteamId} on server {ServerId}", ban.SteamId, ban.ServerId);
                   }
                   catch (Exception ex)
                   {
                       // Log but don't fail if server is offline - unban is still tracked in database
                       _logger.LogWarning(ex, "[UNBAN] Could not send unban command to server");
                   }

                   // Remove the ban record from database
                   dbContext.PlayerBans.Remove(ban);
                   await dbContext.SaveChangesAsync();
                   _logger.LogInformation("[UNBAN] Removed ban record for SteamID {SteamId} from database", ban.SteamId);

                   return Ok(new { message = "Player unbanned successfully on server and database updated" });
               }
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "[UNBAN] Error unbanning player");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       [HttpPut("UpdateBan/{banId}")]
       public async Task<IActionResult> UpdateBan(int banId, [FromBody] UpdateBanRequest request)
       {
           try
           {
               var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               var ban = await dbContext.PlayerBans.FirstOrDefaultAsync(b => b.Id == banId);
               if (ban == null)
                   return NotFound("Ban not found");

               // Verify user has access
               if (ban.ServerId != -1)
               {
                   bool access = await User.HasServerAccess(dbContext, ban.ServerId);
                   if (!access)
                       return Unauthorized();
               }

               var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "System";
               var now = DateTime.UtcNow;

               // Calculate new expiry
               long? newExpiry = null;
               if (request.DurationHours > 0)
               {
                   var expiryDate = now.AddHours(request.DurationHours);
                   newExpiry = ConvertDateTimeToUnixTimestamp(expiryDate);
               }

               // Update ban record (reason cannot be changed — must unban and re-ban)
               ban.InternalNote = request.InternalNote;
               ban.Expiry = newExpiry;
               ban.UpdatedAt = now;

               // Handle global ban toggle
               bool wasGlobal = ban.IsGlobalBan;
               bool nowGlobal = request.IsGlobalBan;

               if (!wasGlobal && nowGlobal)
               {
                   // Promote to global ban: create copies on all servers
                   ban.IsGlobalBan = true;
                   ban.ServerId = -1;

                   var allServers = await dbContext.RconServers.ToListAsync();
                   foreach (var server in allServers)
                   {
                       var existingServerBan = await dbContext.PlayerBans
                           .FirstOrDefaultAsync(b => b.SteamId == ban.SteamId && b.ServerId == server.Id);

                       if (existingServerBan == null)
                       {
                           var serverBan = new PlayerBan
                           {
                               ServerId = server.Id,
                               SteamId = ban.SteamId,
                               Username = ban.Username,
                               Notes = $"[GLOBAL BAN ENFORCEMENT] {ban.Notes}",
                               Expiry = newExpiry,
                               IsGlobalBan = false,
                               CreatedAt = now,
                               UpdatedAt = now
                           };
                           dbContext.PlayerBans.Add(serverBan);
                       }
                       else
                       {
                           existingServerBan.Notes = $"[GLOBAL BAN ENFORCEMENT] {ban.Notes}";
                           existingServerBan.Expiry = newExpiry;
                           existingServerBan.UpdatedAt = now;
                       }

                       // Send updated ban command to server
                       string durationSuffix = request.DurationHours > 0 ? $" {request.DurationHours}" : "";
                       string banCommand = $"banid {ban.SteamId} \"{ban.Username}\" \"{ban.Notes}\"{durationSuffix}";
                       _rconService.SendRconCommand(banCommand, server.Id);
                   }
               }
               else if (wasGlobal && !nowGlobal)
               {
                   // Demote from global: remove copies from other servers, keep on active server
                   ban.IsGlobalBan = false;
                   ban.ServerId = request.ServerId;

                   var otherBans = await dbContext.PlayerBans
                       .Where(b => b.SteamId == ban.SteamId && b.Id != ban.Id)
                       .ToListAsync();

                   foreach (var otherBan in otherBans)
                   {
                       // Unban on other servers
                       _rconService.SendRconCommand($"unban {ban.SteamId}", otherBan.ServerId);
                       dbContext.PlayerBans.Remove(otherBan);

                       // Update history
                       var historyRecords = await dbContext.PlayerBanHistories
                           .Where(h => h.SteamId == ban.SteamId && h.ServerId == otherBan.ServerId && h.IsActive)
                           .ToListAsync();
                       foreach (var h in historyRecords)
                       {
                           h.IsActive = false;
                           h.LiftedAt = now;
                           h.LiftedBy = userEmail;
                           h.LiftReason = "Global ban demoted to server-specific";
                           h.UpdatedAt = now;
                       }
                   }

                   // Update ban on the target server with new duration
                   {
                       string durationSuffix = request.DurationHours > 0 ? $" {request.DurationHours}" : "";
                       string banCommand = $"banid {ban.SteamId} \"{ban.Username}\" \"{ban.Notes}\"{durationSuffix}";
                       _rconService.SendRconCommand(banCommand, request.ServerId);
                   }
               }
               else
               {
                   // Same global status — just update duration/reason on applicable servers
                   if (ban.IsGlobalBan)
                   {
                       // Update all server copies
                       var serverBans = await dbContext.PlayerBans
                           .Where(b => b.SteamId == ban.SteamId && b.Id != ban.Id)
                           .ToListAsync();

                       foreach (var serverBan in serverBans)
                       {
                           serverBan.Notes = $"[GLOBAL BAN ENFORCEMENT] {ban.Notes}";
                           serverBan.Expiry = newExpiry;
                           serverBan.UpdatedAt = now;

                           string durationSuffix = request.DurationHours > 0 ? $" {request.DurationHours}" : "";
                           string banCommand = $"banid {ban.SteamId} \"{ban.Username}\" \"{ban.Notes}\"{durationSuffix}";
                           _rconService.SendRconCommand(banCommand, serverBan.ServerId);
                       }
                   }
                   else
                   {
                       // Update on the single server
                       string durationSuffix = request.DurationHours > 0 ? $" {request.DurationHours}" : "";
                       string banCommand = $"banid {ban.SteamId} \"{ban.Username}\" \"{ban.Notes}\"{durationSuffix}";
                       _rconService.SendRconCommand(banCommand, ban.ServerId);
                   }
               }

               // Update ban history
               var activeHistory = await dbContext.PlayerBanHistories
                   .Where(h => h.SteamId == ban.SteamId && h.ServerId == (wasGlobal ? -1 : ban.ServerId) && h.IsActive)
                   .FirstOrDefaultAsync();

               if (activeHistory != null)
               {
                   activeHistory.DurationHours = request.DurationHours;
                   activeHistory.ExpiryDate = request.DurationHours > 0 ? now.AddHours(request.DurationHours) : null;
                   activeHistory.IsGlobalBan = request.IsGlobalBan;
                   activeHistory.UpdatedAt = now;
               }

               dbContext.PlayerBans.Update(ban);
               await dbContext.SaveChangesAsync();

               return Ok(new { message = "Ban updated successfully" });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "[UPDATE BAN] Error");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       public class UpdateBanRequest
       {
           public long DurationHours { get; set; }
           public bool IsGlobalBan { get; set; }
           public int ServerId { get; set; }
           public string? InternalNote { get; set; }
       }

       // DTO for toggle global ban request
       public class ToggleGlobalBanRequest
       {
           public bool IsGlobalBan { get; set; }
       }

       public class DeleteBanRequest { }

       private static PlayerBan_BanDTO MapBanToDto(PlayerBan b, Dictionary<int, string>? serverNames = null)
       {
           return new PlayerBan_BanDTO
           {
               Id = b.Id,
               ServerId = b.ServerId,
               ServerName = serverNames != null && serverNames.TryGetValue(b.ServerId, out var name) ? name : null,
               Group = b.Group,
               SteamId = b.SteamId,
               Username = b.Username,
               Notes = b.Notes,
               InternalNote = b.InternalNote,
               Expiry = b.Expiry,
               IsGlobalBan = b.IsGlobalBan,
               IsLifted = b.IsLifted,
               LiftedAt = b.LiftedAt,
               LiftedBy = b.LiftedBy,
               CreatedAt = b.CreatedAt,
               UpdatedAt = b.UpdatedAt
           };
       }

       // Helper method to convert DateTime to Unix timestamp
       private long ConvertDateTimeToUnixTimestamp(DateTime dateTime)
       {
           var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
           var unixTimestamp = (long)(dateTime - unixEpoch).TotalSeconds;
           return unixTimestamp;
       }

       /// <summary>
       /// Gets all Rust items from the database for the give item dialog
       /// </summary>
       [HttpGet("GetRustItems")]
       public async Task<IActionResult> GetRustItems()
       {
           try
           {
               using var scope = _scopeFactory.CreateScope();
               var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               var items = await dbContext.RustItems
                   .OrderBy(i => i.DisplayName)
                   .ToListAsync();

               return Ok(items);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "[GET RUST ITEMS] Error fetching items");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       /// <summary>
       /// Gives an item to a player using RCON command
       /// </summary>
       [HttpPost("GiveItem")]
       public async Task<IActionResult> GiveItem([FromBody] GiveItemRequest request)
       {
           try
           {
               if (string.IsNullOrWhiteSpace(request.SteamId))
                   return BadRequest(new { error = "SteamId is required" });

               if (string.IsNullOrWhiteSpace(request.ShortName))
                   return BadRequest(new { error = "Item shortname is required" });

               if (request.Quantity < 1)
                   return BadRequest(new { error = "Quantity must be at least 1" });

               // Build the RCON command: inventory.giveto <steamid> <shortname> <quantity>
               var command = $"inventory.giveto {request.SteamId} {request.ShortName} {request.Quantity}";

               _logger.LogInformation("[GIVE ITEM] Executing command: {Command} for player {PlayerName}", command, request.PlayerName);

               // Execute the RCON command
               var response = await _rconService.ExecuteCommandWithResponse(command, request.ServerId);

               _logger.LogDebug("[GIVE ITEM] Command response: {Response}", response);

               return Ok(new {
                   success = true,
                   message = $"Gave {request.Quantity}x {request.ItemDisplayName} to {request.PlayerName}",
                   response = response
               });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "[GIVE ITEM] Error giving item");
               return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
           }
       }

       // DTO for give item request
       public class GiveItemRequest
       {
           public int ServerId { get; set; }
           public string SteamId { get; set; } = string.Empty;
           public string PlayerName { get; set; } = string.Empty;
           public string ShortName { get; set; } = string.Empty;
           public string ItemDisplayName { get; set; } = string.Empty;
           public int Quantity { get; set; } = 1;
       }


    }
}