using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.ServerProtection;
using RustRconServerManager.Backend.Helpers;

namespace RustRconServerManager.Backend.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ServerProtectionController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ServerProtectionController> _logger;

        public ServerProtectionController(
            AppDbContext dbContext,
            ILogger<ServerProtectionController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Get server protection settings for the selected server
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
                var settings = await _dbContext.ServerProtectionSettings
                    .FirstOrDefaultAsync(s => s.ServerId == serverId);

                if (settings == null)
                {
                    // Create default settings
                    settings = new ServerProtectionSettings
                    {
                        ServerId = serverId,
                        CountryFilterMode = CountryFilterMode.None,
                        CountryList = string.Empty,
                        BanDurationMinutes = 1440,
                        EnableVacBanProtection = false,
                        MaxVacBans = 0,
                        VacBanAction = VacBanAction.Kick,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _dbContext.ServerProtectionSettings.Add(settings);
                    await _dbContext.SaveChangesAsync();
                }

                var response = new ServerProtection_ResponseDTO
                {
                    Message = "Settings loaded successfully",
                    Settings = new ServerProtectionSettingsDTO
                    {
                        Id = settings.Id,
                        ServerId = settings.ServerId,
                        EnableWhitelistOnly = settings.EnableWhitelistOnly,
                        WhitelistOnlyKickMessage = settings.WhitelistOnlyKickMessage,
                        CountryFilterMode = settings.CountryFilterMode,
                        CountryList = string.IsNullOrWhiteSpace(settings.CountryList)
                            ? new List<string>()
                            : settings.CountryList.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                        BanDurationMinutes = settings.BanDurationMinutes,
                        EnableVacBanProtection = settings.EnableVacBanProtection,
                        MaxVacBans = settings.MaxVacBans,
                        MinDaysSinceLastVACBan = settings.MinDaysSinceLastVACBan,
                        VacBanAction = settings.VacBanAction,
                        BlockPrivateSteamProfiles = settings.BlockPrivateSteamProfiles,
                        PrivateSteamProfileAction = settings.PrivateSteamProfileAction,
                        EnableVpnCheck = settings.EnableVpnCheck,
                        EnableVpnProtection = settings.EnableVpnProtection,
                        VpnProtectionAction = settings.VpnProtectionAction,
                        WhitelistedSteamIds = ParseWhitelistEntries(settings.WhitelistedSteamIds),
                        CreatedAt = settings.CreatedAt,
                        UpdatedAt = settings.UpdatedAt
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting server protection settings");
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting settings", ex));
            }
        }

        /// <summary>
        /// Update server protection settings
        /// </summary>
        [HttpPut("UpdateSettings")]
        public async Task<IActionResult> UpdateSettings([FromBody] ServerProtection_UpdateSettingsDTO request)
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
                var settings = await _dbContext.ServerProtectionSettings
                    .FirstOrDefaultAsync(s => s.ServerId == serverId);

                if (settings == null)
                {
                    settings = new ServerProtectionSettings
                    {
                        ServerId = serverId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.ServerProtectionSettings.Add(settings);
                }

                // Update settings
                settings.EnableWhitelistOnly = request.EnableWhitelistOnly;
                settings.WhitelistOnlyKickMessage = request.WhitelistOnlyKickMessage;
                settings.CountryFilterMode = request.CountryFilterMode;
                settings.CountryList = string.Join(",", request.CountryList);
                settings.BanDurationMinutes = request.BanDurationMinutes;
                settings.EnableVacBanProtection = request.EnableVacBanProtection;
                settings.MaxVacBans = request.MaxVacBans;
                settings.MinDaysSinceLastVACBan = request.MinDaysSinceLastVACBan;
                settings.VacBanAction = request.VacBanAction;
                settings.BlockPrivateSteamProfiles = request.BlockPrivateSteamProfiles;
                settings.PrivateSteamProfileAction = request.PrivateSteamProfileAction;
                settings.EnableVpnCheck = request.EnableVpnCheck;
                settings.EnableVpnProtection = request.EnableVpnProtection;
                settings.VpnProtectionAction = request.VpnProtectionAction;
                settings.WhitelistedSteamIds = JsonSerializer.Serialize(request.WhitelistedSteamIds);
                settings.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Updated server protection settings for server {ServerId}", serverId);

                return Ok(new ServerProtection_ResponseDTO
                {
                    Message = "Settings updated successfully",
                    Settings = new ServerProtectionSettingsDTO
                    {
                        Id = settings.Id,
                        ServerId = settings.ServerId,
                        EnableWhitelistOnly = settings.EnableWhitelistOnly,
                        WhitelistOnlyKickMessage = settings.WhitelistOnlyKickMessage,
                        CountryFilterMode = settings.CountryFilterMode,
                        CountryList = request.CountryList,
                        BanDurationMinutes = settings.BanDurationMinutes,
                        EnableVacBanProtection = settings.EnableVacBanProtection,
                        MaxVacBans = settings.MaxVacBans,
                        MinDaysSinceLastVACBan = settings.MinDaysSinceLastVACBan,
                        VacBanAction = settings.VacBanAction,
                        BlockPrivateSteamProfiles = settings.BlockPrivateSteamProfiles,
                        PrivateSteamProfileAction = settings.PrivateSteamProfileAction,
                        EnableVpnCheck = settings.EnableVpnCheck,
                        EnableVpnProtection = settings.EnableVpnProtection,
                        VpnProtectionAction = settings.VpnProtectionAction,
                        WhitelistedSteamIds = ParseWhitelistEntries(settings.WhitelistedSteamIds),
                        CreatedAt = settings.CreatedAt,
                        UpdatedAt = settings.UpdatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating server protection settings");
                return StatusCode(500, ApiErrorHelper.FormatError("Error updating settings", ex));
            }
        }

        /// <summary>
        /// Get list of all countries for the dropdown
        /// </summary>
        [HttpGet("GetCountries")]
        public IActionResult GetCountries()
        {
            try
            {
                var countries = new List<CountryDTO>
                {
                    new CountryDTO { Code = "AF", Name = "Afghanistan" },
                    new CountryDTO { Code = "AL", Name = "Albania" },
                    new CountryDTO { Code = "DZ", Name = "Algeria" },
                    new CountryDTO { Code = "AR", Name = "Argentina" },
                    new CountryDTO { Code = "AU", Name = "Australia" },
                    new CountryDTO { Code = "AT", Name = "Austria" },
                    new CountryDTO { Code = "BE", Name = "Belgium" },
                    new CountryDTO { Code = "BR", Name = "Brazil" },
                    new CountryDTO { Code = "BG", Name = "Bulgaria" },
                    new CountryDTO { Code = "CA", Name = "Canada" },
                    new CountryDTO { Code = "CN", Name = "China" },
                    new CountryDTO { Code = "HR", Name = "Croatia" },
                    new CountryDTO { Code = "CZ", Name = "Czech Republic" },
                    new CountryDTO { Code = "DK", Name = "Denmark" },
                    new CountryDTO { Code = "EE", Name = "Estonia" },
                    new CountryDTO { Code = "FI", Name = "Finland" },
                    new CountryDTO { Code = "FR", Name = "France" },
                    new CountryDTO { Code = "DE", Name = "Germany" },
                    new CountryDTO { Code = "GR", Name = "Greece" },
                    new CountryDTO { Code = "HK", Name = "Hong Kong" },
                    new CountryDTO { Code = "HU", Name = "Hungary" },
                    new CountryDTO { Code = "IS", Name = "Iceland" },
                    new CountryDTO { Code = "IN", Name = "India" },
                    new CountryDTO { Code = "ID", Name = "Indonesia" },
                    new CountryDTO { Code = "IE", Name = "Ireland" },
                    new CountryDTO { Code = "IL", Name = "Israel" },
                    new CountryDTO { Code = "IT", Name = "Italy" },
                    new CountryDTO { Code = "JP", Name = "Japan" },
                    new CountryDTO { Code = "KR", Name = "South Korea" },
                    new CountryDTO { Code = "LV", Name = "Latvia" },
                    new CountryDTO { Code = "LT", Name = "Lithuania" },
                    new CountryDTO { Code = "LU", Name = "Luxembourg" },
                    new CountryDTO { Code = "MY", Name = "Malaysia" },
                    new CountryDTO { Code = "MX", Name = "Mexico" },
                    new CountryDTO { Code = "NL", Name = "Netherlands" },
                    new CountryDTO { Code = "NZ", Name = "New Zealand" },
                    new CountryDTO { Code = "NO", Name = "Norway" },
                    new CountryDTO { Code = "PL", Name = "Poland" },
                    new CountryDTO { Code = "PT", Name = "Portugal" },
                    new CountryDTO { Code = "RO", Name = "Romania" },
                    new CountryDTO { Code = "RU", Name = "Russia" },
                    new CountryDTO { Code = "SA", Name = "Saudi Arabia" },
                    new CountryDTO { Code = "RS", Name = "Serbia" },
                    new CountryDTO { Code = "SG", Name = "Singapore" },
                    new CountryDTO { Code = "SK", Name = "Slovakia" },
                    new CountryDTO { Code = "SI", Name = "Slovenia" },
                    new CountryDTO { Code = "ZA", Name = "South Africa" },
                    new CountryDTO { Code = "ES", Name = "Spain" },
                    new CountryDTO { Code = "SE", Name = "Sweden" },
                    new CountryDTO { Code = "CH", Name = "Switzerland" },
                    new CountryDTO { Code = "TW", Name = "Taiwan" },
                    new CountryDTO { Code = "TH", Name = "Thailand" },
                    new CountryDTO { Code = "TR", Name = "Turkey" },
                    new CountryDTO { Code = "UA", Name = "Ukraine" },
                    new CountryDTO { Code = "AE", Name = "United Arab Emirates" },
                    new CountryDTO { Code = "GB", Name = "United Kingdom" },
                    new CountryDTO { Code = "US", Name = "United States" },
                    new CountryDTO { Code = "VN", Name = "Vietnam" }
                };

                return Ok(countries.OrderBy(c => c.Name).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting countries list");
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting countries", ex));
            }
        }

        /// <summary>
        /// Parses the WhitelistedSteamIds column — supports both new JSON format
        /// and the old comma-separated format for backward compatibility.
        /// </summary>
        private static List<WhitelistEntryDTO> ParseWhitelistEntries(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<WhitelistEntryDTO>();

            try
            {
                return JsonSerializer.Deserialize<List<WhitelistEntryDTO>>(raw)
                       ?? new List<WhitelistEntryDTO>();
            }
            catch
            {
                // Fall back to old comma-separated plain SteamID format
                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(id => new WhitelistEntryDTO { SteamId = id.Trim(), Comment = string.Empty })
                          .ToList();
            }
        }
    }
}
