using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.ServerSettings;
using RustRconServerManager.Backend.Helpers;

namespace RustRconServerManager.Backend.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ServerSettingsController : ControllerBase
    {
        private readonly IRconBackgroundService _rconBackgroundService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ServerSettingsController> _logger;

        public ServerSettingsController(
            IRconBackgroundService rconBackgroundService,
            AppDbContext dbContext,
            ILogger<ServerSettingsController> logger)
        {
            _rconBackgroundService = rconBackgroundService;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Get current server settings (hostname and description)
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

                // Get server hostname via RCON
                string? hostname = await _rconBackgroundService.ExecuteCommandWithResponse("server.hostname", serverId);
                if (hostname == null)
                {
                    return StatusCode(503, "Failed to get server hostname - RCON timeout or not connected");
                }

                // Get server description via RCON
                string? description = await _rconBackgroundService.ExecuteCommandWithResponse("server.description", serverId);
                description ??= "";

                // Get server URL via RCON
                string? url = await _rconBackgroundService.ExecuteCommandWithResponse("server.url", serverId);
                url ??= "";

                // Get server header image via RCON
                string? headerImage = await _rconBackgroundService.ExecuteCommandWithResponse("server.headerimage", serverId);
                headerImage ??= "";

                // Get server logo image via RCON
                string? logoImage = await _rconBackgroundService.ExecuteCommandWithResponse("server.logoimage", serverId);
                logoImage ??= "";

                // Get max players via RCON
                string? maxPlayersStr = await _rconBackgroundService.ExecuteCommandWithResponse("server.maxplayers", serverId);
                int maxPlayers = 0;
                if (!string.IsNullOrEmpty(maxPlayersStr) && int.TryParse(maxPlayersStr, out int parsedMaxPlayers))
                {
                    maxPlayers = parsedMaxPlayers;
                }

                var settings = new ServerSettings_DTO
                {
                    ServerHostname = hostname,
                    ServerDescription = description,
                    ServerUrl = url,
                    ServerHeaderImage = headerImage,
                    ServerLogoImage = logoImage,
                    MaxPlayers = maxPlayers
                };

                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting server settings");
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting server settings", ex));
            }
        }

        /// <summary>
        /// Update server settings (hostname and description)
        /// </summary>
        [HttpPost("UpdateSettings")]
        public async Task<IActionResult> UpdateSettings([FromBody] ServerSettings_UpdateDTO settings)
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

                // Update server hostname
                if (!string.IsNullOrWhiteSpace(settings.ServerHostname))
                {
                    var hostnameResponse = await _rconBackgroundService.ExecuteCommandWithResponse(
                        $"server.hostname \"{settings.ServerHostname}\"",
                        serverId);

                    if (hostnameResponse == null)
                    {
                        return StatusCode(503, "Failed to update server hostname - RCON timeout or not connected");
                    }
                }

                // Update server description
                var descriptionResponse = await _rconBackgroundService.ExecuteCommandWithResponse(
                    $"server.description \"{settings.ServerDescription}\"",
                    serverId);

                // Update server URL
                var urlResponse = await _rconBackgroundService.ExecuteCommandWithResponse(
                    $"server.url \"{settings.ServerUrl}\"",
                    serverId);

                // Update server header image
                var headerImageResponse = await _rconBackgroundService.ExecuteCommandWithResponse(
                    $"server.headerimage \"{settings.ServerHeaderImage}\"",
                    serverId);

                // Update server logo image
                var logoImageResponse = await _rconBackgroundService.ExecuteCommandWithResponse(
                    $"server.logoimage \"{settings.ServerLogoImage}\"",
                    serverId);

                // Update max players
                if (settings.MaxPlayers > 0)
                {
                    var maxPlayersResponse = await _rconBackgroundService.ExecuteCommandWithResponse(
                        $"server.maxplayers {settings.MaxPlayers}",
                        serverId);

                    if (maxPlayersResponse == null)
                    {
                        _logger.LogWarning("Failed to update max players for server {ServerId}", serverId);
                    }
                }

                // Save the server config
                var saveResponse = await _rconBackgroundService.ExecuteCommandWithResponse("server.writecfg", serverId);
                if (saveResponse == null)
                {
                    return StatusCode(503, "Failed to save server config - RCON timeout or not connected");
                }

                return Ok("Server settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating server settings");
                return StatusCode(500, ApiErrorHelper.FormatError("Error updating server settings", ex));
            }
        }
    }
}
