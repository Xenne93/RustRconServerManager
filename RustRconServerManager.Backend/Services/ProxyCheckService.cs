using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.Services;

public class ProxyCheckService : IProxyCheckService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProxyCheckService> _logger;

    public ProxyCheckService(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ProxyCheckService> logger)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<VpnCheckResult> CheckIpAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return new VpnCheckResult { IsVpn = false };

        // Clean IP (remove port if present)
        string cleanIp = ipAddress.Contains(':') ? ipAddress.Split(':')[0] : ipAddress;

        _logger.LogInformation("ProxyCheck: Checking IP {Ip}", cleanIp);

        // Check database cache first
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cached = await db.IpVpnCaches
            .FirstOrDefaultAsync(c => c.IpAddress == cleanIp && c.ExpiresAt > DateTime.UtcNow);

        if (cached != null)
        {
            _logger.LogInformation("ProxyCheck: Cache hit for IP {Ip}: IsVpn={IsVpn}", cleanIp, cached.IsVpn);
            return new VpnCheckResult
            {
                IsVpn = cached.IsVpn,
                ProxyType = cached.ProxyType,
                Provider = cached.Provider
            };
        }

        // Call proxycheck.io API
        var apiKey = _configuration["ProxyCheck:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("ProxyCheck: API key is not configured, skipping check");
            return new VpnCheckResult { IsVpn = false };
        }

        try
        {
            var url = $"https://proxycheck.io/v2/{cleanIp}?key={apiKey}&vpn=1&asn=1";
            _logger.LogInformation("ProxyCheck: Calling API for IP {Ip}", cleanIp);

            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("ProxyCheck: API response for {Ip}: Status={Status}, Body={Body}",
                cleanIp, response.StatusCode, json);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ProxyCheck: API returned HTTP {Status} for IP {Ip}", response.StatusCode, cleanIp);
                return new VpnCheckResult { IsVpn = false };
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // Check status
            if (root.TryGetProperty("status", out var status) && status.GetString() != "ok")
            {
                _logger.LogWarning("ProxyCheck: API returned status '{Status}' for IP {Ip}", status.GetString(), cleanIp);
                return new VpnCheckResult { IsVpn = false };
            }

            // Parse result for this IP
            bool isVpn = false;
            string? proxyType = null;
            string? provider = null;

            if (root.TryGetProperty(cleanIp, out var ipData))
            {
                if (ipData.TryGetProperty("proxy", out var proxyProp))
                    isVpn = proxyProp.GetString()?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;

                if (ipData.TryGetProperty("type", out var typeProp))
                    proxyType = typeProp.GetString();

                if (ipData.TryGetProperty("provider", out var providerProp))
                    provider = providerProp.GetString();
            }

            _logger.LogInformation("ProxyCheck: Result for {Ip}: IsVpn={IsVpn}, Type={Type}, Provider={Provider}",
                cleanIp, isVpn, proxyType ?? "N/A", provider ?? "N/A");

            // Cache the result in database for 1 day
            var oldEntry = await db.IpVpnCaches.FirstOrDefaultAsync(c => c.IpAddress == cleanIp);
            if (oldEntry != null)
            {
                oldEntry.IsVpn = isVpn;
                oldEntry.ProxyType = proxyType;
                oldEntry.Provider = provider;
                oldEntry.CheckedAt = DateTime.UtcNow;
                oldEntry.ExpiresAt = DateTime.UtcNow.AddDays(1);
            }
            else
            {
                db.IpVpnCaches.Add(new IpVpnCache
                {
                    IpAddress = cleanIp,
                    IsVpn = isVpn,
                    ProxyType = proxyType,
                    Provider = provider,
                    CheckedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(1)
                });
            }

            await db.SaveChangesAsync();

            return new VpnCheckResult
            {
                IsVpn = isVpn,
                ProxyType = proxyType,
                Provider = provider
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProxyCheck: Error checking IP {Ip}", cleanIp);
            return new VpnCheckResult { IsVpn = false };
        }
    }
}
