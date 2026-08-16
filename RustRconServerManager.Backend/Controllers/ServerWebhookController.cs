using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.ServerWebhooks;

namespace RustRconServerManager.Backend.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ServerWebhookController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ServerWebhookController> _logger;
    private readonly IDiscordWebhookService _discordWebhookService;

    public ServerWebhookController(
        AppDbContext dbContext,
        ILogger<ServerWebhookController> logger,
        IDiscordWebhookService discordWebhookService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _discordWebhookService = discordWebhookService;
    }

    /// <summary>
    /// Get webhook settings for the selected server
    /// </summary>
    [HttpGet("GetSettings")]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            ApplicationUser user = await User.GetUser(_dbContext);
            SystemProfile profile = await User.GetUserSystemProfile(_dbContext);

            if (profile.Id != user.SystemProfileId)
            {
                return Unauthorized("System profile mismatch");
            }

            int serverId = user.SelectedServerId ?? 0;
            if (serverId <= 0)
            {
                return BadRequest("No server selected");
            }

            // Check if user has access to this server
            bool hasAccess = await User.HasServerAccess(_dbContext, serverId);
            if (!hasAccess)
            {
                return Unauthorized("User does not have access to this server");
            }

            // Get or create settings for this server
            var settings = await _dbContext.ServerWebhookSettings
                .FirstOrDefaultAsync(s => s.ServerId == serverId);

            if (settings == null)
            {
                // Create default settings
                settings = new ServerWebhookSettings
                {
                    ServerId = serverId,
                    EnablePlayerConnectWebhook = false,
                    PlayerConnectWebhookUrl = string.Empty,
                    EnablePlayerDisconnectWebhook = false,
                    PlayerDisconnectWebhookUrl = string.Empty,
                    EnablePlayerKillWebhook = false,
                    PlayerKillWebhookUrl = string.Empty,
                    EnablePlayerBanWebhook = false,
                    PlayerBanWebhookUrl = string.Empty,
                    EnablePlayerKickWebhook = false,
                    PlayerKickWebhookUrl = string.Empty,
                    EnablePlayerReportWebhook = false,
                    PlayerReportWebhookUrl = string.Empty,
                    EnableServerOfflineWebhook = false,
                    ServerOfflineWebhookUrl = string.Empty,
                    EnableServerOnlineWebhook = false,
                    ServerOnlineWebhookUrl = string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.ServerWebhookSettings.Add(settings);
                await _dbContext.SaveChangesAsync();
            }

            var response = new ServerWebhooks_ResponseDTO
            {
                Message = "Settings loaded successfully",
                Settings = new ServerWebhookSettingsDTO
                {
                    Id = settings.Id,
                    ServerId = settings.ServerId,
                    EnablePlayerConnectWebhook = settings.EnablePlayerConnectWebhook,
                    PlayerConnectWebhookUrl = settings.PlayerConnectWebhookUrl,
                    PlayerConnectFormat = settings.PlayerConnectFormat,
                    PlayerConnectTextContent = settings.PlayerConnectTextContent,
                    PlayerConnectCustomContent = settings.PlayerConnectCustomContent,
                    EnablePlayerDisconnectWebhook = settings.EnablePlayerDisconnectWebhook,
                    PlayerDisconnectWebhookUrl = settings.PlayerDisconnectWebhookUrl,
                    PlayerDisconnectFormat = settings.PlayerDisconnectFormat,
                    PlayerDisconnectTextContent = settings.PlayerDisconnectTextContent,
                    PlayerDisconnectCustomContent = settings.PlayerDisconnectCustomContent,
                    EnablePlayerKillWebhook = settings.EnablePlayerKillWebhook,
                    PlayerKillWebhookUrl = settings.PlayerKillWebhookUrl,
                    PlayerKillFormat = settings.PlayerKillFormat,
                    PlayerKillTextContent = settings.PlayerKillTextContent,
                    PlayerKillCustomContent = settings.PlayerKillCustomContent,
                    EnablePlayerBanWebhook = settings.EnablePlayerBanWebhook,
                    PlayerBanWebhookUrl = settings.PlayerBanWebhookUrl,
                    PlayerBanFormat = settings.PlayerBanFormat,
                    PlayerBanTextContent = settings.PlayerBanTextContent,
                    PlayerBanCustomContent = settings.PlayerBanCustomContent,
                    EnablePlayerKickWebhook = settings.EnablePlayerKickWebhook,
                    PlayerKickWebhookUrl = settings.PlayerKickWebhookUrl,
                    PlayerKickFormat = settings.PlayerKickFormat,
                    PlayerKickTextContent = settings.PlayerKickTextContent,
                    PlayerKickCustomContent = settings.PlayerKickCustomContent,
                    EnablePlayerReportWebhook = settings.EnablePlayerReportWebhook,
                    PlayerReportWebhookUrl = settings.PlayerReportWebhookUrl,
                    PlayerReportFormat = settings.PlayerReportFormat,
                    PlayerReportTextContent = settings.PlayerReportTextContent,
                    PlayerReportCustomContent = settings.PlayerReportCustomContent,
                    EnableServerOfflineWebhook = settings.EnableServerOfflineWebhook,
                    ServerOfflineWebhookUrl = settings.ServerOfflineWebhookUrl,
                    ServerOfflineFormat = settings.ServerOfflineFormat,
                    ServerOfflineTextContent = settings.ServerOfflineTextContent,
                    ServerOfflineCustomContent = settings.ServerOfflineCustomContent,
                    EnableServerOnlineWebhook = settings.EnableServerOnlineWebhook,
                    ServerOnlineWebhookUrl = settings.ServerOnlineWebhookUrl,
                    ServerOnlineFormat = settings.ServerOnlineFormat,
                    ServerOnlineTextContent = settings.ServerOnlineTextContent,
                    ServerOnlineCustomContent = settings.ServerOnlineCustomContent,
                    EnableServerProtectionWebhook = settings.EnableServerProtectionWebhook,
                    ServerProtectionWebhookUrl = settings.ServerProtectionWebhookUrl,
                    ServerProtectionFormat = settings.ServerProtectionFormat,
                    ServerProtectionTextContent = settings.ServerProtectionTextContent,
                    ServerProtectionCustomContent = settings.ServerProtectionCustomContent,
                    CreatedAt = settings.CreatedAt,
                    UpdatedAt = settings.UpdatedAt
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting webhook settings");
            return StatusCode(500, ApiErrorHelper.FormatError("Error getting settings", ex));
        }
    }

    /// <summary>
    /// Update webhook settings
    /// </summary>
    [HttpPut("UpdateSettings")]
    public async Task<IActionResult> UpdateSettings([FromBody] ServerWebhooks_UpdateSettingsDTO request)
    {
        try
        {
            ApplicationUser user = await User.GetUser(_dbContext);
            SystemProfile profile = await User.GetUserSystemProfile(_dbContext);

            if (profile.Id != user.SystemProfileId)
            {
                return Unauthorized("System profile mismatch");
            }

            int serverId = user.SelectedServerId ?? 0;
            if (serverId <= 0)
            {
                return BadRequest("No server selected");
            }

            // Check if user has access to this server
            bool hasAccess = await User.HasServerAccess(_dbContext, serverId);
            if (!hasAccess)
            {
                return Unauthorized("User does not have access to this server");
            }

            // Get existing settings or create new
            var settings = await _dbContext.ServerWebhookSettings
                .FirstOrDefaultAsync(s => s.ServerId == serverId);

            if (settings == null)
            {
                settings = new ServerWebhookSettings
                {
                    ServerId = serverId,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.ServerWebhookSettings.Add(settings);
            }

            // Update settings
            settings.EnablePlayerConnectWebhook = request.EnablePlayerConnectWebhook;
            settings.PlayerConnectWebhookUrl = request.PlayerConnectWebhookUrl;
            settings.PlayerConnectFormat = request.PlayerConnectFormat;
            settings.PlayerConnectTextContent = request.PlayerConnectTextContent;
            settings.PlayerConnectCustomContent = request.PlayerConnectCustomContent;
            settings.EnablePlayerDisconnectWebhook = request.EnablePlayerDisconnectWebhook;
            settings.PlayerDisconnectWebhookUrl = request.PlayerDisconnectWebhookUrl;
            settings.PlayerDisconnectFormat = request.PlayerDisconnectFormat;
            settings.PlayerDisconnectTextContent = request.PlayerDisconnectTextContent;
            settings.PlayerDisconnectCustomContent = request.PlayerDisconnectCustomContent;
            settings.EnablePlayerKillWebhook = request.EnablePlayerKillWebhook;
            settings.PlayerKillWebhookUrl = request.PlayerKillWebhookUrl;
            settings.PlayerKillFormat = request.PlayerKillFormat;
            settings.PlayerKillTextContent = request.PlayerKillTextContent;
            settings.PlayerKillCustomContent = request.PlayerKillCustomContent;
            settings.EnablePlayerBanWebhook = request.EnablePlayerBanWebhook;
            settings.PlayerBanWebhookUrl = request.PlayerBanWebhookUrl;
            settings.PlayerBanFormat = request.PlayerBanFormat;
            settings.PlayerBanTextContent = request.PlayerBanTextContent;
            settings.PlayerBanCustomContent = request.PlayerBanCustomContent;
            settings.EnablePlayerKickWebhook = request.EnablePlayerKickWebhook;
            settings.PlayerKickWebhookUrl = request.PlayerKickWebhookUrl;
            settings.PlayerKickFormat = request.PlayerKickFormat;
            settings.PlayerKickTextContent = request.PlayerKickTextContent;
            settings.PlayerKickCustomContent = request.PlayerKickCustomContent;
            settings.EnablePlayerReportWebhook = request.EnablePlayerReportWebhook;
            settings.PlayerReportWebhookUrl = request.PlayerReportWebhookUrl;
            settings.PlayerReportFormat = request.PlayerReportFormat;
            settings.PlayerReportTextContent = request.PlayerReportTextContent;
            settings.PlayerReportCustomContent = request.PlayerReportCustomContent;
            settings.EnableServerOfflineWebhook = request.EnableServerOfflineWebhook;
            settings.ServerOfflineWebhookUrl = request.ServerOfflineWebhookUrl;
            settings.ServerOfflineFormat = request.ServerOfflineFormat;
            settings.ServerOfflineTextContent = request.ServerOfflineTextContent;
            settings.ServerOfflineCustomContent = request.ServerOfflineCustomContent;
            settings.EnableServerOnlineWebhook = request.EnableServerOnlineWebhook;
            settings.ServerOnlineWebhookUrl = request.ServerOnlineWebhookUrl;
            settings.ServerOnlineFormat = request.ServerOnlineFormat;
            settings.ServerOnlineTextContent = request.ServerOnlineTextContent;
            settings.ServerOnlineCustomContent = request.ServerOnlineCustomContent;
            settings.EnableServerProtectionWebhook = request.EnableServerProtectionWebhook;
            settings.ServerProtectionWebhookUrl = request.ServerProtectionWebhookUrl;
            settings.ServerProtectionFormat = request.ServerProtectionFormat;
            settings.ServerProtectionTextContent = request.ServerProtectionTextContent;
            settings.ServerProtectionCustomContent = request.ServerProtectionCustomContent;
            settings.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Updated webhook settings for server {ServerId}", serverId);

            return Ok(new ServerWebhooks_ResponseDTO
            {
                Message = "Settings updated successfully",
                Settings = new ServerWebhookSettingsDTO
                {
                    Id = settings.Id,
                    ServerId = settings.ServerId,
                    EnablePlayerConnectWebhook = settings.EnablePlayerConnectWebhook,
                    PlayerConnectWebhookUrl = settings.PlayerConnectWebhookUrl,
                    PlayerConnectFormat = settings.PlayerConnectFormat,
                    PlayerConnectTextContent = settings.PlayerConnectTextContent,
                    PlayerConnectCustomContent = settings.PlayerConnectCustomContent,
                    EnablePlayerDisconnectWebhook = settings.EnablePlayerDisconnectWebhook,
                    PlayerDisconnectWebhookUrl = settings.PlayerDisconnectWebhookUrl,
                    PlayerDisconnectFormat = settings.PlayerDisconnectFormat,
                    PlayerDisconnectTextContent = settings.PlayerDisconnectTextContent,
                    PlayerDisconnectCustomContent = settings.PlayerDisconnectCustomContent,
                    EnablePlayerKillWebhook = settings.EnablePlayerKillWebhook,
                    PlayerKillWebhookUrl = settings.PlayerKillWebhookUrl,
                    PlayerKillFormat = settings.PlayerKillFormat,
                    PlayerKillTextContent = settings.PlayerKillTextContent,
                    PlayerKillCustomContent = settings.PlayerKillCustomContent,
                    EnablePlayerBanWebhook = settings.EnablePlayerBanWebhook,
                    PlayerBanWebhookUrl = settings.PlayerBanWebhookUrl,
                    PlayerBanFormat = settings.PlayerBanFormat,
                    PlayerBanTextContent = settings.PlayerBanTextContent,
                    PlayerBanCustomContent = settings.PlayerBanCustomContent,
                    EnablePlayerKickWebhook = settings.EnablePlayerKickWebhook,
                    PlayerKickWebhookUrl = settings.PlayerKickWebhookUrl,
                    PlayerKickFormat = settings.PlayerKickFormat,
                    PlayerKickTextContent = settings.PlayerKickTextContent,
                    PlayerKickCustomContent = settings.PlayerKickCustomContent,
                    EnablePlayerReportWebhook = settings.EnablePlayerReportWebhook,
                    PlayerReportWebhookUrl = settings.PlayerReportWebhookUrl,
                    PlayerReportFormat = settings.PlayerReportFormat,
                    PlayerReportTextContent = settings.PlayerReportTextContent,
                    PlayerReportCustomContent = settings.PlayerReportCustomContent,
                    EnableServerOfflineWebhook = settings.EnableServerOfflineWebhook,
                    ServerOfflineWebhookUrl = settings.ServerOfflineWebhookUrl,
                    ServerOfflineFormat = settings.ServerOfflineFormat,
                    ServerOfflineTextContent = settings.ServerOfflineTextContent,
                    ServerOfflineCustomContent = settings.ServerOfflineCustomContent,
                    EnableServerOnlineWebhook = settings.EnableServerOnlineWebhook,
                    ServerOnlineWebhookUrl = settings.ServerOnlineWebhookUrl,
                    ServerOnlineFormat = settings.ServerOnlineFormat,
                    ServerOnlineTextContent = settings.ServerOnlineTextContent,
                    ServerOnlineCustomContent = settings.ServerOnlineCustomContent,
                    EnableServerProtectionWebhook = settings.EnableServerProtectionWebhook,
                    ServerProtectionWebhookUrl = settings.ServerProtectionWebhookUrl,
                    ServerProtectionFormat = settings.ServerProtectionFormat,
                    ServerProtectionTextContent = settings.ServerProtectionTextContent,
                    ServerProtectionCustomContent = settings.ServerProtectionCustomContent,
                    CreatedAt = settings.CreatedAt,
                    UpdatedAt = settings.UpdatedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating webhook settings");
            return StatusCode(500, ApiErrorHelper.FormatError("Error updating settings", ex));
        }
    }

    /// <summary>
    /// Test a webhook URL
    /// </summary>
    [HttpPost("TestWebhook")]
    public async Task<IActionResult> TestWebhook([FromBody] ServerWebhooks_TestWebhookDTO request)
    {
        try
        {
            _logger.LogInformation("TestWebhook endpoint called");

            // Log request details
            if (request == null)
            {
                _logger.LogWarning("TestWebhook: Request is NULL");
                return BadRequest("Request cannot be null");
            }

            _logger.LogInformation("TestWebhook: EventType={EventType}, WebhookUrl={WebhookUrl}",
                request.EventType,
                string.IsNullOrWhiteSpace(request.WebhookUrl) ? "(empty)" : LogSanitizer.Sanitize(request.WebhookUrl.Substring(0, Math.Min(50, request.WebhookUrl.Length))));

            ApplicationUser user = await User.GetUser(_dbContext);
            _logger.LogInformation("TestWebhook: User retrieved: {UserId}", user.Id);

            SystemProfile profile = await User.GetUserSystemProfile(_dbContext);
            _logger.LogInformation("TestWebhook: Profile retrieved: {ProfileId}", profile.Id);

            if (profile.Id != user.SystemProfileId)
            {
                _logger.LogWarning("TestWebhook: Profile mismatch - Profile.Id={ProfileId}, User.SystemProfileId={UserProfileId}",
                    profile.Id, user.SystemProfileId);
                return Unauthorized("System profile mismatch");
            }

            int serverId = user.SelectedServerId ?? 0;
            _logger.LogInformation("TestWebhook: SelectedServerId={ServerId}", serverId);

            if (serverId <= 0)
            {
                _logger.LogWarning("TestWebhook: No server selected");
                return BadRequest("No server selected");
            }

            // Check if user has access to this server
            bool hasAccess = await User.HasServerAccess(_dbContext, serverId);
            _logger.LogInformation("TestWebhook: Server access check={HasAccess}", hasAccess);

            if (!hasAccess)
            {
                _logger.LogWarning("TestWebhook: User does not have access to server {ServerId}", serverId);
                return Unauthorized("User does not have access to this server");
            }

            // Validate webhook URL
            if (string.IsNullOrWhiteSpace(request.WebhookUrl))
            {
                _logger.LogWarning("TestWebhook: Webhook URL is empty or whitespace");
                return BadRequest("Webhook URL is required");
            }

            _logger.LogInformation("TestWebhook: Validating URL format");
            if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("TestWebhook: Invalid URL format: {WebhookUrl}", LogSanitizer.Sanitize(request.WebhookUrl));
                return BadRequest("Invalid webhook URL format");
            }

            _logger.LogInformation("TestWebhook: Checking if URL is Discord webhook");
            bool isDiscordHost = uri.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) ||
                                  uri.Host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase) ||
                                  uri.Host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase) ||
                                  uri.Host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);

            if (!isDiscordHost || !uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("TestWebhook: URL is not a Discord webhook: {WebhookUrl}", LogSanitizer.Sanitize(request.WebhookUrl));
                return BadRequest("URL must be a valid Discord webhook URL (discord.com or discordapp.com)");
            }

            _logger.LogInformation("TestWebhook: All validations passed, sending test webhook");

            // Send test webhook
            await _discordWebhookService.SendTestWebhookAsync(serverId, request.WebhookUrl, request.EventType);

            _logger.LogInformation("TestWebhook: Successfully sent test webhook for event {EventType}", request.EventType);

            return Ok(new { message = $"Test webhook for {request.EventType} sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestWebhook: Exception occurred - {Message}", ex.Message);
            return StatusCode(500, ApiErrorHelper.FormatError("Error sending test webhook", ex));
        }
    }
}
