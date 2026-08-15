using Microsoft.Extensions.Hosting;
using RustRconServerManager.Backend.Interfaces;
using Xenne.RCON;
using Xenne.RCON.Commands;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Periodic data collection (light and heavy) loops.
/// </summary>
public partial class RconBackgroundService : BackgroundService, IRconBackgroundService
{
    /// <summary>
    /// Light periodic data grabber - runs every 30 seconds
    /// </summary>
    public async Task GetPeriodicDataLight(RconClient client)
    {
        GetPlayerListCommand playerlist = new GetPlayerListCommand();
        FireAndForget(() => playerlist.ExecuteAsync(client));

        GetServerFpsCommand fps = new GetServerFpsCommand();
        FireAndForget(() => fps.ExecuteAsync(client));

        GetServerQueryPort queryport = new GetServerQueryPort();
        FireAndForget(() => queryport.ExecuteAsync(client));

        GetServerGamePort gameport = new GetServerGamePort();
        FireAndForget(() => gameport.ExecuteAsync(client));

        GetServerHostname hostname = new GetServerHostname();
        FireAndForget(() => hostname.ExecuteAsync(client));

        // Mark players offline if they haven't been seen in 30+ seconds
        await MarkOfflinePlayersAsync(client.ServerId);
    }

    /// <summary>
    /// Ban list grabber - runs on its own faster loop (_banlistUpdateInterval) since in-game
    /// bans/unbans can only ever be detected by polling (see BanlistLoopAsync).
    /// </summary>
    public Task GetBanlistData(RconClient client)
    {
        GetServerBanList banlist = new GetServerBanList();
        FireAndForget(() => banlist.ExecuteAsync(client));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ban list polling loop - decoupled from the light loop and runs on a faster interval,
    /// since ban/unban state matters more for moderation than FPS/ports/hostname.
    /// </summary>
    private async Task BanlistLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RconBackgroundService] Ban list loop starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_banlistUpdateInterval), stoppingToken);
                _logger.LogDebug("[RconBackgroundService] Ban list check running...");

                foreach (var kvp in _rconConnectionManager.GetAllClients())
                {
                    var client = kvp.Value;
                    if (client.IsConnected)
                    {
                        // Skip this tick if the previous "bans" request for this server hasn't
                        // had its response parsed yet - avoids overlapping/out-of-order
                        // processing (see _banlistRequestInFlight) - unless that request is old
                        // enough its response was likely lost entirely, in which case go ahead
                        // rather than wedging polling for this server forever.
                        var now = DateTime.UtcNow;
                        bool previousStillInFlight = _banlistRequestInFlight.TryGetValue(client.ServerId, out var sentAt)
                            && now - sentAt <= BanlistRequestStaleAfter;

                        if (!previousStillInFlight)
                        {
                            _banlistRequestInFlight[client.ServerId] = now;
                            FireAndForget(() => GetBanlistData(client));
                        }
                        else
                        {
                            _logger.LogDebug("[Server {ServerId}] BANLIST: Skipping poll - previous request still in flight", client.ServerId);
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("[RconBackgroundService] Ban list loop cancelled.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RconBackgroundService] Unexpected error during ban list check.");
            }
        }

        _logger.LogInformation("[RconBackgroundService] Ban list loop stopped.");
    }

    /// <summary>
    /// Heavy periodic data grabber - runs every 60 seconds for resource-intensive tasks
    /// </summary>
    public async Task GetPeriodicDataHeavy(RconClient client)
    {
        // ServerInfo creates StatsHistory records - heavy operation
        GetServerInfo serverinfo = new GetServerInfo();
        FireAndForget(() => serverinfo.ExecuteAsync(client));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Heavy operations loop that runs every 60 seconds for resource-intensive tasks like StatsHistory
    /// </summary>
    private async Task HeavyOperationsLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RconBackgroundService] Heavy operations loop starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_heavyUpdateInterval), stoppingToken);
                _logger.LogDebug("[RconBackgroundService] Heavy periodic check running...");

                foreach (var kvp in _rconConnectionManager.GetAllClients())
                {
                    var client = kvp.Value;
                    if (client.IsConnected)
                    {
                        FireAndForget(() => GetPeriodicDataHeavy(client));
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("[RconBackgroundService] Heavy operations loop cancelled.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RconBackgroundService] Unexpected error during heavy periodic actions.");
            }
        }

        _logger.LogInformation("[RconBackgroundService] Heavy operations loop stopped.");
    }
}
