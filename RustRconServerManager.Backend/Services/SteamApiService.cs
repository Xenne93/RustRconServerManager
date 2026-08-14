using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Interfaces;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service for interacting with the Steam Web API
/// </summary>
public class SteamApiService : ISteamApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<SteamApiService> _logger;
    private readonly AppDbContext _dbContext;

    private const string SteamApiBaseUrl = "https://api.steampowered.com";
    private const string GetPlayerSummariesEndpoint = "ISteamUser/GetPlayerSummaries/v0002";
    private const string GetPlayerBansEndpoint = "ISteamUser/GetPlayerBans/v1";
    private const string GetOwnedGamesEndpoint = "IPlayerService/GetOwnedGames/v0001";
    private const int RustAppId = 252490;

    public SteamApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SteamApiService> logger,
        AppDbContext dbContext)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiKey = configuration["SteamApi:ApiKey"] ?? throw new InvalidOperationException("Steam API key not configured");
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Fetches player country information from Steam API using GetPlayerSummaries
    /// </summary>
    public async Task<string?> GetPlayerCountryAsync(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            _logger.LogWarning("GetPlayerCountryAsync called with empty Steam ID");
            return null;
        }

        try
        {
            var url = $"{SteamApiBaseUrl}/{GetPlayerSummariesEndpoint}?key={_apiKey}&steamids={steamId}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Steam API request failed with status {response.StatusCode} for Steam ID {steamId}");
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonContent);

            var root = document.RootElement;

            if (!root.TryGetProperty("response", out var responseElement))
            {
                _logger.LogWarning($"No 'response' property in Steam API response for Steam ID {steamId}");
                return null;
            }

            if (!responseElement.TryGetProperty("players", out var playersElement) || playersElement.GetArrayLength() == 0)
            {
                _logger.LogWarning($"No players found in Steam API response for Steam ID {steamId}");
                return null;
            }

            var player = playersElement[0];

            if (player.TryGetProperty("loccountrycode", out var countryElement))
            {
                var country = countryElement.GetString();
                _logger.LogInformation($"Successfully fetched country '{country}' for Steam ID {steamId}");
                return country;
            }

            _logger.LogDebug($"No 'loccountrycode' property found for Steam ID {steamId}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"HTTP request error while fetching country for Steam ID {steamId}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"JSON parsing error while fetching country for Steam ID {steamId}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error while fetching country for Steam ID {steamId}");
            return null;
        }
    }

    /// <summary>
    /// Fetches player VAC ban information from Steam API using GetPlayerBans
    /// Returns tuple: (VACBanned, NumberOfVACBans, DaysSinceLastBan)
    /// </summary>
    public async Task<(bool VACBanned, int? NumberOfVACBans, int? DaysSinceLastBan)?> GetPlayerVACBanInfoAsync(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            _logger.LogWarning("GetPlayerVACBanInfoAsync called with empty Steam ID");
            return null;
        }

        // Developer mode: let admins assign fake VAC-ban profiles to specific SteamIDs to
        // test the VAC-ban protection rules without needing a real banned account. Ignored
        // entirely unless developer mode is on, even if overrides still exist in the table.
        var developerModeEnabled = await _dbContext.PanelSettings.AnyAsync(ps => ps.DeveloperModeEnabled);
        if (developerModeEnabled)
        {
            var overrideProfile = await _dbContext.DeveloperVacBanOverrides
                .FirstOrDefaultAsync(o => o.SteamId == steamId);

            if (overrideProfile != null)
            {
                _logger.LogWarning("[DEV MODE] Returning overridden VAC ban profile for SteamID {SteamId}: VACBanned={VACBanned}, Bans={Bans}, DaysSinceLastBan={Days}",
                    steamId, overrideProfile.VACBanned, overrideProfile.NumberOfVACBans, overrideProfile.DaysSinceLastBan);
                return (overrideProfile.VACBanned, overrideProfile.NumberOfVACBans, overrideProfile.DaysSinceLastBan);
            }
        }

        try
        {
            var url = $"{SteamApiBaseUrl}/{GetPlayerBansEndpoint}?key={_apiKey}&steamids={steamId}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Steam API request failed with status {response.StatusCode} for Steam ID {steamId}");
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonContent);

            var root = document.RootElement;

            if (!root.TryGetProperty("players", out var playersElement) || playersElement.GetArrayLength() == 0)
            {
                _logger.LogWarning($"No players found in Steam API ban response for Steam ID {steamId}");
                return null;
            }

            var player = playersElement[0];

            // Get VAC ban status
            bool vacBanned = false;
            if (player.TryGetProperty("VACBanned", out var vacBannedElement))
            {
                vacBanned = vacBannedElement.GetBoolean();
            }

            // Get number of VAC bans
            int? numberOfVACBans = null;
            if (player.TryGetProperty("NumberOfVACBans", out var numberOfVACBansElement))
            {
                numberOfVACBans = numberOfVACBansElement.GetInt32();
            }

            // Get days since last ban
            int? daysSinceLastBan = null;
            if (player.TryGetProperty("DaysSinceLastBan", out var daysSinceLastBanElement))
            {
                daysSinceLastBan = daysSinceLastBanElement.GetInt32();
            }

            _logger.LogInformation($"Successfully fetched VAC ban info for Steam ID {steamId}: VACBanned={vacBanned}, Bans={numberOfVACBans}, DaysSince={daysSinceLastBan}");
            return (vacBanned, numberOfVACBans, daysSinceLastBan);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"HTTP request error while fetching VAC ban info for Steam ID {steamId}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"JSON parsing error while fetching VAC ban info for Steam ID {steamId}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error while fetching VAC ban info for Steam ID {steamId}");
            return null;
        }
    }

    /// <summary>
    /// Fetches player avatar URL from Steam API
    /// Returns the Steam CDN URL for the avatar (avatarmedium - 64x64px)
    /// </summary>
    public async Task<string?> GetPlayerAvatarAsync(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            _logger.LogWarning("GetPlayerAvatarAsync called with empty Steam ID");
            return null;
        }

        try
        {
            var url = $"{SteamApiBaseUrl}/{GetPlayerSummariesEndpoint}?key={_apiKey}&steamids={steamId}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Steam API request failed with status {response.StatusCode} for Steam ID {steamId}");
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonContent);

            var root = document.RootElement;

            if (!root.TryGetProperty("response", out var responseElement))
            {
                _logger.LogWarning($"No 'response' property in Steam API response for Steam ID {steamId}");
                return null;
            }

            if (!responseElement.TryGetProperty("players", out var playersElement) || playersElement.GetArrayLength() == 0)
            {
                _logger.LogWarning($"No players found in Steam API response for Steam ID {steamId}");
                return null;
            }

            var player = playersElement[0];

            // Try to get avatarmedium (64x64px) first, fallback to avatarfull or avatar
            if (player.TryGetProperty("avatarmedium", out var avatarMediumElement))
            {
                var avatarUrl = avatarMediumElement.GetString();
                _logger.LogInformation($"Successfully fetched avatar URL (medium) for Steam ID {steamId}");
                return avatarUrl;
            }
            else if (player.TryGetProperty("avatarfull", out var avatarFullElement))
            {
                var avatarUrl = avatarFullElement.GetString();
                _logger.LogInformation($"Successfully fetched avatar URL (full) for Steam ID {steamId}");
                return avatarUrl;
            }
            else if (player.TryGetProperty("avatar", out var avatarElement))
            {
                var avatarUrl = avatarElement.GetString();
                _logger.LogInformation($"Successfully fetched avatar URL (small) for Steam ID {steamId}");
                return avatarUrl;
            }

            _logger.LogDebug($"No avatar property found for Steam ID {steamId}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"HTTP request error while fetching avatar for Steam ID {steamId}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"JSON parsing error while fetching avatar for Steam ID {steamId}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error while fetching avatar for Steam ID {steamId}");
            return null;
        }
    }

    /// <summary>
    /// Fetches player Steam account information (account age, Rust playtime, profile visibility)
    /// Returns tuple: (AccountCreated, RustPlaytimeMinutes, ProfileVisibility)
    /// ProfileVisibility: 1 = Private, 3 = Public
    /// </summary>
    public async Task<(DateTime? AccountCreated, int? RustPlaytimeMinutes, int? ProfileVisibility)?> GetPlayerSteamInfoAsync(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            _logger.LogWarning("GetPlayerSteamInfoAsync called with empty Steam ID");
            return null;
        }

        try
        {
            DateTime? accountCreated = null;
            int? profileVisibility = null;
            int? rustPlaytimeMinutes = null;

            // Step 1: Get account creation date and profile visibility from GetPlayerSummaries
            var summariesUrl = $"{SteamApiBaseUrl}/{GetPlayerSummariesEndpoint}?key={_apiKey}&steamids={steamId}";
            var summariesResponse = await _httpClient.GetAsync(summariesUrl);

            if (summariesResponse.IsSuccessStatusCode)
            {
                var summariesJson = await summariesResponse.Content.ReadAsStringAsync();
                using var summariesDoc = JsonDocument.Parse(summariesJson);
                var root = summariesDoc.RootElement;

                if (root.TryGetProperty("response", out var responseElement) &&
                    responseElement.TryGetProperty("players", out var playersElement) &&
                    playersElement.GetArrayLength() > 0)
                {
                    var player = playersElement[0];

                    // Get account creation date (Unix timestamp)
                    if (player.TryGetProperty("timecreated", out var timeCreatedElement))
                    {
                        long unixTimestamp = timeCreatedElement.GetInt64();
                        accountCreated = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
                        _logger.LogInformation($"Steam account created: {accountCreated} for Steam ID {steamId}");
                    }

                    // Get profile visibility (1 = private, 3 = public)
                    if (player.TryGetProperty("communityvisibilitystate", out var visibilityElement))
                    {
                        profileVisibility = visibilityElement.GetInt32();
                        _logger.LogInformation($"Profile visibility: {profileVisibility} for Steam ID {steamId}");
                    }
                }
            }

            // Step 2: Get Rust playtime from GetOwnedGames (only if profile is public)
            if (profileVisibility == 3) // Public profile
            {
                var gamesUrl = $"{SteamApiBaseUrl}/{GetOwnedGamesEndpoint}?key={_apiKey}&steamid={steamId}&include_played_free_games=1&appids_filter[0]={RustAppId}";
                var gamesResponse = await _httpClient.GetAsync(gamesUrl);

                if (gamesResponse.IsSuccessStatusCode)
                {
                    var gamesJson = await gamesResponse.Content.ReadAsStringAsync();
                    using var gamesDoc = JsonDocument.Parse(gamesJson);
                    var root = gamesDoc.RootElement;

                    if (root.TryGetProperty("response", out var responseElement) &&
                        responseElement.TryGetProperty("games", out var gamesElement) &&
                        gamesElement.GetArrayLength() > 0)
                    {
                        var rustGame = gamesElement[0];

                        // Get playtime in minutes
                        if (rustGame.TryGetProperty("playtime_forever", out var playtimeElement))
                        {
                            rustPlaytimeMinutes = playtimeElement.GetInt32();
                            _logger.LogInformation($"Rust playtime: {rustPlaytimeMinutes} minutes for Steam ID {steamId}");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"No Rust playtime found for Steam ID {steamId} (player may not own the game)");
                    }
                }
            }
            else
            {
                _logger.LogInformation($"Profile is private for Steam ID {steamId}, skipping playtime fetch");
            }

            _logger.LogInformation($"Successfully fetched Steam info for {steamId}: Created={accountCreated}, Playtime={rustPlaytimeMinutes}min, Visibility={profileVisibility}");
            return (accountCreated, rustPlaytimeMinutes, profileVisibility);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"HTTP request error while fetching Steam info for Steam ID {steamId}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"JSON parsing error while fetching Steam info for Steam ID {steamId}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error while fetching Steam info for Steam ID {steamId}");
            return null;
        }
    }
}
