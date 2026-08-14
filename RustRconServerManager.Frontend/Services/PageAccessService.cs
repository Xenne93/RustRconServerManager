using System.Net.Http.Json;

namespace RustRconServerManager.Frontend.Services;

/// <summary>
/// Service to check if the current user has access to specific pages
/// Used for moderator page permission checking
/// </summary>
public class PageAccessService
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, bool> _cache = new();

    public PageAccessService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Check if the current user has access to a specific page route
    /// </summary>
    /// <param name="pageRoute">The route to check (e.g., "dashboard", "players")</param>
    /// <returns>True if the user has access, false otherwise</returns>
    public async Task<bool> HasPageAccess(string pageRoute)
    {
        // Check cache first
        if (_cache.TryGetValue(pageRoute, out var cachedResult))
        {
            return cachedResult;
        }

        try
        {
            // No manual Authorization header needed — HttpOnly cookie is sent automatically
            var response = await _http.GetFromJsonAsync<AccessCheckResponse>($"/api/Moderator/CheckPageAccess/{pageRoute}");

            if (response != null)
            {
                // Cache the result
                _cache[pageRoute] = response.HasAccess;
                return response.HasAccess;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking page access for {pageRoute}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clear the access cache (useful after permission changes)
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    private class AccessCheckResponse
    {
        public bool HasAccess { get; set; }
    }
}
