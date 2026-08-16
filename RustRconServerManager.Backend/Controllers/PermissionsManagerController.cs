using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.PermissionsManager;
using System.Text.RegularExpressions;

namespace RustRconServerManager.Backend.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PermissionsManagerController : ControllerBase
    {
        private readonly IRconBackgroundService _rconBackgroundService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<PermissionsManagerController> _logger;

        public PermissionsManagerController(
            IRconBackgroundService rconBackgroundService,
            AppDbContext dbContext,
            ILogger<PermissionsManagerController> logger)
        {
            _rconBackgroundService = rconBackgroundService;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Get all permissions/groups for the selected server
        /// </summary>
        [HttpGet("GetPermissions")]
        public async Task<IActionResult> GetPermissions()
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

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                string command = server.ModFramework?.ToLower() == "carbon" ? "c.show groups" : "o.show groups";

                // Execute RCON command to get groups
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command, rconResponse ?? "NULL");

                if (string.IsNullOrWhiteSpace(rconResponse))
                {
                    _logger.LogWarning("RCON response is null or empty");
                    return Ok(new PermissionsManager_ResponseDTO
                    {
                        Message = "No permission groups found or server did not respond",
                        Groups = new List<PermissionGroupDTO>()
                    });
                }

                // Parse the RCON response to extract groups
                var groups = ParseGroupsFromRconResponse(rconResponse, server.ModFramework);

                _logger.LogInformation("Parsed {Count} groups successfully", groups.Count);

                var response = new PermissionsManager_ResponseDTO
                {
                    Message = "Groups loaded successfully",
                    Groups = groups
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions");
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting permissions", ex));
            }
        }

        /// <summary>
        /// Get players in a specific group
        /// </summary>
        [HttpGet("GetGroupPlayers")]
        public async Task<IActionResult> GetGroupPlayers([FromQuery] string groupName)
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

                if (string.IsNullOrWhiteSpace(groupName))
                {
                    return BadRequest("Group name is required");
                }

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? $"c.show group {groupName}"
                    : $"o.show group {groupName}";

                // Execute RCON command to get group details
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command.Replace("\r", "").Replace("\n", ""), rconResponse?.Replace("\r", "").Replace("\n", "") ?? "NULL");

                if (string.IsNullOrWhiteSpace(rconResponse))
                {
                    return Ok(new List<GroupPlayerDTO>());
                }

                // Parse the RCON response to extract players
                var players = ParsePlayersFromGroupResponse(rconResponse, groupName);

                _logger.LogInformation("Parsed {Count} players for group {GroupName}", players.Count, groupName.Replace("\r", "").Replace("\n", ""));

                return Ok(players);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting group players");
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting group players", ex));
            }
        }

        /// <summary>
        /// Search for players by name or Steam ID
        /// </summary>
        [HttpGet("SearchPlayers")]
        public async Task<IActionResult> SearchPlayers([FromQuery] string searchTerm)
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

                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return Ok(new List<PlayerSearchResultDTO>());
                }

                // Search players by name or Steam ID
                var players = await _dbContext.SteamPlayers
                    .Where(p => p.ServerId == serverId &&
                               ((p.Name != null && p.Name.Contains(searchTerm)) || p.SteamId.Contains(searchTerm)))
                    .OrderByDescending(p => p.LastSeen)
                    .Take(10)
                    .Select(p => new PlayerSearchResultDTO
                    {
                        SteamId = p.SteamId,
                        PlayerName = p.Name ?? "Unknown",
                        LastSeen = p.LastSeen
                    })
                    .ToListAsync();

                return Ok(players);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching players");
                return StatusCode(500, ApiErrorHelper.FormatError("Error searching players", ex));
            }
        }

        /// <summary>
        /// Add a player to a permission group
        /// </summary>
        [HttpPost("AddPlayerToGroup")]
        public async Task<IActionResult> AddPlayerToGroup([FromBody] PermissionsManager_AddToGroupDTO request)
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

                if (string.IsNullOrWhiteSpace(request.SteamId) || string.IsNullOrWhiteSpace(request.GroupName))
                {
                    return BadRequest("SteamId and GroupName are required");
                }

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? $"c.usergroup add {request.SteamId} {request.GroupName}"
                    : $"o.usergroup add {request.SteamId} {request.GroupName}";

                _logger.LogInformation("Executing command: {Command} for server {ServerId}", command.Replace("\r", "").Replace("\n", ""), serverId);

                // Execute RCON command
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command.Replace("\r", "").Replace("\n", ""), rconResponse?.Replace("\r", "").Replace("\n", "") ?? "NULL");

                return Ok(new {
                    message = $"Player {request.SteamId} added to group {request.GroupName}",
                    rconResponse = rconResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding player to group");
                return StatusCode(500, ApiErrorHelper.FormatError("Error adding player to group", ex));
            }
        }

        /// <summary>
        /// Remove a player from a permission group
        /// </summary>
        [HttpPost("RemovePlayerFromGroup")]
        public async Task<IActionResult> RemovePlayerFromGroup([FromBody] PermissionsManager_RemoveFromGroupDTO request)
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

                if (string.IsNullOrWhiteSpace(request.SteamId) || string.IsNullOrWhiteSpace(request.GroupName))
                {
                    return BadRequest("SteamId and GroupName are required");
                }

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? $"c.usergroup remove {request.SteamId} {request.GroupName}"
                    : $"o.usergroup remove {request.SteamId} {request.GroupName}";

                _logger.LogInformation("Executing command: {Command} for server {ServerId}", command.Replace("\r", "").Replace("\n", ""), serverId);

                // Execute RCON command
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command.Replace("\r", "").Replace("\n", ""), rconResponse?.Replace("\r", "").Replace("\n", "") ?? "NULL");

                return Ok(new {
                    message = $"Player {request.SteamId} removed from group {request.GroupName}",
                    rconResponse = rconResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing player from group");
                return StatusCode(500, ApiErrorHelper.FormatError("Error removing player from group", ex));
            }
        }

        /// <summary>
        /// Parse groups from RCON response
        /// Expected format from Oxide/Carbon: "Groups: default, admin, moderator, ..."
        /// </summary>
        private List<PermissionGroupDTO> ParseGroupsFromRconResponse(string rconResponse, string? modFramework)
        {
            var groups = new List<PermissionGroupDTO>();

            try
            {
                _logger.LogInformation("Starting to parse RCON response. Response length: {Length}", rconResponse.Length);

                // Split response into lines
                var lines = rconResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                _logger.LogInformation("Split into {Count} lines", lines.Length);

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    _logger.LogInformation("Line {Index}: '{Line}'", i, line);

                    // Look for the line that starts with "Groups:"
                    if (line.TrimStart().StartsWith("Groups:", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Found Groups line at index {Index}", i);

                        // Extract everything after "Groups:" on the same line
                        var colonIndex = line.IndexOf(':');
                        var groupsLine = line.Substring(colonIndex + 1).Trim();

                        // If the groups are on the same line, use them
                        // Otherwise, check the next line
                        if (string.IsNullOrWhiteSpace(groupsLine) && i + 1 < lines.Length)
                        {
                            groupsLine = lines[i + 1].Trim();
                            _logger.LogInformation("Groups are on next line: '{GroupsLine}'", groupsLine);
                        }
                        else
                        {
                            _logger.LogInformation("Groups line after colon: '{GroupsLine}'", groupsLine);
                        }

                        // Split by comma
                        var groupNames = groupsLine.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        _logger.LogInformation("Found {Count} group names after split", groupNames.Length);

                        foreach (var groupName in groupNames)
                        {
                            var trimmedName = groupName.Trim();
                            _logger.LogInformation("Processing group: '{GroupName}'", trimmedName);

                            if (!string.IsNullOrWhiteSpace(trimmedName))
                            {
                                groups.Add(new PermissionGroupDTO
                                {
                                    GroupName = trimmedName,
                                    Permissions = new List<string>(),
                                    PlayerCount = 0 // Will be populated later when we fetch players
                                });
                            }
                        }

                        break; // Found the groups line, no need to continue
                    }
                }

                _logger.LogInformation("Parsed {Count} groups from RCON response for {Framework}", groups.Count, modFramework ?? "Unknown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing groups from RCON response");
            }

            return groups;
        }

        /// <summary>
        /// Parse players from group response
        /// Expected format: "Group 'name' players:" followed by comma-separated "SteamID (PlayerName)" entries
        /// </summary>
        private List<GroupPlayerDTO> ParsePlayersFromGroupResponse(string rconResponse, string groupName)
        {
            var players = new List<GroupPlayerDTO>();

            try
            {
                _logger.LogInformation("Starting to parse players from group response. Response length: {Length}", rconResponse.Length);

                var lines = rconResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                _logger.LogInformation("Split into {Count} lines", lines.Length);

                bool foundPlayersSection = false;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    _logger.LogInformation("Processing line: '{Line}'", trimmedLine.Length > 100 ? trimmedLine.Substring(0, 100) + "..." : trimmedLine);

                    // Look for the players section - format: "Group 'groupname' players:"
                    if (trimmedLine.Contains("players:", StringComparison.OrdinalIgnoreCase))
                    {
                        foundPlayersSection = true;
                        _logger.LogInformation("Found players section header");
                        continue;
                    }

                    // If we found the players section, the next line should contain the players
                    if (foundPlayersSection && !trimmedLine.EndsWith(":"))
                    {
                        _logger.LogInformation("Processing players line");

                        // Split by comma to get individual player entries
                        var playerEntries = trimmedLine.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        _logger.LogInformation("Found {Count} player entries", playerEntries.Length);

                        foreach (var entry in playerEntries)
                        {
                            var trimmedEntry = entry.Trim();

                            // Format: "SteamID (PlayerName)"
                            // Example: "76561198012345678 (John Doe)"
                            var match = Regex.Match(trimmedEntry, @"^(\d{17})\s*\((.+?)\)");
                            if (match.Success)
                            {
                                var steamId = match.Groups[1].Value;
                                var playerName = match.Groups[2].Value.Trim();

                                _logger.LogInformation("Found player: {Name} ({SteamId})", playerName, steamId);

                                players.Add(new GroupPlayerDTO
                                {
                                    PlayerName = playerName,
                                    SteamId = steamId
                                });
                            }
                            else if (!string.IsNullOrWhiteSpace(trimmedEntry))
                            {
                                _logger.LogWarning("Could not parse entry: '{Entry}'", trimmedEntry);
                            }
                        }

                        // We've processed the players, stop here
                        break;
                    }
                }

                _logger.LogInformation("Parsed {Count} players from group response", players.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing players from group response");
            }

            return players;
        }
    }
}
