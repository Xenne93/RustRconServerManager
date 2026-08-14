using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Shared.ServerProtection;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service that enforces server protection rules for connecting players
/// </summary>
public class PlayerProtectionService : IPlayerProtectionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerProtectionService> _logger;
    private readonly IIpGeolocationService _ipGeolocationService;
    private readonly ISteamApiService _steamApiService;

    public PlayerProtectionService(
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerProtectionService> logger,
        IIpGeolocationService ipGeolocationService,
        ISteamApiService steamApiService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _ipGeolocationService = ipGeolocationService;
        _steamApiService = steamApiService;
    }

    /// <summary>
    /// Checks if a player should be allowed to join based on server protection settings
    /// </summary>
    public async Task<PlayerProtectionResult> CheckPlayerAsync(int serverId, string steamId, string playerName, string ipAddress)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Load protection settings for this server
            var settings = await dbContext.ServerProtectionSettings
                .FirstOrDefaultAsync(s => s.ServerId == serverId);

            if (settings == null)
            {
                // No protection settings configured, allow player
                return new PlayerProtectionResult
                {
                    IsAllowed = true,
                    Reason = null,
                    Action = PlayerProtectionAction.None
                };
            }

            // Resolve the exempt SteamID list once — used by both whitelist-only and bypass logic
            IEnumerable<string> exemptIds = Enumerable.Empty<string>();
            if (!string.IsNullOrWhiteSpace(settings.WhitelistedSteamIds))
            {
                try
                {
                    var entries = JsonSerializer.Deserialize<List<WhitelistEntryDTO>>(settings.WhitelistedSteamIds);
                    exemptIds = entries?.Select(e => e.SteamId) ?? Enumerable.Empty<string>();
                }
                catch
                {
                    // Backward compat: old comma-separated plain SteamID format
                    exemptIds = settings.WhitelistedSteamIds
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => id.Trim());
                }
            }

            bool isExempt = exemptIds.Contains(steamId, StringComparer.OrdinalIgnoreCase);

            if (isExempt)
            {
                _logger.LogInformation($"[PROTECTION] Player {playerName} ({steamId}) is on the exempt list — bypassing all protections");
                return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
            }

            // Whitelist-only server mode: block everyone not on the exempt list
            if (settings.EnableWhitelistOnly)
            {
                string kickMessage = string.IsNullOrWhiteSpace(settings.WhitelistOnlyKickMessage)
                    ? "This server is restricted to whitelisted players only."
                    : settings.WhitelistOnlyKickMessage;

                _logger.LogWarning($"[PROTECTION] Blocking player {playerName} ({steamId}) — server is whitelist-only");

                return new PlayerProtectionResult
                {
                    IsAllowed = false,
                    Reason = kickMessage,
                    Action = PlayerProtectionAction.Kick
                };
            }

            // Check country-based protection
            if (settings.CountryFilterMode != CountryFilterMode.None)
            {
                var countryCheckResult = await CheckCountryProtectionAsync(settings, steamId, playerName, ipAddress, settings.BanDurationMinutes);
                if (!countryCheckResult.IsAllowed)
                {
                    return countryCheckResult;
                }
            }

            // Check VAC ban protection
            if (settings.EnableVacBanProtection)
            {
                var vacCheckResult = await CheckVacBanProtectionAsync(settings, steamId, playerName);
                if (!vacCheckResult.IsAllowed)
                {
                    return vacCheckResult;
                }
            }

            // Check private Steam profile protection
            if (settings.BlockPrivateSteamProfiles)
            {
                var privateProfileResult = await CheckPrivateProfileProtectionAsync(settings, steamId, playerName);
                if (!privateProfileResult.IsAllowed)
                {
                    return privateProfileResult;
                }
            }

            // Check VPN/proxy protection
            if (settings.EnableVpnProtection)
            {
                var vpnCheckResult = await CheckVpnProtectionAsync(settings, steamId, playerName, ipAddress);
                if (!vpnCheckResult.IsAllowed)
                {
                    return vpnCheckResult;
                }
            }

            // TODO: Check public ban protection when Steam API is integrated

            // Player passed all checks
            return new PlayerProtectionResult
            {
                IsAllowed = true,
                Reason = null,
                Action = PlayerProtectionAction.None
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SERVER {serverId}] Error checking player protection for {playerName} ({steamId})");

            // On error, allow player (fail-open to avoid blocking legitimate players)
            return new PlayerProtectionResult
            {
                IsAllowed = true,
                Reason = null,
                Action = PlayerProtectionAction.None
            };
        }
    }

    /// <summary>
    /// Checks VPN/proxy protection rules
    /// </summary>
    private async Task<PlayerProtectionResult> CheckVpnProtectionAsync(
        Models.ServerProtectionSettings settings,
        string steamId,
        string playerName,
        string ipAddress)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var proxyCheckService = scope.ServiceProvider.GetRequiredService<IProxyCheckService>();

            var vpnResult = await proxyCheckService.CheckIpAsync(ipAddress);

            if (vpnResult.IsVpn)
            {
                var action = settings.VpnProtectionAction == VpnProtectionAction.Ban
                    ? PlayerProtectionAction.Ban
                    : PlayerProtectionAction.Kick;

                _logger.LogWarning($"[PROTECTION] Player {playerName} ({steamId}) blocked — VPN/Proxy detected: {vpnResult.ProxyType} ({vpnResult.Provider})");

                return new PlayerProtectionResult
                {
                    IsAllowed = false,
                    Reason = "VPN/Proxy connections are not allowed on this server.",
                    Action = action,
                    BanDurationMinutes = settings.BanDurationMinutes
                };
            }

            return new PlayerProtectionResult { IsAllowed = true, Action = PlayerProtectionAction.None };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[PROTECTION] Error checking VPN for {playerName} ({steamId})");
            return new PlayerProtectionResult { IsAllowed = true, Action = PlayerProtectionAction.None };
        }
    }

    /// <summary>
    /// Checks country-based protection rules
    /// </summary>
    private async Task<PlayerProtectionResult> CheckCountryProtectionAsync(
        Models.ServerProtectionSettings settings,
        string steamId,
        string playerName,
        string ipAddress,
        int banDurationMinutes)
    {
        try
        {
            // Get country from IP address
            string? countryCode = await _ipGeolocationService.GetCountryFromIpAsync(ipAddress);

            if (string.IsNullOrEmpty(countryCode))
            {
                _logger.LogWarning($"Could not determine country for player {playerName} ({steamId}) with IP {ipAddress}");

                // If we can't determine country, allow player (fail-open)
                return new PlayerProtectionResult
                {
                    IsAllowed = true,
                    Reason = null,
                    Action = PlayerProtectionAction.None
                };
            }

            _logger.LogInformation($"Player {playerName} ({steamId}) is from country: {countryCode}");

            // Parse country list from comma-separated string
            var allowedCountries = string.IsNullOrWhiteSpace(settings.CountryList)
                ? new List<string>()
                : settings.CountryList.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

            bool isInList = allowedCountries.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

            // Check based on filter mode
            bool isBlocked = settings.CountryFilterMode switch
            {
                CountryFilterMode.Whitelist => !isInList, // Block if NOT in whitelist
                CountryFilterMode.Blacklist => isInList,   // Block if IN blacklist
                _ => false
            };

            if (isBlocked)
            {
                string filterType = settings.CountryFilterMode == CountryFilterMode.Whitelist ? "whitelist" : "blacklist";
                string reason = $"Country {countryCode} is not allowed (server {filterType})";

                _logger.LogWarning($"[PROTECTION] Blocking player {playerName} ({steamId}) from {countryCode} - {reason}");

                return new PlayerProtectionResult
                {
                    IsAllowed = false,
                    Reason = reason,
                    Action = PlayerProtectionAction.Ban, // Always ban for country violations
                    BanDurationMinutes = banDurationMinutes
                };
            }

            // Country is allowed
            return new PlayerProtectionResult
            {
                IsAllowed = true,
                Reason = null,
                Action = PlayerProtectionAction.None
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking country protection for player {playerName} ({steamId})");

            // On error, allow player (fail-open)
            return new PlayerProtectionResult
            {
                IsAllowed = true,
                Reason = null,
                Action = PlayerProtectionAction.None
            };
        }
    }

    /// <summary>
    /// Checks VAC ban protection rules (number of bans and/or days since last ban)
    /// </summary>
    private async Task<PlayerProtectionResult> CheckVacBanProtectionAsync(
        Models.ServerProtectionSettings settings,
        string steamId,
        string playerName)
    {
        try
        {
            var vacInfo = await _steamApiService.GetPlayerVACBanInfoAsync(steamId);

            if (vacInfo == null)
            {
                _logger.LogWarning($"[PROTECTION] Could not retrieve VAC ban info for {playerName} ({steamId}) — allowing player (fail-open)");
                return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
            }

            var (vacBanned, numberOfVacBans, daysSinceLastBan) = vacInfo.Value;

            // Check number-of-bans threshold (MaxVacBans > 0 means "block if bans > threshold")
            bool blockedByCount = settings.MaxVacBans > 0
                && vacBanned
                && (numberOfVacBans ?? 0) > settings.MaxVacBans;

            // Check recency threshold (MinDaysSinceLastVACBan > 0 means "block if last ban was within X days")
            bool blockedByRecency = settings.MinDaysSinceLastVACBan > 0
                && vacBanned
                && (daysSinceLastBan ?? int.MaxValue) < settings.MinDaysSinceLastVACBan;

            if (blockedByCount || blockedByRecency)
            {
                string reason = blockedByRecency
                    ? $"VAC ban received {daysSinceLastBan} days ago (minimum required: {settings.MinDaysSinceLastVACBan} days)"
                    : $"Player has {numberOfVacBans} VAC ban(s) (maximum allowed: {settings.MaxVacBans})";

                _logger.LogWarning($"[PROTECTION] Blocking player {playerName} ({steamId}) — {reason}");

                PlayerProtectionAction action = settings.VacBanAction == VacBanAction.Ban
                    ? PlayerProtectionAction.Ban
                    : PlayerProtectionAction.Kick;

                return new PlayerProtectionResult
                {
                    IsAllowed = false,
                    Reason = reason,
                    Action = action,
                    BanDurationMinutes = action == PlayerProtectionAction.Ban ? settings.BanDurationMinutes : 0
                };
            }

            return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking VAC ban protection for player {playerName} ({steamId})");

            // On error, allow player (fail-open)
            return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
        }
    }

    /// <summary>
    /// Checks if the player has a private Steam profile (ProfileVisibility = 1)
    /// </summary>
    private async Task<PlayerProtectionResult> CheckPrivateProfileProtectionAsync(
        Models.ServerProtectionSettings settings,
        string steamId,
        string playerName)
    {
        try
        {
            var steamInfo = await _steamApiService.GetPlayerSteamInfoAsync(steamId);

            if (steamInfo == null)
            {
                _logger.LogWarning($"[PROTECTION] Could not retrieve Steam info for {playerName} ({steamId}) — allowing player (fail-open)");
                return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
            }

            // ProfileVisibility: 1 = Private, 3 = Public
            bool isPrivate = steamInfo.Value.ProfileVisibility == 1;

            if (isPrivate)
            {
                string reason = "Private Steam profile is not allowed on this server";
                _logger.LogWarning($"[PROTECTION] Blocking player {playerName} ({steamId}) — {reason}");

                PlayerProtectionAction action = settings.PrivateSteamProfileAction == PrivateSteamProfileAction.Ban
                    ? PlayerProtectionAction.Ban
                    : PlayerProtectionAction.Kick;

                return new PlayerProtectionResult
                {
                    IsAllowed = false,
                    Reason = reason,
                    Action = action,
                    BanDurationMinutes = action == PlayerProtectionAction.Ban ? settings.BanDurationMinutes : 0
                };
            }

            return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking private profile protection for player {playerName} ({steamId})");

            // On error, allow player (fail-open)
            return new PlayerProtectionResult { IsAllowed = true, Reason = null, Action = PlayerProtectionAction.None };
        }
    }
}
