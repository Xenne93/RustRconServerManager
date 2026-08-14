using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.PluginVersionCheck;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service for checking plugin versions against Codefling and Umod.
/// Uses a local DB cache (PluginVersionCache) — entries expire after 30 minutes.
/// </summary>
public class PluginVersionCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PluginVersionCheckService> _logger;
    private readonly AppDbContext _dbContext;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PluginVersionCheckService(
        HttpClient httpClient,
        ILogger<PluginVersionCheckService> logger,
        AppDbContext dbContext)
    {
        _httpClient = httpClient;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Check plugin version against configured source (Umod/Codefling).
    /// Returns null if no source is configured or if source is Custom.
    /// Order: local DB cache -> configured source API.
    /// </summary>
    public async Task<PluginVersionCheckResult?> CheckPluginVersionAsync(int serverId, string pluginName, string installedVersion)
    {
        try
        {
            var pluginSource = await _dbContext.ServerPluginSources
                .FirstOrDefaultAsync(sps => sps.RustServerId == serverId && sps.PluginName == pluginName);

            if (pluginSource == null)
            {
                _logger.LogInformation($"[NO SOURCE] Plugin {pluginName} has no source configured for server {serverId} - skipping version check");
                return null;
            }

            if (pluginSource.Source == PluginSource.Custom)
            {
                _logger.LogInformation($"[CUSTOM PLUGIN] Plugin {pluginName} is marked as custom for server {serverId} - skipping version check");
                return null;
            }

            _logger.LogInformation($"[SOURCE CHECK] Plugin {pluginName} configured with source {pluginSource.Source} for server {serverId}");

            // Step 1: local cache
            var cached = await GetFromCacheAsync(pluginName);
            if (cached != null)
            {
                _logger.LogInformation("[CACHE HIT] Plugin {PluginName} found in local cache", pluginName);
                return new PluginVersionCheckResult
                {
                    PluginName = pluginName,
                    InstalledVersion = installedVersion,
                    LatestVersion = cached.LatestVersion,
                    IsUpToDate = CompareVersions(installedVersion, cached.LatestVersion ?? "0.0.0"),
                    PluginUrl = cached.PluginUrl,
                    Source = cached.Source
                };
            }

            _logger.LogInformation($"[CACHE MISS] Plugin {pluginName} not in local cache, checking {pluginSource.Source} API...");

            // Step 2: configured source
            PluginVersionCheckResult? result = null;
            int? umodRateLimitRemaining = null;
            int? umodRateLimitTotal = null;

            if (pluginSource.Source == PluginSource.Codefling)
            {
                var codeflingResult = await CheckCodeflingAsync($"{pluginName}.cs")
                                      ?? await CheckCodeflingAsync($"{pluginName}.zip");
                if (codeflingResult != null)
                {
                    result = CreateResult(pluginName, installedVersion, codeflingResult.Version, codeflingResult.Url, PluginSource.Codefling);
                }
            }
            else if (pluginSource.Source == PluginSource.Umod)
            {
                var umodResult = await CheckUmodAsync(pluginName);
                if (umodResult != null)
                {
                    result = CreateResult(pluginName, installedVersion, umodResult.Latest_Release_Version, umodResult.Url, PluginSource.Umod);
                    umodRateLimitRemaining = umodResult.RateLimitRemaining;
                    umodRateLimitTotal = umodResult.RateLimitTotal;
                }
            }

            if (result != null)
            {
                await SaveToCacheAsync(result, umodRateLimitRemaining, umodRateLimitTotal);
                return result;
            }

            _logger.LogWarning($"[NOT FOUND] Plugin {pluginName} not found on {pluginSource.Source}");
            return new PluginVersionCheckResult
            {
                PluginName = pluginName,
                InstalledVersion = installedVersion,
                LatestVersion = null,
                IsUpToDate = false,
                PluginUrl = null,
                Source = pluginSource.Source,
                NotFound = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking version for plugin {pluginName}");
            return new PluginVersionCheckResult
            {
                PluginName = pluginName,
                InstalledVersion = installedVersion,
                LatestVersion = null,
                IsUpToDate = false,
                PluginUrl = null,
                Source = null,
                NotFound = false
            };
        }
    }

    /// <summary>
    /// Check multiple plugin versions at once. Pulls fresh entries from the local cache
    /// in one query, then hits the external APIs for cache misses.
    /// </summary>
    public async Task<Dictionary<string, PluginVersionCheckResult?>> CheckPluginVersionsBatchAsync(
        int serverId,
        List<(string PluginName, string InstalledVersion)> plugins)
    {
        var results = new Dictionary<string, PluginVersionCheckResult?>();

        if (plugins.Count == 0)
            return results;

        var pluginNames = plugins.Select(p => p.PluginName).ToList();

        var pluginSources = await _dbContext.ServerPluginSources
            .Where(sps => sps.RustServerId == serverId && pluginNames.Contains(sps.PluginName))
            .ToDictionaryAsync(sps => sps.PluginName, sps => sps.Source);

        // Plugins without a source / Custom plugins → null result
        foreach (var plugin in plugins)
        {
            if (!pluginSources.TryGetValue(plugin.PluginName, out var source) || source == PluginSource.Custom)
            {
                results[plugin.PluginName] = null;
            }
        }

        var pluginsToCheck = plugins
            .Where(p => pluginSources.TryGetValue(p.PluginName, out var s) && s != PluginSource.Custom)
            .ToList();

        if (pluginsToCheck.Count == 0)
            return results;

        // Bulk-load any non-expired cache entries
        var namesToCheck = pluginsToCheck.Select(p => p.PluginName).ToList();
        var now = DateTime.UtcNow;
        var cacheHits = await _dbContext.PluginVersionCache
            .Where(c => namesToCheck.Contains(c.PluginName) && c.ExpiresAt > now)
            .ToDictionaryAsync(c => c.PluginName, c => c);

        var misses = new List<(string PluginName, string InstalledVersion)>();

        foreach (var plugin in pluginsToCheck)
        {
            if (cacheHits.TryGetValue(plugin.PluginName, out var cached))
            {
                results[plugin.PluginName] = new PluginVersionCheckResult
                {
                    PluginName = plugin.PluginName,
                    InstalledVersion = plugin.InstalledVersion,
                    LatestVersion = cached.LatestVersion,
                    IsUpToDate = CompareVersions(plugin.InstalledVersion, cached.LatestVersion ?? "0.0.0"),
                    PluginUrl = cached.PluginUrl,
                    Source = cached.Source
                };
            }
            else
            {
                misses.Add(plugin);
            }
        }

        // Resolve cache misses via the source APIs (sequential to respect rate limits)
        foreach (var plugin in misses)
        {
            var result = await CheckPluginVersionAsync(serverId, plugin.PluginName, plugin.InstalledVersion);
            results[plugin.PluginName] = result;
        }

        return results;
    }

    private async Task<PluginVersionCache?> GetFromCacheAsync(string pluginName)
    {
        return await _dbContext.PluginVersionCache
            .Where(c => c.PluginName == pluginName && c.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync();
    }

    private async Task SaveToCacheAsync(PluginVersionCheckResult result, int? umodRateLimitRemaining, int? umodRateLimitTotal)
    {
        try
        {
            var existing = await _dbContext.PluginVersionCache
                .FirstOrDefaultAsync(c => c.PluginName == result.PluginName);

            var now = DateTime.UtcNow;
            var expiresAt = now.AddMinutes(30);

            if (existing != null)
            {
                existing.LatestVersion = result.LatestVersion;
                existing.PluginUrl = result.PluginUrl;
                existing.Source = result.Source;
                existing.CachedAt = now;
                existing.ExpiresAt = expiresAt;
                existing.UmodRateLimitRemaining = umodRateLimitRemaining;
                existing.UmodRateLimitTotal = umodRateLimitTotal;
                _logger.LogInformation($"[CACHE UPDATE] {result.PluginName} (expires {expiresAt:O})");
            }
            else
            {
                _dbContext.PluginVersionCache.Add(new PluginVersionCache
                {
                    PluginName = result.PluginName,
                    LatestVersion = result.LatestVersion,
                    PluginUrl = result.PluginUrl,
                    Source = result.Source,
                    CachedAt = now,
                    ExpiresAt = expiresAt,
                    UmodRateLimitRemaining = umodRateLimitRemaining,
                    UmodRateLimitTotal = umodRateLimitTotal
                });
                _logger.LogInformation($"[CACHE SAVE] {result.PluginName} (expires {expiresAt:O})");
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving plugin {result.PluginName} to cache");
        }
    }

    private async Task<CodeflingPluginResponse?> CheckCodeflingAsync(string fileName)
    {
        try
        {
            var url = $"https://www.codefling.com/db/?category=all&filename={Uri.EscapeDataString(fileName)}";
            _logger.LogInformation($"[CODEFLING] Checking filename '{fileName}' -> URL: {url}");

            var response = await _httpClient.GetAsync(url);
            _logger.LogInformation($"[CODEFLING] Response for '{fileName}': StatusCode={response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"[CODEFLING] Failed to fetch '{fileName}': {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var plugins = JsonSerializer.Deserialize<List<CodeflingPluginResponse>>(content, _jsonOptions);

            var plugin = plugins?.FirstOrDefault();
            if (plugin != null)
                _logger.LogInformation($"[CODEFLING] Found plugin '{plugin.Title}' v{plugin.Version}");
            else
                _logger.LogInformation($"[CODEFLING] No plugin found for '{fileName}'");

            return plugin;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[CODEFLING] Error checking {fileName}");
            return null;
        }
    }

    private async Task<UmodPluginResponseWithRateLimit?> CheckUmodAsync(string pluginName)
    {
        try
        {
            var slug = ConvertToSlug(pluginName);
            var url = $"https://umod.org/plugins/{slug}.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            int? rateLimitRemaining = null;
            int? rateLimitTotal = null;

            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
                && int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
            {
                rateLimitRemaining = remaining;
            }

            if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limitValues)
                && int.TryParse(limitValues.FirstOrDefault(), out var limit))
            {
                rateLimitTotal = limit;
            }

            if (rateLimitRemaining.HasValue && rateLimitTotal.HasValue)
            {
                _logger.LogInformation($"[UMOD RATE LIMIT] {rateLimitRemaining}/{rateLimitTotal} requests remaining");
                if (rateLimitRemaining.Value < 5)
                    _logger.LogWarning($"⚠️ [UMOD RATE LIMIT] Only {rateLimitRemaining} requests remaining!");
            }

            var content = await response.Content.ReadAsStringAsync();
            var plugin = JsonSerializer.Deserialize<UmodPluginResponse>(content, _jsonOptions);

            if (plugin == null) return null;

            return new UmodPluginResponseWithRateLimit
            {
                Name = plugin.Name,
                Latest_Release_Version = plugin.Latest_Release_Version,
                Url = plugin.Url,
                RateLimitRemaining = rateLimitRemaining,
                RateLimitTotal = rateLimitTotal
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error checking Umod for {pluginName}");
            return null;
        }
    }

    private string ConvertToSlug(string input) => input.ToLower();

    private PluginVersionCheckResult CreateResult(
        string pluginName,
        string installedVersion,
        string latestVersion,
        string pluginUrl,
        PluginSource source)
    {
        return new PluginVersionCheckResult
        {
            PluginName = pluginName,
            InstalledVersion = installedVersion,
            LatestVersion = latestVersion,
            IsUpToDate = CompareVersions(installedVersion, latestVersion),
            PluginUrl = pluginUrl,
            Source = source
        };
    }

    private bool CompareVersions(string installed, string latest)
    {
        try
        {
            if (Version.TryParse(installed, out var installedVer) &&
                Version.TryParse(latest, out var latestVer))
            {
                return installedVer >= latestVer;
            }

            return string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private class UmodPluginResponseWithRateLimit
    {
        public string Name { get; set; } = string.Empty;
        public string Latest_Release_Version { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int? RateLimitRemaining { get; set; }
        public int? RateLimitTotal { get; set; }
    }
}
