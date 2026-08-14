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

        GetServerBanList banlist = new GetServerBanList();
        FireAndForget(() => banlist.ExecuteAsync(client));

        // Mark players offline if they haven't been seen in 30+ seconds
        await MarkOfflinePlayersAsync(client.ServerId);
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
