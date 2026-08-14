using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.SignalRHubs;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Interfaces;
using Xenne.RCON;

namespace RustRconServerManager.Backend.Services;

public partial class RconBackgroundService : BackgroundService, IRconBackgroundService, IDisposable
{
    private readonly ILogger<RconBackgroundService> _logger;
    private readonly RconConnectionManager _rconConnectionManager;
    private readonly IHubContext<LiveConsoleHub> _liveConsoleHub;
    private readonly IHubContext<LiveChatHub> _liveChatHub;
    private readonly IRconPasswordsCryptoService _rconPasswordsCryptoService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TriggerExecutionService _triggerExecutionService;

    private readonly ConcurrentDictionary<int, RconServer> _servers = new();
    private readonly double _updateInterval = 30.0; // Tickrate in seconds for light periodic actions
    private readonly double _heavyUpdateInterval = 60.0; // Tickrate in seconds for heavy periodic actions (StatsHistory, etc.)
    private readonly double _cleanupInterval = 3600.0; // Cleanup interval in seconds (1 hour)

    // How often to check if hourly aggregation is needed (in seconds).
    // Note: This does NOT determine the aggregation granularity - stats are always aggregated into HOURLY buckets.
    // Running frequently ensures we don't miss aggregating an hour if the service restarts.
    private readonly double _aggregationInterval = 60.0;

    private readonly int _dataRetentionMinutes = 129600; // Keep data for 129600 minutes (90 days / 3 months)
    private readonly int _rawStatsRetentionDays = 7; // Keep raw StatsHistories for 7 days, then rely on aggregated data

    // Server status tracking for offline/online webhooks
    private readonly ConcurrentDictionary<int, bool> _serverOnlineStatus = new();
    private readonly ConcurrentDictionary<int, DateTime> _serverOfflineTimestamps = new();

    // Track consecutive empty banlists to prevent accidental ban deletion
    private readonly ConcurrentDictionary<int, int> _consecutiveEmptyBanlists = new();

    // Suppress player join/leave notifications during initial sync after (re)connect
    // When the service starts or reconnects, the first playerlist sync would trigger
    // notifications for ALL online players. This flag prevents that.
    private readonly ConcurrentDictionary<int, bool> _suppressPlayerEvents = new();

    public RconBackgroundService(
        RconConnectionManager rconConnectionManager,
        ILogger<RconBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        IHubContext<LiveConsoleHub> liveConsoleHub,
        IHubContext<LiveChatHub> liveChatHub,
        IRconPasswordsCryptoService rconPasswordsCryptoService,
        TriggerExecutionService triggerExecutionService)
    {
        _rconConnectionManager = rconConnectionManager ?? throw new ArgumentNullException(nameof(rconConnectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory;
        _liveConsoleHub = liveConsoleHub;
        _liveChatHub = liveChatHub;
        _rconPasswordsCryptoService = rconPasswordsCryptoService;
        _triggerExecutionService = triggerExecutionService ?? throw new ArgumentNullException(nameof(triggerExecutionService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogInformation("[RconBackgroundService] Starting...");

        // Set the RconBackgroundService reference in TriggerExecutionService to avoid circular dependency
        _triggerExecutionService.SetRconBackgroundService(this);

        await GetServersFromDatabase();
        await AdvanceStaleScheduledCommandsOnStartupAsync();
        _ = Task.Run(async () => await StartServerConnections(), stoppingToken);

        // Start the heavy operations loop in parallel
        _ = Task.Run(async () => await HeavyOperationsLoopAsync(stoppingToken), stoppingToken);

        // Start the cleanup loop in parallel
        _ = Task.Run(async () => await CleanupLoopAsync(stoppingToken), stoppingToken);

        // Start the stats aggregation loop in parallel
        _ = Task.Run(async () => await AggregationLoopAsync(stoppingToken), stoppingToken);

        // Start the scheduler loop in parallel (checks every 2 seconds for precise execution)
        _ = Task.Run(async () => await SchedulerLoopAsync(stoppingToken), stoppingToken);

        // Light operations loop (30 seconds)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_updateInterval), stoppingToken);
                _logger.LogDebug("[RconBackgroundService] Light periodic check running...");

                // Run each server's connectivity check/reconnect in parallel - a single offline
                // server used to block this loop for up to its 10s connect timeout before the
                // next server (and the periodic data grab below) was even looked at.
                await Task.WhenAll(_servers.Values.Select(ProcessServerConnectivityAsync));

                foreach (var kvp in _rconConnectionManager.GetAllClients())
                {
                    var client = kvp.Value;
                    if (client.IsConnected)
                    {
                        FireAndForget(() => GetPeriodicDataLight(client));
                    }
                }

                // Scheduled commands are handled by the dedicated SchedulerLoopAsync
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("[RconBackgroundService] Periodic loop cancelled.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RconBackgroundService] Unexpected error during periodic actions.");
            }
        }

        await StopRconServiceAsync();
        _logger.LogInformation("[RconBackgroundService] Stopped.");
    }

    /// <summary>
    /// Checks one server's connection status, attempts a reconnect if needed, and fires
    /// online/offline webhooks on state changes. Called for every server in parallel from the
    /// light loop - each server's own state lives under its own key in the ConcurrentDictionary
    /// fields below, so running these concurrently across servers is safe.
    /// </summary>
    private async Task ProcessServerConnectivityAsync(RconServer server)
    {
        bool isCurrentlyConnected = _rconConnectionManager.TryGetClient(server.Id, out var client) && client.IsConnected;
        bool wasOnline = _serverOnlineStatus.GetValueOrDefault(server.Id, true); // Assume online initially

        if (!isCurrentlyConnected)
        {
            // Server is disconnected
            if (wasOnline)
            {
                // Server just went offline
                _logger.LogWarning($"[Server {server.Id}] Server went OFFLINE. Recording timestamp.");
                _serverOnlineStatus[server.Id] = false;
                _serverOfflineTimestamps[server.Id] = DateTime.UtcNow;

                // Send ServerOffline webhook
                var serverId = server.Id;
                var serverName = server.Name;
                FireAndForget(async () =>
                {
                    using var webhookScope = _scopeFactory.CreateScope();
                    var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                    await webhookService.SendServerOfflineAsync(serverId, serverName);
                });
            }

            // Attempt to reconnect
            _logger.LogWarning($"[Server {server.Id}] Disconnected. Reconnecting...");
            await StartConnectionWithServer(server);

            // Check if reconnection succeeded
            bool reconnected = _rconConnectionManager.TryGetClient(server.Id, out var reconnectedClient) && reconnectedClient.IsConnected;
            if (reconnected && !wasOnline)
            {
                // Server came back online
                _logger.LogInformation($"[Server {server.Id}] Server is back ONLINE.");
                _serverOnlineStatus[server.Id] = true;

                // Calculate downtime
                int downtimeMinutes = 0;
                if (_serverOfflineTimestamps.TryGetValue(server.Id, out var offlineTime))
                {
                    downtimeMinutes = (int)(DateTime.UtcNow - offlineTime).TotalMinutes;
                    _serverOfflineTimestamps.TryRemove(server.Id, out _);
                }

                // Send ServerOnline webhook
                var serverId = server.Id;
                var serverName = server.Name;
                var downtime = downtimeMinutes;
                FireAndForget(async () =>
                {
                    using var webhookScope = _scopeFactory.CreateScope();
                    var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                    await webhookService.SendServerOnlineAsync(serverId, serverName, downtime);
                });
            }
        }
        else
        {
            // Server is connected
            if (!wasOnline)
            {
                // Server came back online (after being offline on previous check)
                _logger.LogInformation($"[Server {server.Id}] Server is back ONLINE.");
                _serverOnlineStatus[server.Id] = true;

                // Calculate downtime
                int downtimeMinutes = 0;
                if (_serverOfflineTimestamps.TryGetValue(server.Id, out var offlineTime))
                {
                    downtimeMinutes = (int)(DateTime.UtcNow - offlineTime).TotalMinutes;
                    _serverOfflineTimestamps.TryRemove(server.Id, out _);
                }

                // Send ServerOnline webhook
                var serverId = server.Id;
                var serverName = server.Name;
                var downtime = downtimeMinutes;
                FireAndForget(async () =>
                {
                    using var webhookScope = _scopeFactory.CreateScope();
                    var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                    await webhookService.SendServerOnlineAsync(serverId, serverName, downtime);
                });
            }
            else
            {
                // Server remains online (no status change)
                _serverOnlineStatus[server.Id] = true;
            }
        }
    }

    private async Task GetServersFromDatabase()
    {
        using var scope = _scopeFactory.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _logger.LogInformation("[RconBackgroundService] Loading servers from database...");

        List<RconServer> serverList = await dbContext.RconServers.ToListAsync();

        foreach (var server in serverList)
        {
            _servers.AddOrUpdate(server.Id, server, (key, existing) => server);
            _logger.LogInformation("[Server {ServerId}] Loaded: {ServerName}", server.Id, server.Name);
        }

        _logger.LogInformation("[RconBackgroundService] Servers loaded.");
    }

    private async Task StartServerConnections()
    {
        _logger.LogInformation("[RconBackgroundService] Establishing initial server connections...");
        var tasks = _servers.Values.Select(server => StartConnectionWithServer(server));
        await Task.WhenAll(tasks);
    }

    private async Task StopRconServiceAsync()
    {
        _logger.LogInformation("[RconBackgroundService] Disconnecting all clients...");

        foreach (var kvp in _rconConnectionManager.GetAllClients())
        {
            try
            {
                var client = kvp.Value;

                // Disconnect first, then dispose (Dispose will also clear event handlers)
                await client.DisconnectAsync();
                client.Dispose();

                _logger.LogInformation($"[Server {client.ServerId}] Disconnected and disposed.");
            }
            catch (ObjectDisposedException)
            {
                // Already disposed - this is fine
                _logger.LogDebug("[Server {ServerId}] Client was already disposed", kvp.Key);
            }
            catch (System.Net.WebSockets.WebSocketException ex)
            {
                // Connection already closed or error during disconnect - dispose anyway
                _logger.LogDebug("[Server {ServerId}] Disconnect error (connection may already be closed): {Message}",
                    kvp.Key, ex.Message);
                try { kvp.Value.Dispose(); } catch { /* Ignore dispose errors */ }
            }
            catch (Exception ex)
            {
                // Unexpected error during disconnect
                _logger.LogWarning(ex, "[Server {ServerId}] Unexpected error during disconnect", kvp.Key);
                try { kvp.Value.Dispose(); } catch { /* Ignore dispose errors */ }
            }
        }

        // Clear all clients from the connection manager
        _rconConnectionManager.ClearClients();
    }
}
