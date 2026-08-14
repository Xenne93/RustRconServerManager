using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.SignalRHubs;
using RustRconServerManager.Shared.Scheduler;
using Xenne.RCON;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Partial class for RconBackgroundService handling scheduled command execution.
/// All times are in UTC — no timezone conversion needed.
/// </summary>
public partial class RconBackgroundService
{
    /// <summary>
    /// Called once on startup to advance any stale scheduled commands without executing them.
    /// </summary>
    private async Task AdvanceStaleScheduledCommandsOnStartupAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduledCommandService = scope.ServiceProvider.GetRequiredService<ScheduledCommandService>();

            await scheduledCommandService.AdvanceStaleCommandsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RconBackgroundService] Error advancing stale scheduled commands on startup");
        }
    }

    /// <summary>
    /// Dedicated scheduler loop that checks for due commands every 2 seconds.
    /// Runs independently from the main loop to ensure precise execution timing.
    /// </summary>
    private async Task SchedulerLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SchedulerLoop] Started — checking every 2 seconds");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                await ExecutePendingScheduledCommandsAsync();
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerLoop] Unexpected error");
            }
        }

        _logger.LogInformation("[SchedulerLoop] Stopped");
    }

    /// <summary>
    /// Executes pending scheduled commands.
    /// All NextExecutionAt values are in UTC, compared directly with DateTime.UtcNow.
    /// </summary>
    private async Task ExecutePendingScheduledCommandsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduledCommandService = scope.ServiceProvider.GetRequiredService<ScheduledCommandService>();

            var dueCommands = await scheduledCommandService.GetDueScheduledCommandsAsync();

            if (dueCommands.Count == 0)
                return;

            _logger.LogInformation($"[SchedulerLoop] Executing {dueCommands.Count} due command(s)");

            foreach (var command in dueCommands)
            {
                if (!_servers.TryGetValue(command.RconServerId, out var server))
                {
                    _logger.LogWarning($"[SchedulerLoop] Server {command.RconServerId} not found for command {command.Id}");
                    await scheduledCommandService.MarkAsExecutedAsync(command.Id, false, "Server not found");
                    continue;
                }

                if (!_rconConnectionManager.TryGetClient(command.RconServerId, out var client) || !client.IsConnected)
                {
                    _logger.LogWarning($"[SchedulerLoop] Server {command.RconServerId} is not connected");
                    await scheduledCommandService.MarkAsExecutedAsync(command.Id, false, "Server not connected");
                    continue;
                }

                try
                {
                    _logger.LogInformation($"[SchedulerLoop] Executing command {command.Id}: {command.Name} -> {command.Command}");

                    await client.SendCommandAsync(command.Command);

                    await scheduledCommandService.MarkAsExecutedAsync(command.Id, true, null);

                    await BroadcastScheduledCommandExecution(command, true, null);
                }
                catch (Exception ex)
                {
                    var errorMessage = ex.Message;
                    _logger.LogError(ex, $"[SchedulerLoop] Failed to execute command {command.Id}: {command.Command}");

                    await scheduledCommandService.MarkAsExecutedAsync(command.Id, false, errorMessage);

                    await BroadcastScheduledCommandExecution(command, false, errorMessage);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchedulerLoop] Error during scheduled command execution");
        }
    }

    /// <summary>
    /// Broadcasts scheduled command execution status to connected clients via SignalR.
    /// </summary>
    private async Task BroadcastScheduledCommandExecution(ScheduledCommand command, bool success, string errorMessage)
    {
        try
        {
            var message = success
                ? $"[SCHEDULED] Command '{command.Name}' executed successfully"
                : $"[SCHEDULED] Command '{command.Name}' failed: {errorMessage}";

            await _liveConsoleHub.Clients
                .Group($"server_{command.RconServerId}")
                .SendAsync("ReceiveConsole", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SchedulerLoop] Failed to broadcast execution of command {command.Id}");
        }
    }
}
