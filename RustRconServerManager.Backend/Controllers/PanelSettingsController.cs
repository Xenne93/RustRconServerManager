using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Shared.Scheduler;
using System.Security.Claims;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PanelSettingsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PanelSettingsController> _logger;
    private readonly AutoUpdateFlagFileService _autoUpdateFlagFileService;

    public PanelSettingsController(AppDbContext dbContext, ILogger<PanelSettingsController> logger, AutoUpdateFlagFileService autoUpdateFlagFileService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _autoUpdateFlagFileService = autoUpdateFlagFileService;
    }

    /// <summary>
    /// Gets the current user's SystemProfile panel settings
    /// </summary>
    [HttpGet("my-settings")]
    public async Task<ActionResult<PanelSettingsDto>> GetMySettings()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var applicationUser = await _dbContext.Users.FindAsync(userId);
            if (applicationUser == null)
            {
                return NotFound("User not found");
            }

            var panelSettings = await _dbContext.PanelSettings
                .FirstOrDefaultAsync(ps => ps.SystemProfileId == applicationUser.SystemProfileId);

            if (panelSettings == null)
            {
                // Create default settings if they don't exist
                panelSettings = new PanelSettings
                {
                    SystemProfileId = applicationUser.SystemProfileId,
                    TimezoneId = "UTC",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.PanelSettings.Add(panelSettings);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"[PanelSettingsController] Created default panel settings for SystemProfile {applicationUser.SystemProfileId}");
            }

            return Ok(MapToDto(panelSettings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error getting panel settings");
            return StatusCode(500, new { error = "Error retrieving panel settings" });
        }
    }

    /// <summary>
    /// Gets database statistics for the current user's servers
    /// </summary>
    [HttpGet("database-stats")]
    public async Task<ActionResult<DatabaseStatsDto>> GetDatabaseStats()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var applicationUser = await _dbContext.Users.FindAsync(userId);
            if (applicationUser == null) return NotFound("User not found");

            var serverIds = await _dbContext.RconServers
                .Where(s => s.SystemProfileId == applicationUser.SystemProfileId)
                .Select(s => s.Id)
                .ToListAsync();

            var entries = new List<DatabaseStatEntry>();

            // Chat Messages
            var chatCount = await _dbContext.ChatMessages.Where(c => serverIds.Contains(c.ServerId)).LongCountAsync();
            var chatOldest = await _dbContext.ChatMessages.Where(c => serverIds.Contains(c.ServerId)).OrderBy(c => c.Timestamp).Select(c => (DateTime?)c.Timestamp).FirstOrDefaultAsync();
            entries.Add(new DatabaseStatEntry { Category = "ChatMessages", Icon = "fas fa-comments", Count = chatCount, OldestRecord = chatOldest, CanPurge = true });

            // Console Logs
            var logCount = await _dbContext.RconLogEntries.Where(e => serverIds.Contains(e.ServerId)).LongCountAsync();
            var logOldest = await _dbContext.RconLogEntries.Where(e => serverIds.Contains(e.ServerId)).OrderBy(e => e.CreatedAt).Select(e => (DateTime?)e.CreatedAt).FirstOrDefaultAsync();
            entries.Add(new DatabaseStatEntry { Category = "ConsoleLogs", Icon = "fas fa-terminal", Count = logCount, OldestRecord = logOldest, CanPurge = true });

            // Stats History
            var statsCount = await _dbContext.StatsHistories.Where(s => serverIds.Contains(s.ServerId)).LongCountAsync();
            var statsOldest = await _dbContext.StatsHistories.Where(s => serverIds.Contains(s.ServerId)).OrderBy(s => s.CreatedAt).Select(s => (DateTime?)s.CreatedAt).FirstOrDefaultAsync();
            entries.Add(new DatabaseStatEntry { Category = "StatsHistory", Icon = "fas fa-chart-line", Count = statsCount, OldestRecord = statsOldest, CanPurge = true });

            // Aggregated Stats
            var aggCount = await _dbContext.AggregatedStats.Where(a => serverIds.Contains(a.ServerId)).LongCountAsync();
            var aggOldest = await _dbContext.AggregatedStats.Where(a => serverIds.Contains(a.ServerId)).OrderBy(a => a.Timestamp).Select(a => (DateTime?)a.Timestamp).FirstOrDefaultAsync();
            entries.Add(new DatabaseStatEntry { Category = "AggregatedStats", Icon = "fas fa-chart-bar", Count = aggCount, OldestRecord = aggOldest, CanPurge = true });

            // Player Kill Logs
            var killCount = await _dbContext.PlayerKillLogs.Where(k => serverIds.Contains(k.ServerId)).LongCountAsync();
            var killOldest = await _dbContext.PlayerKillLogs.Where(k => serverIds.Contains(k.ServerId)).OrderBy(k => k.CreatedAt).Select(k => k.CreatedAt).FirstOrDefaultAsync();
            entries.Add(new DatabaseStatEntry { Category = "KillLogs", Icon = "fas fa-skull-crossbones", Count = killCount, OldestRecord = killOldest, CanPurge = true });

            // Players (not purgeable)
            var playerCount = await _dbContext.SteamPlayers.Where(p => serverIds.Contains(p.ServerId)).LongCountAsync();
            var playerOldest = await _dbContext.SteamPlayers.Where(p => serverIds.Contains(p.ServerId)).OrderBy(p => p.FirstSeen).Select(p => (DateTime?)p.FirstSeen).FirstOrDefaultAsync();
            entries.Add(new DatabaseStatEntry { Category = "Players", Icon = "fas fa-users", Count = playerCount, OldestRecord = playerOldest, CanPurge = false });

            return Ok(new DatabaseStatsDto { Entries = entries });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error getting database stats");
            return StatusCode(500, new { error = "Error retrieving database statistics" });
        }
    }

    /// <summary>
    /// Purges old data for a specific category
    /// </summary>
    [HttpDelete("purge-data")]
    public async Task<IActionResult> PurgeData([FromBody] PurgeDataRequestDto request)
    {
        try
        {
            if (request.OlderThanDays < 1)
                return BadRequest(new { error = "OlderThanDays must be at least 1" });

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var applicationUser = await _dbContext.Users.FindAsync(userId);
            if (applicationUser == null) return NotFound("User not found");

            var serverIds = await _dbContext.RconServers
                .Where(s => s.SystemProfileId == applicationUser.SystemProfileId)
                .Select(s => s.Id)
                .ToListAsync();

            var cutoff = DateTime.UtcNow.AddDays(-request.OlderThanDays);
            int deleted = 0;

            switch (request.Category)
            {
                case "ChatMessages":
                    deleted = await _dbContext.ChatMessages
                        .Where(c => serverIds.Contains(c.ServerId) && c.Timestamp < cutoff)
                        .ExecuteDeleteAsync();
                    break;

                case "ConsoleLogs":
                    deleted = await _dbContext.RconLogEntries
                        .Where(e => serverIds.Contains(e.ServerId) && e.CreatedAt < cutoff)
                        .ExecuteDeleteAsync();
                    break;

                case "StatsHistory":
                    deleted = await _dbContext.StatsHistories
                        .Where(s => serverIds.Contains(s.ServerId) && s.CreatedAt < cutoff)
                        .ExecuteDeleteAsync();
                    break;

                case "AggregatedStats":
                    deleted = await _dbContext.AggregatedStats
                        .Where(a => serverIds.Contains(a.ServerId) && a.Timestamp < cutoff)
                        .ExecuteDeleteAsync();
                    break;

                case "KillLogs":
                    deleted = await _dbContext.PlayerKillLogs
                        .Where(k => serverIds.Contains(k.ServerId) && k.CreatedAt < cutoff)
                        .ExecuteDeleteAsync();
                    break;

                default:
                    return BadRequest(new { error = $"Unknown or non-purgeable category: {request.Category}" });
            }

            _logger.LogInformation("[PanelSettingsController] User {UserId} purged {Count} records from {Category} (older than {Days} days)",
                userId, deleted, request.Category, request.OlderThanDays);

            return Ok(new { deleted, category = request.Category });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error purging data");
            return StatusCode(500, new { error = "Error purging data" });
        }
    }

    /// <summary>
    /// Updates the minimum log level. Applies immediately (no restart needed) via
    /// LogLevelState, and persists so it survives an app restart.
    /// </summary>
    [HttpPut("log-level")]
    public async Task<ActionResult<PanelSettingsDto>> SetLogLevel([FromBody] SetLogLevelDto dto)
    {
        try
        {
            if (!LogLevelState.TryParse(dto.MinimumLogLevel, out var parsedLevel))
            {
                return BadRequest(new { error = $"Invalid log level: {dto.MinimumLogLevel}" });
            }

            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var panelSettings = await _dbContext.PanelSettings
                .FirstOrDefaultAsync(ps => ps.SystemProfileId == currentUser.SystemProfileId);

            if (panelSettings == null)
                return NotFound("Panel settings not found");

            panelSettings.MinimumLogLevel = parsedLevel.ToString();
            panelSettings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            LogLevelState.Minimum = parsedLevel;

            _logger.LogInformation("[PanelSettingsController] Minimum log level changed to {Level} by {User}",
                parsedLevel, currentUser.Email);

            return Ok(MapToDto(panelSettings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error setting log level");
            return StatusCode(500, new { error = "Error updating log level" });
        }
    }

    /// <summary>
    /// Updates whether the app should automatically download and install a newer release
    /// on startup. Persists to the database and mirrors the value to a flag file that the
    /// Docker/standalone update-check scripts read, since they run before the app process
    /// (and its database connection) exists.
    /// </summary>
    [HttpPut("auto-update")]
    public async Task<ActionResult<PanelSettingsDto>> SetAutoUpdate([FromBody] SetAutoUpdateDto dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var panelSettings = await _dbContext.PanelSettings
                .FirstOrDefaultAsync(ps => ps.SystemProfileId == currentUser.SystemProfileId);

            if (panelSettings == null)
                return NotFound("Panel settings not found");

            panelSettings.AutoUpdateEnabled = dto.AutoUpdateEnabled;
            panelSettings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _autoUpdateFlagFileService.Write(dto.AutoUpdateEnabled);

            _logger.LogInformation("[PanelSettingsController] Auto-update {State} by {User}",
                dto.AutoUpdateEnabled ? "enabled" : "disabled", currentUser.Email);

            return Ok(MapToDto(panelSettings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error setting auto-update");
            return StatusCode(500, new { error = "Error updating auto-update setting" });
        }
    }

    /// <summary>
    /// Toggles developer mode, which gates the fake VAC-ban override list below - useful
    /// for testing the VAC-ban protection rules without needing a real banned account.
    /// </summary>
    [HttpPut("developer-mode")]
    public async Task<ActionResult<PanelSettingsDto>> SetDeveloperMode([FromBody] SetDeveloperModeDto dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var panelSettings = await _dbContext.PanelSettings
                .FirstOrDefaultAsync(ps => ps.SystemProfileId == currentUser.SystemProfileId);

            if (panelSettings == null)
                return NotFound("Panel settings not found");

            panelSettings.DeveloperModeEnabled = dto.DeveloperModeEnabled;
            panelSettings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[PanelSettingsController] Developer mode {State} by {User}",
                dto.DeveloperModeEnabled ? "enabled" : "disabled", currentUser.Email);

            return Ok(MapToDto(panelSettings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error setting developer mode");
            return StatusCode(500, new { error = "Error updating developer mode" });
        }
    }

    /// <summary>
    /// Updates whether this installation sends anonymous usage statistics (install
    /// check-ins, server/player/user counts) to the developer.
    /// </summary>
    [HttpPut("analytics")]
    public async Task<ActionResult<PanelSettingsDto>> SetAnalytics([FromBody] SetAnalyticsDto dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var panelSettings = await _dbContext.PanelSettings
                .FirstOrDefaultAsync(ps => ps.SystemProfileId == currentUser.SystemProfileId);

            if (panelSettings == null)
                return NotFound("Panel settings not found");

            panelSettings.AnalyticsEnabled = dto.AnalyticsEnabled;
            panelSettings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[PanelSettingsController] Anonymous analytics {State} by {User}",
                dto.AnalyticsEnabled ? "enabled" : "disabled", currentUser.Email);

            return Ok(MapToDto(panelSettings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error setting analytics");
            return StatusCode(500, new { error = "Error updating analytics setting" });
        }
    }

    /// <summary>
    /// Public, unauthenticated check for whether this installation has opted in to
    /// anonymous analytics - the frontend calls this on every page load (before login is
    /// even possible) to decide whether to load the analytics script, so toggling the
    /// setting takes effect immediately without a rebuild or restart. Self-hosted installs
    /// have exactly one SystemProfile/PanelSettings row, so the first one is authoritative.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("analytics-status")]
    public async Task<ActionResult<AnalyticsStatusDto>> GetAnalyticsStatus()
    {
        var enabled = await _dbContext.PanelSettings.Select(ps => ps.AnalyticsEnabled).FirstOrDefaultAsync();
        return Ok(new AnalyticsStatusDto { Enabled = enabled });
    }

    /// <summary>
    /// Lists all developer-mode fake VAC-ban overrides.
    /// </summary>
    [HttpGet("developer/vac-overrides")]
    public async Task<ActionResult<List<DeveloperVacBanOverrideDto>>> GetVacBanOverrides()
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var overrides = await _dbContext.DeveloperVacBanOverrides
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new DeveloperVacBanOverrideDto
                {
                    Id = o.Id,
                    SteamId = o.SteamId,
                    VACBanned = o.VACBanned,
                    NumberOfVACBans = o.NumberOfVACBans,
                    DaysSinceLastBan = o.DaysSinceLastBan,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            return Ok(overrides);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error getting VAC-ban overrides");
            return StatusCode(500, new { error = "Error retrieving VAC-ban overrides" });
        }
    }

    /// <summary>
    /// Creates or updates (by SteamID) a fake VAC-ban override.
    /// </summary>
    [HttpPut("developer/vac-overrides")]
    public async Task<ActionResult<DeveloperVacBanOverrideDto>> UpsertVacBanOverride([FromBody] UpsertDeveloperVacBanOverrideDto dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var steamId = dto.SteamId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(steamId))
                return BadRequest(new { error = "SteamID is required" });

            if (dto.NumberOfVACBans < 0 || dto.DaysSinceLastBan < 0)
                return BadRequest(new { error = "NumberOfVACBans and DaysSinceLastBan cannot be negative" });

            var existing = await _dbContext.DeveloperVacBanOverrides
                .FirstOrDefaultAsync(o => o.SteamId == steamId);

            if (existing == null)
            {
                existing = new DeveloperVacBanOverride
                {
                    SteamId = steamId,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.DeveloperVacBanOverrides.Add(existing);
            }

            existing.VACBanned = dto.VACBanned;
            existing.NumberOfVACBans = dto.NumberOfVACBans;
            existing.DaysSinceLastBan = dto.DaysSinceLastBan;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[PanelSettingsController] VAC-ban override upserted for SteamID {SteamId} by {User}",
                steamId, currentUser.Email);

            return Ok(new DeveloperVacBanOverrideDto
            {
                Id = existing.Id,
                SteamId = existing.SteamId,
                VACBanned = existing.VACBanned,
                NumberOfVACBans = existing.NumberOfVACBans,
                DaysSinceLastBan = existing.DaysSinceLastBan,
                CreatedAt = existing.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error upserting VAC-ban override");
            return StatusCode(500, new { error = "Error saving VAC-ban override" });
        }
    }

    /// <summary>
    /// Removes a fake VAC-ban override by SteamID.
    /// </summary>
    [HttpDelete("developer/vac-overrides/{steamId}")]
    public async Task<IActionResult> DeleteVacBanOverride(string steamId)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var deleted = await _dbContext.DeveloperVacBanOverrides
                .Where(o => o.SteamId == steamId)
                .ExecuteDeleteAsync();

            if (deleted == 0)
                return NotFound("Override not found");

            _logger.LogInformation("[PanelSettingsController] VAC-ban override removed for SteamID {SteamId} by {User}",
                steamId, currentUser.Email);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelSettingsController] Error deleting VAC-ban override");
            return StatusCode(500, new { error = "Error deleting VAC-ban override" });
        }
    }

    private PanelSettingsDto MapToDto(PanelSettings panelSettings)
    {
        return new PanelSettingsDto
        {
            Id = panelSettings.Id,
            SystemProfileId = panelSettings.SystemProfileId,
            TimezoneId = panelSettings.TimezoneId,
            MinimumLogLevel = panelSettings.MinimumLogLevel,
            AutoUpdateEnabled = panelSettings.AutoUpdateEnabled,
            DeveloperModeEnabled = panelSettings.DeveloperModeEnabled,
            AnalyticsEnabled = panelSettings.AnalyticsEnabled,
            CreatedAt = panelSettings.CreatedAt,
            UpdatedAt = panelSettings.UpdatedAt
        };
    }
}
