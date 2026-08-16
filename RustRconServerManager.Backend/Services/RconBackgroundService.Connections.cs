using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.SignalRHubs;
using RustRconServerManager.Backend.Models;
using Xenne.RCON;
using Xenne.RCON.Commands;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Server connections: event wiring, disconnect handling, and public RCON API.
/// </summary>
public partial class RconBackgroundService
{
    private async Task StartConnectionWithServer(RconServer server)
    {
        string pass = _rconPasswordsCryptoService.Decrypt(server.EncryptedPassword);
        string host = !string.IsNullOrEmpty(server.EncryptedHost)
            ? _rconPasswordsCryptoService.Decrypt(server.EncryptedHost)
            : string.Empty;

        // Suppress player join/leave events and periodic tasks until the first playerlist sync completes
        _suppressPlayerEvents[server.Id] = true;
        // Reset stale banlist counter from previous (disconnected) session
        _consecutiveEmptyBanlists.TryRemove(server.Id, out _);

        try
        {
            var client = new RconClient(host, server.RconPort, pass, server.Id);
            _rconConnectionManager.AddOrReplaceClient(server.Id, client);

            client.OnMessageReceived += async (sender, args) =>
            {
                _logger.LogInformation("[Server {ServerId}] Message: {Message}", args.ServerId, args.Message?.Replace("\r", "").Replace("\n", ""));
                await ServerMessageReceived(args.ServerId, args.Message);
            };

            client.OnCommandAnswerReceived += async (sender, args) =>
            {
                _logger.LogDebug("[Server {ServerId}] Answer: {Message}", args.ServerId, args.Message?.Replace("\r", "").Replace("\n", ""));
                await ServerCommandAnswerReceived(args.ServerId, args.Message, args.Command, args.Purpose);
            };

            client.OnConnectionClosed += async (sender, args) =>
            {
                _logger.LogWarning("[Server {0}] Disconnected.", args.ServerId);
                await OnDisconnectReceived(args.ServerId);
            };

            client.OnChatMessageReceived += async (sender, args) =>
            {
                _logger.LogWarning("Global chat received: {ChatMessage}", args.ChatMessage?.Replace("\r", "").Replace("\n", ""));
                await OnChatReceived(args.ServerId, args.ChatMessage, args.PlayerId, args.PlayerName, args.Channel.ToString());
            };

            client.OnPlayerKill += async (sender, args) =>
            {
                _logger.LogWarning("Player killed: {KillerName} killed {VictimName}", args.KillerName?.Replace("\r", "").Replace("\n", ""), args.VictimName?.Replace("\r", "").Replace("\n", ""));
                await PlayerKilled(args.ServerId, args.KillerName, args.KillerId, args.VictimName, args.VictimId, args.Position);
            };

            client.OnPlayerConnected += async (sender, args) =>
            {
                _logger.LogWarning("Player connected: {PlayerName}", args.PlayerName?.Replace("\r", "").Replace("\n", ""));
                // Note: args.PlayerId contains the player NAME, args.PlayerName contains the SteamId
                await OnPlayerConnectedAsync(args.ServerId, args.PlayerName, args.PlayerId, args.PlayerEndpoint);
            };

            client.OnPlayerDisconnected += async (sender, args) =>
            {
                _logger.LogInformation("Player disconnected: {PlayerName} ({PlayerId}) - Reason: {Reason}",
                    args.PlayerName?.Replace("\r", "").Replace("\n", ""), args.PlayerId, args.Reason?.Replace("\r", "").Replace("\n", ""));
                await OnPlayerDisconnectedAsync(args.ServerId, args.PlayerId, args.PlayerName, args.Reason);
            };

            client.OnPlayerReported += async (sender, args) =>
            {
                _logger.LogInformation("Player reported: {Reporter} reported {Reported} for {Type}",
                    args.ReporterName?.Replace("\r", "").Replace("\n", ""), args.ReportedName?.Replace("\r", "").Replace("\n", ""), args.Type?.Replace("\r", "").Replace("\n", ""));
                await OnPlayerReportedAsync(args.ServerId, args.ReporterName, args.ReporterId,
                    args.ReportedName, args.ReportedId, args.Subject, args.Message, args.Type);
            };


            // ConnectAsync already has a 10 second timeout built-in
            await client.ConnectAsync();
        }
        catch (System.Net.WebSockets.WebSocketException ex) when (ex.InnerException is System.Net.Http.HttpRequestException)
        {
            // Connection refused or network error - server is likely offline
            _logger.LogWarning("[Server {ServerId}] Unable to connect to {Host}:{Port} - Server appears to be offline or unreachable",
                server.Id, host, server.RconPort);
        }
        catch (System.Net.WebSockets.WebSocketException ex) when (ex.InnerException is System.Net.Sockets.SocketException socketEx)
        {
            // Socket-level error (connection refused, timeout, etc.)
            _logger.LogWarning("[Server {ServerId}] Connection failed to {Host}:{Port} - {SocketError}",
                server.Id, host, server.RconPort, socketEx.SocketErrorCode);
        }
        catch (System.Net.WebSockets.WebSocketException ex)
        {
            // Other WebSocket errors
            _logger.LogWarning("[Server {ServerId}] WebSocket connection failed to {Host}:{Port} - {Message}",
                server.Id, host, server.RconPort, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Connection timeout
            _logger.LogWarning("[Server {ServerId}] Connection timeout to {Host}:{Port}",
                server.Id, host, server.RconPort);
        }
        catch (Exception ex)
        {
            // Unexpected error - log with full details
            _logger.LogError(ex, "[Server {ServerId}] Unexpected error while connecting to {Host}:{Port}",
                server.Id, host, server.RconPort);
        }
    }

    private async Task ServerMessageReceived(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogInformation("[RconBackgroundService] Message from Server {ServerId}: {Message}", serverId, message?.Replace("\r", "").Replace("\n", ""));

        RconLogEntry logEntry = new RconLogEntry();
        logEntry.CreatedAt = DateTime.UtcNow;
        logEntry.Message = message;
        logEntry.ServerId = serverId;

        await dbContext.RconLogEntries.AddAsync(logEntry);
        await dbContext.SaveChangesAsync();

        await _liveConsoleHub.Clients
            .Group($"{LiveConsoleHub.ServerGroupPrefix}{serverId}")
            .SendAsync("ReceivedServerMessage", serverId, message);
    }

    public async Task HandleNewServer(RconServer server)
    {
        _logger.LogInformation("[RconBackgroundService] Handling new server connection for Server {ServerId}", server.Id);
        _servers.AddOrUpdate(server.Id, server, (key, existing) => server);
        await StartConnectionWithServer(server);
    }


    // This task is called when a server gets deleted.
    // Task deletes all database entries connected to that server.
    public async Task HandleDeleteServer(RconServer server)
    {
        using var scope = _scopeFactory.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Delete ChatMessages
        var chatMessages = await dbContext.ChatMessages.Where(c => c.ServerId == server.Id).ToListAsync();
        dbContext.ChatMessages.RemoveRange(chatMessages);
        await dbContext.SaveChangesAsync();

        // Delete PlayerKillLogs
        var playerKillLogs = await dbContext.PlayerKillLogs.Where(c => c.ServerId == server.Id).ToListAsync();
        dbContext.PlayerKillLogs.RemoveRange(playerKillLogs);
        await dbContext.SaveChangesAsync();

        // Delete SteamPlayers
        var steamPlayers = await dbContext.SteamPlayers.Where(c => c.ServerId == server.Id).ToListAsync();
        dbContext.SteamPlayers.RemoveRange(steamPlayers);
        await dbContext.SaveChangesAsync();

        // Delete RconServers
        dbContext.RconServers.Remove(server);
        await dbContext.SaveChangesAsync();

        _servers.Remove(server.Id, out _);
        _logger.LogInformation("[RconBackgroundService] Server {ServerId} deleted.", server.Id);
    }


    public void SendRconCommand(RconCommand command, int serverId)
    {
        if (_rconConnectionManager.TryGetClient(serverId, out var client))
        {
            client.ExecuteCommand(command);
        }
        else
        {
            _logger.LogError("[SendRconCommand] No client found for server {ServerId}", serverId);
        }
    }

    public void SendRconCommand(string command, int serverId)
    {
        var cmd = new CustomCommand(command);
        SendRconCommand(cmd, serverId);
    }

    /// <summary>
    /// Executes an RCON command and waits for the response
    /// </summary>
    /// <param name="command">The RCON command to execute</param>
    /// <param name="serverId">The server ID</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default 5000)</param>
    /// <returns>The command response, or null if timeout/error</returns>
    public async Task<string?> ExecuteCommandWithResponse(string command, int serverId, int timeoutMs = 5000)
    {
        if (!_rconConnectionManager.TryGetClient(serverId, out var client))
        {
            _logger.LogError("[ExecuteCommandWithResponse] No client found for server {ServerId}", serverId);
            return null;
        }

        if (!client.IsConnected)
        {
            _logger.LogError("[ExecuteCommandWithResponse] Client not connected for server {ServerId}", serverId);
            return null;
        }

        try
        {
            // Create the command
            var rconCommand = new CustomCommand(command);

            // Execute the command (this adds it to pending commands and sends it)
            await client.ExecuteCommand(rconCommand);

            // Wait for the response with timeout
            var startTime = DateTime.UtcNow;
            while (!rconCommand.isSent && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(50); // Poll every 50ms
            }

            // Check if we got a response
            if (rconCommand.isSent)
            {
                // Command was sent and response received (even if empty)
                string response = rconCommand.Answer?.Trim() ?? string.Empty;

                // Strip the command echo from the response
                // RCON responses often echo the command followed by the value
                if (response.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                {
                    // Remove the command part and return only the value
                    response = response.Substring(command.Length).Trim();

                    // Remove leading colon or quotes if present
                    response = response.TrimStart(':', ' ', '"').TrimEnd('"');
                }

                return response; // Return empty string if value is empty, not null
            }

            // Timeout - no response received
            _logger.LogWarning("[ExecuteCommandWithResponse] Timeout waiting for response from server {ServerId} for command {Command}", serverId, command);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ExecuteCommandWithResponse] Error executing command {Command} on server {ServerId}", command, serverId);
            return null;
        }
    }

    public Task<bool> IsServerConnected(int serverId)
    {
        return Task.FromResult(_rconConnectionManager.TryGetClient(serverId, out var client) && client.IsConnected);
    }

    public async Task<bool> OnDisconnectReceived(int serverId)
    {
        _logger.LogInformation("[RconBackgroundService] Handling disconnect for Server {ServerId}", serverId);
        try
        {
            _rconConnectionManager.RemoveClient(serverId);
            _logger.LogInformation("[RconBackgroundService] Removed client for Server {ServerId}", serverId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RconBackgroundService] Failed to remove client for Server {ServerId}", serverId);
            return false;
        }
    }

    public async Task<bool> DisconnectServerAsync(int serverId)
    {
        _logger.LogInformation("[RconBackgroundService] Disconnect requested for Server {ServerId}", serverId);


        if (!_rconConnectionManager.TryGetClient(serverId, out var client))
        {
            _logger.LogWarning("[RconBackgroundService] No client found for Server {ServerId}", serverId);
            return false;
        }

        try
        {
            // Disconnect the client
            await client.DisconnectAsync();

            // RemoveClient will dispose the client automatically
            _rconConnectionManager.RemoveClient(serverId);

            _logger.LogInformation("[RconBackgroundService] Server {ServerId} disconnected and removed.", serverId);
            _servers.Remove(serverId, out _);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RconBackgroundService] Failed to disconnect Server {ServerId}", serverId);
            return false;
        }
    }
}
