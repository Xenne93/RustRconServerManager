using System.Text.Json;
using Microsoft.Extensions.Logging;
using RustRconServerManager.Backend.Interfaces;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service for determining player country from IP address using geolocation API
/// </summary>
public class IpGeolocationService : IIpGeolocationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IpGeolocationService> _logger;

    // Using ip-api.com for geolocation (free tier available)
    // Rate limit: 45 requests per minute for free tier
    private const string IpApiBaseUrl = "http://ip-api.com/json";

    // Cache to avoid repeated lookups for the same IP
    private readonly Dictionary<string, (string? Country, DateTime CachedAt)> _cache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

    public IpGeolocationService(HttpClient httpClient, ILogger<IpGeolocationService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the country code for an IP address using geolocation API
    /// </summary>
    public async Task<string?> GetCountryFromIpAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            _logger.LogWarning("GetCountryFromIpAsync called with empty IP address");
            return null;
        }

        // Extract IP from "IP:PORT" format if present
        string cleanIp = ipAddress;
        if (ipAddress.Contains(':'))
        {
            cleanIp = ipAddress.Split(':')[0];
            _logger.LogDebug($"Extracted IP {cleanIp} from {ipAddress}");
        }

        // Check cache first
        if (_cache.TryGetValue(cleanIp, out var cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < _cacheExpiration)
            {
                _logger.LogDebug($"Cache hit for IP {cleanIp}: {cached.Country}");
                return cached.Country;
            }
            else
            {
                // Remove expired cache entry
                _cache.Remove(cleanIp);
            }
        }

        try
        {
            var url = $"{IpApiBaseUrl}/{cleanIp}?fields=countryCode,status,message";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"IP geolocation request failed with status {response.StatusCode} for IP {cleanIp}");
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonContent);

            var root = document.RootElement;

            // Check if the request was successful
            if (root.TryGetProperty("status", out var statusElement))
            {
                string status = statusElement.GetString();
                if (status != "success")
                {
                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        _logger.LogWarning($"IP API returned status '{status}': {messageElement.GetString()} for IP {cleanIp}");
                    }
                    return null;
                }
            }

            // Extract country code
            if (root.TryGetProperty("countryCode", out var countryElement))
            {
                var country = countryElement.GetString();

                // Cache the result
                if (!string.IsNullOrEmpty(country))
                {
                    _cache[cleanIp] = (country, DateTime.UtcNow);
                    _logger.LogInformation($"Successfully determined country '{country}' for IP {cleanIp}");
                    return country;
                }
            }

            _logger.LogDebug($"No country code found in IP API response for IP {cleanIp}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"HTTP request error while fetching geolocation for IP {cleanIp}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"JSON parsing error while fetching geolocation for IP {cleanIp}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error while fetching geolocation for IP {cleanIp}");
            return null;
        }
    }
}
