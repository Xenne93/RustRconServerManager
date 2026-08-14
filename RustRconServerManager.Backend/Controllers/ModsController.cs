using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Shared.Mods;
using RustRconServerManager.Shared.PluginVersionCheck;
using System.Text.RegularExpressions;

namespace RustRconServerManager.Backend.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ModsController : ControllerBase
    {
        private readonly IRconBackgroundService _rconBackgroundService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ModsController> _logger;
        private readonly PluginVersionCheckService _pluginVersionCheckService;

        public ModsController(
            IRconBackgroundService rconBackgroundService,
            AppDbContext dbContext,
            ILogger<ModsController> logger,
            PluginVersionCheckService pluginVersionCheckService)
        {
            _rconBackgroundService = rconBackgroundService;
            _dbContext = dbContext;
            _logger = logger;
            _pluginVersionCheckService = pluginVersionCheckService;
        }

        /// <summary>
        /// Get all plugins/mods for the selected server
        /// </summary>
        [HttpGet("GetPlugins")]
        public async Task<IActionResult> GetPlugins()
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
                string command = server.ModFramework?.ToLower() == "carbon" ? "c.plugins" : "o.plugins";

                // Execute RCON command to get plugins
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command, rconResponse ?? "NULL");

                if (string.IsNullOrWhiteSpace(rconResponse))
                {
                    _logger.LogWarning("RCON response is null or empty");
                    return Ok(new Mods_ResponseDTO
                    {
                        Message = "No plugins found or server did not respond",
                        Plugins = new List<PluginDTO>()
                    });
                }

                // Parse the RCON response to extract plugins
                var plugins = ParsePluginsFromRconResponse(rconResponse);

                _logger.LogInformation("Parsed {Count} plugins successfully", plugins.Count);

                // Load plugin source configurations from database
                var pluginSources = await _dbContext.ServerPluginSources
                    .Where(sps => sps.RustServerId == serverId)
                    .ToDictionaryAsync(sps => sps.PluginName, sps => sps.Source);

                foreach (var plugin in plugins)
                {
                    var pluginName = plugin.FileName.Replace(".cs", "");

                    // Set source information
                    if (pluginSources.TryGetValue(pluginName, out var source))
                    {
                        plugin.Source = source;
                        plugin.IsCustom = source == PluginSource.Custom;
                    }
                    else
                    {
                        plugin.Source = null;
                        plugin.IsCustom = false;
                    }
                }

                var response = new Mods_ResponseDTO
                {
                    Message = "Plugins loaded successfully",
                    Plugins = plugins,
                    TotalCount = plugins.Count,
                    LoadedCount = plugins.Count(p => p.Status == PluginStatus.Loaded),
                    FailedCount = plugins.Count(p => p.Status == PluginStatus.Failed),
                    UnloadedCount = plugins.Count(p => p.Status == PluginStatus.Unloaded)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting plugins");
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting plugins", ex));
            }
        }

        /// <summary>
        /// Load a specific plugin
        /// </summary>
        [HttpPost("LoadPlugin")]
        public async Task<IActionResult> LoadPlugin([FromBody] Mods_ReloadPluginDTO request)
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

                if (string.IsNullOrWhiteSpace(request.PluginName))
                {
                    return BadRequest("Plugin name is required");
                }

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                // Use filename without .cs extension
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? $"c.load {request.PluginName}"
                    : $"o.load {request.PluginName}";

                _logger.LogInformation("Executing command: {Command} for server {ServerId}", command, serverId);

                // Execute RCON command
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command, rconResponse ?? "NULL");

                return Ok(new
                {
                    message = $"Plugin {request.PluginName} loaded",
                    rconResponse = rconResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading plugin");
                return StatusCode(500, ApiErrorHelper.FormatError("Error loading plugin", ex));
            }
        }

        /// <summary>
        /// Reload a specific plugin
        /// </summary>
        [HttpPost("ReloadPlugin")]
        public async Task<IActionResult> ReloadPlugin([FromBody] Mods_ReloadPluginDTO request)
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

                if (string.IsNullOrWhiteSpace(request.PluginName))
                {
                    return BadRequest("Plugin name is required");
                }

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                // Use filename without .cs extension
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? $"c.reload {request.PluginName}"
                    : $"o.reload {request.PluginName}";

                _logger.LogInformation("Executing command: {Command} for server {ServerId}", command, serverId);

                // Execute RCON command
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command, rconResponse ?? "NULL");

                return Ok(new
                {
                    message = $"Plugin {request.PluginName} reloaded",
                    rconResponse = rconResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading plugin");
                return StatusCode(500, ApiErrorHelper.FormatError("Error reloading plugin", ex));
            }
        }

        /// <summary>
        /// Unload a specific plugin
        /// </summary>
        [HttpPost("UnloadPlugin")]
        public async Task<IActionResult> UnloadPlugin([FromBody] Mods_UnloadPluginDTO request)
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

                if (string.IsNullOrWhiteSpace(request.PluginName))
                {
                    return BadRequest("Plugin name is required");
                }

                // Get server information to determine modding framework
                var server = await _dbContext.RconServers.FindAsync(serverId);
                if (server == null)
                {
                    return NotFound("Server not found");
                }

                // Determine the command based on modding framework
                // Use filename without .cs extension
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? $"c.unload {request.PluginName}"
                    : $"o.unload {request.PluginName}";

                _logger.LogInformation("Executing command: {Command} for server {ServerId}", command, serverId);

                // Execute RCON command
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command, rconResponse ?? "NULL");

                return Ok(new
                {
                    message = $"Plugin {request.PluginName} unloaded",
                    rconResponse = rconResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading plugin");
                return StatusCode(500, ApiErrorHelper.FormatError("Error unloading plugin", ex));
            }
        }

        /// <summary>
        /// Reload all plugins
        /// </summary>
        [HttpPost("ReloadAll")]
        public async Task<IActionResult> ReloadAll()
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
                string command = server.ModFramework?.ToLower() == "carbon"
                    ? "c.reload *"
                    : "o.reload *";

                _logger.LogInformation("Executing command: {Command} for server {ServerId}", command, serverId);

                // Execute RCON command
                string? rconResponse = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId);

                _logger.LogInformation("RCON Response for '{Command}': {Response}", command, rconResponse ?? "NULL");

                return Ok(new
                {
                    message = "All plugins reloaded",
                    rconResponse = rconResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading all plugins");
                return StatusCode(500, ApiErrorHelper.FormatError("Error reloading all plugins", ex));
            }
        }

        /// <summary>
        /// Set plugin source (Umod, Codefling, or Custom)
        /// Only plugins with a configured source will show version update information
        /// </summary>
        [HttpPost("SetPluginSource")]
        public async Task<IActionResult> SetPluginSource([FromBody] Mods_SetPluginSourceDTO request)
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

                if (string.IsNullOrWhiteSpace(request.PluginName))
                {
                    return BadRequest("Plugin name is required");
                }

                // Check if entry exists
                var existingEntry = await _dbContext.ServerPluginSources
                    .FirstOrDefaultAsync(sps => sps.RustServerId == serverId && sps.PluginName == request.PluginName);

                if (request.Source == null)
                {
                    // Remove source configuration (plugin will not show update info)
                    if (existingEntry != null)
                    {
                        _dbContext.ServerPluginSources.Remove(existingEntry);
                        await _dbContext.SaveChangesAsync();

                        _logger.LogInformation("Removed plugin source for {PluginName} on server {ServerId}", request.PluginName, serverId);

                        return Ok(new
                        {
                            message = $"Plugin {request.PluginName} source removed - no version checking",
                            source = (string?)null
                        });
                    }

                    return Ok(new
                    {
                        message = $"Plugin {request.PluginName} has no source configured",
                        source = (string?)null
                    });
                }

                if (existingEntry != null)
                {
                    // Update existing source
                    existingEntry.Source = request.Source.Value;
                    existingEntry.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("Updated plugin source for {PluginName} on server {ServerId} to {Source}",
                        request.PluginName, serverId, request.Source.Value);

                    return Ok(new
                    {
                        message = $"Plugin {request.PluginName} source updated to {request.Source.Value}",
                        source = request.Source.Value.ToString()
                    });
                }
                else
                {
                    // Create new source configuration
                    var newEntry = new ServerPluginSource
                    {
                        RustServerId = serverId,
                        PluginName = request.PluginName,
                        Source = request.Source.Value,
                        CreatedAt = DateTime.UtcNow
                    };

                    _dbContext.ServerPluginSources.Add(newEntry);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("Created plugin source for {PluginName} on server {ServerId} as {Source}",
                        request.PluginName, serverId, request.Source.Value);

                    return Ok(new
                    {
                        message = $"Plugin {request.PluginName} source set to {request.Source.Value}",
                        source = request.Source.Value.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting plugin source");
                return StatusCode(500, ApiErrorHelper.FormatError("Error setting plugin source", ex));
            }
        }

        /// <summary>
        /// Parse plugins from RCON response
        /// Expected formats:
        /// - Loaded: 01 "Admin Deep Cover" (2.2.8) by Dana (0.00s / 20 KB) - AdminDeepCover.cs
        /// - Failed: 56 EntityOwner - Failed to compile: 'error message' | Line: 1227, Pos: 61
        /// - Unloaded: 57 ZoneManager - Unloaded
        /// </summary>
        private List<PluginDTO> ParsePluginsFromRconResponse(string rconResponse)
        {
            var plugins = new List<PluginDTO>();

            try
            {
                _logger.LogInformation("Starting to parse RCON response. Response length: {Length}", rconResponse.Length);

                // Split response into lines
                var lines = rconResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                _logger.LogInformation("Split into {Count} lines", lines.Length);

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Skip header line
                    if (trimmedLine.StartsWith("Listing") || string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        continue;
                    }

                    // Try to parse loaded plugin
                    // Format: 01 "Admin Deep Cover" (2.2.8) by Dana (0.00s / 20 KB) - AdminDeepCover.cs
                    var loadedMatch = Regex.Match(trimmedLine, @"^\s*(\d+)\s+""([^""]+)""\s+\(([^)]+)\)\s+by\s+([^(]+)\s+\(([^/]+)/\s*([^)]+)\)\s+-\s+(.+\.cs)$");
                    if (loadedMatch.Success)
                    {
                        plugins.Add(new PluginDTO
                        {
                            Number = int.Parse(loadedMatch.Groups[1].Value),
                            Name = loadedMatch.Groups[2].Value.Trim(),
                            Version = loadedMatch.Groups[3].Value.Trim(),
                            Author = loadedMatch.Groups[4].Value.Trim(),
                            LoadTime = loadedMatch.Groups[5].Value.Trim(),
                            MemoryUsage = loadedMatch.Groups[6].Value.Trim(),
                            FileName = loadedMatch.Groups[7].Value.Trim(),
                            Status = PluginStatus.Loaded
                        });
                        continue;
                    }

                    // Try to parse failed plugin
                    // Format: 56 EntityOwner - Failed to compile: 'error message' | Line: 1227, Pos: 61
                    var failedMatch = Regex.Match(trimmedLine, @"^\s*(\d+)\s+(\S+)\s+-\s+Failed to compile:\s+(.+)$");
                    if (failedMatch.Success)
                    {
                        plugins.Add(new PluginDTO
                        {
                            Number = int.Parse(failedMatch.Groups[1].Value),
                            Name = failedMatch.Groups[2].Value.Trim(),
                            FileName = $"{failedMatch.Groups[2].Value.Trim()}.cs",
                            Status = PluginStatus.Failed,
                            ErrorMessage = failedMatch.Groups[3].Value.Trim()
                        });
                        continue;
                    }

                    // Try to parse unloaded plugin
                    // Format: 57 ZoneManager - Unloaded
                    var unloadedMatch = Regex.Match(trimmedLine, @"^\s*(\d+)\s+(\S+)\s+-\s+Unloaded$");
                    if (unloadedMatch.Success)
                    {
                        plugins.Add(new PluginDTO
                        {
                            Number = int.Parse(unloadedMatch.Groups[1].Value),
                            Name = unloadedMatch.Groups[2].Value.Trim(),
                            FileName = $"{unloadedMatch.Groups[2].Value.Trim()}.cs",
                            Status = PluginStatus.Unloaded
                        });
                        continue;
                    }

                    _logger.LogWarning("Could not parse line: '{Line}'", trimmedLine);
                }

                _logger.LogInformation("Parsed {Count} plugins from RCON response", plugins.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing plugins from RCON response");
            }

            return plugins;
        }

        /// <summary>
        /// Check plugin version against configured source (Umod/Codefling)
        /// Only checks plugins with a configured source
        /// </summary>
        [HttpGet("CheckPluginVersion")]
        public async Task<IActionResult> CheckPluginVersion([FromQuery] string pluginName, [FromQuery] string installedVersion)
        {
            try
            {
                ApplicationUser user = await User.GetUser(_dbContext);
                int serverId = user.SelectedServerId ?? 0;

                if (serverId <= 0)
                {
                    return BadRequest("No server selected");
                }

                if (string.IsNullOrWhiteSpace(pluginName) || string.IsNullOrWhiteSpace(installedVersion))
                {
                    return BadRequest("Plugin name and installed version are required");
                }

                var result = await _pluginVersionCheckService.CheckPluginVersionAsync(serverId, pluginName, installedVersion);

                if (result == null)
                {
                    return Ok(new
                    {
                        message = "No source configured for this plugin. Set a source (Umod/Codefling/Custom) to enable version checking.",
                        hasSource = false
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking version for plugin {pluginName}");
                return StatusCode(500, ApiErrorHelper.FormatError("Error checking plugin version", ex));
            }
        }

        /// <summary>
        /// Check multiple plugin versions in a single batch request
        /// More efficient than calling CheckPluginVersion for each plugin individually
        /// </summary>
        [HttpPost("CheckPluginVersionsBatch")]
        public async Task<IActionResult> CheckPluginVersionsBatch([FromBody] PluginVersionCheckBatchRequest request)
        {
            try
            {
                ApplicationUser user = await User.GetUser(_dbContext);
                int serverId = user.SelectedServerId ?? 0;

                if (serverId <= 0)
                {
                    return BadRequest("No server selected");
                }

                if (request.Plugins == null || request.Plugins.Count == 0)
                {
                    return BadRequest("No plugins provided");
                }

                // Limit batch size
                if (request.Plugins.Count > 100)
                {
                    return BadRequest("Maximum 100 plugins per batch request");
                }

                _logger.LogInformation("Checking versions for {Count} plugins in batch", request.Plugins.Count);

                var pluginsList = request.Plugins
                    .Where(p => !string.IsNullOrWhiteSpace(p.PluginName) && !string.IsNullOrWhiteSpace(p.InstalledVersion))
                    .Select(p => (p.PluginName, p.InstalledVersion))
                    .ToList();

                var results = await _pluginVersionCheckService.CheckPluginVersionsBatchAsync(serverId, pluginsList);

                var response = new PluginVersionCheckBatchResponse
                {
                    Results = results,
                    TotalCount = request.Plugins.Count,
                    CheckedCount = results.Count(r => r.Value != null)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking plugin versions in batch");
                return StatusCode(500, ApiErrorHelper.FormatError("Error checking plugin versions", ex));
            }
        }
    }
}
