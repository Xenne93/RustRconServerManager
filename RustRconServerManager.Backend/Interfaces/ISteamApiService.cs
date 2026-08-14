namespace RustRconServerManager.Backend.Interfaces;

/// <summary>
/// Interface for Steam Web API interactions
/// </summary>
public interface ISteamApiService
{
    /// <summary>
    /// Fetches player country information from Steam API
    /// </summary>
    /// <param name="steamId">The Steam ID of the player</param>
    /// <returns>Country code (e.g., "US", "GB") or null if not found</returns>
    Task<string?> GetPlayerCountryAsync(string steamId);

    /// <summary>
    /// Fetches player VAC ban information from Steam API
    /// </summary>
    /// <param name="steamId">The Steam ID of the player</param>
    /// <returns>Tuple of (VACBanned, NumberOfVACBans, DaysSinceLastBan) or null if not found</returns>
    Task<(bool VACBanned, int? NumberOfVACBans, int? DaysSinceLastBan)?> GetPlayerVACBanInfoAsync(string steamId);

    /// <summary>
    /// Fetches player avatar URL from Steam API
    /// </summary>
    /// <param name="steamId">The Steam ID of the player</param>
    /// <returns>Avatar URL (avatarfull - 184x184px) or null if not found</returns>
    Task<string?> GetPlayerAvatarAsync(string steamId);

    /// <summary>
    /// Fetches player Steam account information (account age and Rust playtime)
    /// </summary>
    /// <param name="steamId">The Steam ID of the player</param>
    /// <returns>Tuple of (AccountCreated, RustPlaytimeMinutes, ProfileVisibility) or null if not found</returns>
    Task<(DateTime? AccountCreated, int? RustPlaytimeMinutes, int? ProfileVisibility)?> GetPlayerSteamInfoAsync(string steamId);
}
