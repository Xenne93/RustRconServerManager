using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;

namespace RustRconServerManager.Backend.Services;

// Sends a daily anonymous usage check-in (server/player counts, panel version) straight to
// Plausible's Events API - not via the official tracking script, since that script hard-
// blocks localhost/private-IP hostnames client-side and, more importantly, this needs to
// keep reporting whether or not anyone ever opens the panel in a browser.
//
// Polls every PollInterval and sends whenever CheckInInterval has elapsed since
// PanelSettings.LastAnalyticsSentAt (persisted, not tracked only in memory - a plain
// "send at startup then sleep 24h" loop would mean a fresh install that opts in during
// setup (which only happens after this service has already started) waits a full day for
// its first check-in, and every restart would reset the clock instead of respecting an
// actual 24-hour cadence). Re-reads PanelSettings.AnalyticsEnabled on every poll, so
// toggling the setting off takes effect within one poll interval.
public class AnalyticsReportingService : BackgroundService
{
    private const string EventsEndpoint = "https://telemetry.xenne.eu/api/event";
    private const string Domain = "rustrconservermanager.xenne.eu";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CheckInInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AnalyticsReportingService> _logger;

    public AnalyticsReportingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        ILogger<AnalyticsReportingService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendSnapshotIfEnabledAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalyticsReportingService] Failed to send analytics check-in");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task SendSnapshotIfEnabledAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var panelSettings = await dbContext.PanelSettings.FirstOrDefaultAsync(stoppingToken);
        if (panelSettings == null || !panelSettings.AnalyticsEnabled) return;

        var dueAt = panelSettings.LastAnalyticsSentAt?.Add(CheckInInterval);
        if (dueAt.HasValue && dueAt.Value > DateTime.UtcNow) return;

        var servers = await dbContext.RconServers.CountAsync(stoppingToken);
        var players = await dbContext.SteamPlayers.CountAsync(stoppingToken);

        var versionFile = Path.Combine(_environment.ContentRootPath, ".version");
        var version = File.Exists(versionFile)
            ? (await File.ReadAllTextAsync(versionFile, stoppingToken)).Trim()
            : "dev";

        var payload = new
        {
            name = "server-stats",
            url = $"https://{Domain}/daily-checkin",
            domain = Domain,
            props = new Dictionary<string, object>
            {
                ["Servers"] = servers,
                ["Playerbasecount"] = players,
                ["Panel Version"] = version
            }
        };

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync(EventsEndpoint, payload, stoppingToken);

        panelSettings.LastAnalyticsSentAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken);

        _logger.LogInformation(
            "[AnalyticsReportingService] Sent daily check-in ({Servers} servers, {Players} players, version {Version}) - {Status}",
            servers, players, version, response.StatusCode);
    }
}
