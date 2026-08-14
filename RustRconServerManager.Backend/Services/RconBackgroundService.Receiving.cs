using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.SignalRHubs;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Shared.Rcon;
using Xenne.RCON;
using Xenne.RCON.Commands;
using Xenne.RCON.Models;

namespace RustRconServerManager.Backend.Services;


// This partial class of RconBackgroundService handles all incoming command answers.
// These commands are requested by the RconBackgroundService.Periodic partial class.

public partial class RconBackgroundService : BackgroundService, IRconBackgroundService
{

    // TODO: Replace functionality so the database does not select complete query but only executes the update async:
    // await db.RconServers.Where(r => r.Id == serverId).ExecuteUpdateAsync(s => s.SetProperty(r => r.QueryPort, _ => port));

    private async Task ServerCommandAnswerReceived(int serverId, string message, string command, RconCommandPurpose purpose)
    {
        switch (command)
        {
            case "playerlist":
                _logger.LogDebug("[Server {ServerId}] Starting Task ParsePlayerList", serverId);
                FireAndForget(() => ParsePlayerList(serverId, message));
                break;
            case "fps":
                _logger.LogDebug("[Server {ServerId}] Starting Task ParseFps", serverId);
                FireAndForget(() => ParseFps(serverId, message));
                break;
            case "server.queryport":
                _logger.LogDebug("[Server {ServerId}] Starting Task ParseServerQueryPort", serverId);
                FireAndForget(() => ParseServerQueryPort(serverId, message));
                break;
            case "server.port":
                _logger.LogDebug("[Server {ServerId}] Starting Task ParseServerPort", serverId);
                FireAndForget(() => ParseServerPort(serverId, message));
                break;
            case "server.hostname":
                _logger.LogDebug("[Server {ServerId}] Starting Task ParseServerHostname", serverId);
                FireAndForget(() => ParseServerHostname(serverId, message));
                break;
            case "serverinfo":
                _logger.LogDebug("[Server {ServerId}] Received serverinfo + {Message}", serverId, message);
                FireAndForget(() => ParseServerInfo(serverId, message));
                break;
            case "bans":
                _logger.LogDebug("[Server {ServerId}] Received server banlist + {Message}", serverId, message);
                FireAndForget(() => ParseServerBanList(serverId, message));
                break;
        }

        // Only persist and show in the live console if it's not a background polling command
        if (purpose != RconCommandPurpose.BackgroundPoll)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.RconLogEntries.Add(new RconLogEntry
                {
                    ServerId = serverId,
                    Message = message,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Server {ServerId}] Unable to persist command answer to console log", serverId);
            }

            await _liveConsoleHub.Clients
               .Group($"{LiveConsoleHub.ServerGroupPrefix}{serverId}")
                .SendAsync("ReceiveConsole", message);
        }
    }

    // Parse banlist
    private async Task ParseServerBanList(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogDebug("[Server {ServerId}] PARSING BANLIST: Starting ban list parse", serverId);

        try
        {
            // Deserialize the banlist JSON
            var banList = JsonSerializer.Deserialize<List<JsonElement>>(message);

            _logger.LogDebug("[Server {ServerId}] BANLIST: Received banlist with {Count} entries", serverId, banList?.Count ?? 0);

            // Get existing bans for this server to compare (do this before checking if banList is empty)
            var existingBans = await db.PlayerBans
                .Where(b => b.ServerId == serverId)
                .ToDictionaryAsync(b => b.SteamId);

            _logger.LogDebug("[Server {ServerId}] BANLIST: Found {Count} existing bans in database", serverId, existingBans.Count);

            if (banList == null || banList.Count == 0)
            {
                // Increment consecutive empty banlist counter
                int emptyCount = _consecutiveEmptyBanlists.AddOrUpdate(serverId, 1, (key, oldValue) => oldValue + 1);

                _logger.LogDebug("[Server {ServerId}] BANLIST: Empty banlist received ({Count}/3 consecutive)", serverId, emptyCount);

                // Only clear bans after 3 consecutive empty banlists to prevent race conditions
                if (emptyCount >= 3)
                {
                    _logger.LogDebug("[Server {ServerId}] BANLIST: Received 3 consecutive empty banlists - clearing all bans for this server", serverId);

                    // If the banlist is empty 3 times in a row, unban everyone
                    if (existingBans.Count > 0)
                    {
                        var allBansToRemove = existingBans.Values.ToList();
                        foreach (var ban in allBansToRemove)
                        {
                            // Update ban history to mark as inactive
                            var historyRecords = await db.PlayerBanHistories
                                .Where(h => h.SteamId == ban.SteamId && h.ServerId == serverId && h.IsActive)
                                .ToListAsync();

                            foreach (var history in historyRecords)
                            {
                                history.IsActive = false;
                                history.LiftedAt = DateTime.UtcNow;
                                history.LiftedBy = "Server (in-game unban)";
                                history.LiftReason = "Player was unbanned using server commands";
                                history.UpdatedAt = DateTime.UtcNow;
                            }

                            db.PlayerBans.Remove(ban);
                            _logger.LogDebug("[Server {ServerId}] BANLIST: Removing ban for {SteamId} ({Username}) - server banlist confirmed empty", serverId, ban.SteamId, ban.Username);
                        }

                        await db.SaveChangesAsync();
                        _logger.LogInformation("[Server {ServerId}] BANLIST: Unbanned {Count} player(s) - server banlist is now empty", serverId, allBansToRemove.Count);
                    }

                    // Reset counter after processing
                    _consecutiveEmptyBanlists.TryRemove(serverId, out _);
                }
                else
                {
                    _logger.LogDebug("[Server {ServerId}] BANLIST: Waiting for {Count} more consecutive empty banlist(s) before clearing bans", serverId, 3 - emptyCount);
                }

                return;
            }

            // Reset empty banlist counter when we receive a non-empty banlist
            _consecutiveEmptyBanlists.TryRemove(serverId, out _);

            _logger.LogDebug("[Server {ServerId}] BANLIST: Found {Count} bans", serverId, banList.Count);

            var now = DateTime.UtcNow;
            var bansToUpsert = new List<PlayerBan>();

            foreach (var banElement in banList)
            {
                try
                {
                    string steamIdStr = banElement.GetProperty("steamid").GetInt64().ToString();
                    string group = banElement.GetProperty("group").GetString() ?? "Banned";
                    string username = banElement.GetProperty("username").GetString() ?? "Unknown";
                    string notes = banElement.GetProperty("notes").GetString() ?? "";
                    long expiryUnix = banElement.GetProperty("expiry").GetInt64();

                    _logger.LogDebug("[Server {ServerId}] BANLIST: Processing ban - SteamID: {SteamId}, Username: {Username}, Expiry: {Expiry}", serverId, steamIdStr, username, expiryUnix);

                    if (existingBans.TryGetValue(steamIdStr, out var existingBan))
                    {
                        // Update existing ban
                        existingBan.Group = group;
                        existingBan.Username = username;
                        existingBan.Notes = notes;
                        existingBan.Expiry = expiryUnix;
                        existingBan.UpdatedAt = now;
                        db.PlayerBans.Update(existingBan);
                        _logger.LogDebug("[Server {ServerId}] BANLIST: Updated ban for {Username}", serverId, username);
                    }
                    else
                    {
                        // Create new ban record
                        var newBan = new PlayerBan
                        {
                            ServerId = serverId,
                            SteamId = steamIdStr,
                            Group = group,
                            Username = username,
                            Notes = notes,
                            Expiry = expiryUnix,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        db.PlayerBans.Add(newBan);
                        _logger.LogInformation("[Server {ServerId}] BANLIST: Added new ban for {Username}", serverId, username);

                        // Send Discord webhook for new ban
                        var banReason = string.IsNullOrEmpty(notes) ? "No reason provided" : notes;
                        var banDurationMinutes = CalculateBanDuration(expiryUnix);
                        var bannedUsername = username;
                        var bannedSteamId = steamIdStr;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var webhookScope = _scopeFactory.CreateScope();
                                var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                                await webhookService.SendPlayerBanAsync(serverId, bannedUsername, bannedSteamId, banReason, banDurationMinutes);
                            }
                            catch (Exception webhookEx)
                            {
                                _logger.LogError(webhookEx, "[Server {ServerId}] Error sending player ban webhook", serverId);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Server {ServerId}] BANLIST: Error processing individual ban entry", serverId);
                }
            }

            // Also check for unbanned players - remove bans that are no longer in the server's banlist
            var bannedSteamIds = banList
                .Select(b => b.GetProperty("steamid").GetInt64().ToString())
                .ToHashSet();

            var unbannedPlayers = new List<string>();

            foreach (var existingBan in existingBans.Values)
            {
                if (!bannedSteamIds.Contains(existingBan.SteamId))
                {
                    unbannedPlayers.Add(existingBan.SteamId);

                    // Update ban history to mark as inactive
                    var historyRecords = await db.PlayerBanHistories
                        .Where(h => h.SteamId == existingBan.SteamId && h.ServerId == serverId && h.IsActive)
                        .ToListAsync();

                    foreach (var history in historyRecords)
                    {
                        history.IsActive = false;
                        history.LiftedAt = DateTime.UtcNow;
                        history.LiftedBy = "Server (in-game unban)";
                        history.LiftReason = "Player was unbanned using server commands";
                        history.UpdatedAt = DateTime.UtcNow;
                    }

                    // This player was banned but is no longer in the banlist - they've been unbanned
                    db.PlayerBans.Remove(existingBan);
                    _logger.LogInformation("[Server {ServerId}] BANLIST: Removed ban for {SteamId} (Username: {Username}) - player was unbanned on server", serverId, existingBan.SteamId, existingBan.Username);
                }
            }

            await db.SaveChangesAsync();
            _logger.LogDebug("[Server {ServerId}] BANLIST: Successfully parsed and stored {Count} bans. Unbanned {UnbannedCount} player(s)", serverId, banList.Count, unbannedPlayers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Server {ServerId}] BANLIST ERROR: Unable to parse server ban list", serverId);
        }
    }

    // Helper method to convert Unix timestamp to DateTime
    private DateTime UnixTimeStampToDateTime(long unixTimeStamp)
    {
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(unixTimeStamp).ToUniversalTime();
        return dateTime;
    }

    // Helper method to calculate ban duration in minutes from Unix expiry timestamp
    private int CalculateBanDuration(long expiryUnix)
    {
        if (expiryUnix == 0) return 0; // Permanent ban
        var expiryTime = UnixTimeStampToDateTime(expiryUnix);
        var duration = expiryTime - DateTime.UtcNow;
        return duration.TotalMinutes > 0 ? (int)duration.TotalMinutes : 0;
    }

    // Parse serverinfo
    private async Task ParseServerInfo(int serverId, string message)
    {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    _logger.LogDebug("[Server {ServerId}] SERVER INFO: {Message}", serverId, message);

    try
    {
        var serverInfo = JsonSerializer.Deserialize<JsonElement>(message);
        int entityCount = serverInfo.GetProperty("EntityCount").GetInt32();
        string map = serverInfo.GetProperty("Map").GetString();
        int uptime = serverInfo.GetProperty("Uptime").GetInt32();
        int players = serverInfo.GetProperty("Players").GetInt32();
        int maxPlayers = serverInfo.GetProperty("MaxPlayers").GetInt32();
        int queuedPlayers = serverInfo.GetProperty("Queued").GetInt32();
        int joiningPlayers = serverInfo.GetProperty("Joining").GetInt32();
        int memory = serverInfo.GetProperty("Memory").GetInt32();
        int version = serverInfo.GetProperty("Version").GetInt32();
        string protocol = serverInfo.GetProperty("Protocol").GetString();
        float framerateFloat = serverInfo.GetProperty("Framerate").GetSingle();
        int framerate = (int)framerateFloat;  // Cast naar int, decimals verdwijnen (256.0 -> 256)

        await db.RconServers.Where(r => r.Id == serverId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LatestEntityCount, _ => entityCount)
                .SetProperty(r => r.LatestMap, _ => map)
                .SetProperty(r => r.LastSeen, _ => DateTime.UtcNow)
                .SetProperty(r => r.LatestUptime, _ => uptime)
                .SetProperty(r => r.LatestPlayerCount, _ => players)
                .SetProperty(r => r.LatestJoiningPlayers, _ => joiningPlayers)
                .SetProperty(r => r.LatestQueuedPlayers, _ => queuedPlayers)
                .SetProperty(r => r.LatestMemoryUsage, _ => memory)
                .SetProperty(r => r.LatestServerVersion, _ => version)
                .SetProperty(r => r.LatestServerProtocol, _ => protocol)
        );


                var now = DateTime.UtcNow;
                var stats = new List<StatsHistory>
                {
                    new() { ServerId = serverId, Stat = "entitycount", Value = entityCount.ToString(), CreatedAt = now },
                    new() { ServerId = serverId, Stat = "uptime", Value = uptime.ToString(), CreatedAt = now },
                    new() { ServerId = serverId, Stat = "players", Value = players.ToString(), CreatedAt = now },
                    new() { ServerId = serverId, Stat = "queued", Value = queuedPlayers.ToString(), CreatedAt = now },
                    new() { ServerId = serverId, Stat = "joining", Value = joiningPlayers.ToString(), CreatedAt = now },
                    new() { ServerId = serverId, Stat = "memory", Value = memory.ToString(), CreatedAt = now },
                    new() { ServerId = serverId, Stat = "framerate", Value = framerate.ToString(), CreatedAt = now },

                };

                await db.StatsHistories.AddRangeAsync(stats);
                await db.SaveChangesAsync();

    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Server {ServerId}] Unable to parse server info", serverId);
    }

    }

    // Parse the playerlist. Check if the player exists in the database and update or add the player (per server).
    // Also updates the current player count of the server.
    private async Task ParsePlayerList(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _logger.LogDebug("[Server {ServerId}] PLAYER LIST: {Message}", serverId, message);

        try
        {
            List<RustRconServerManager.Shared.Rcon.Rcon_PlayerInfoDTO> playerList = JsonSerializer.Deserialize<List<Rcon_PlayerInfoDTO>>(message);

            foreach (var player in playerList)
            {
                _logger.LogDebug("[Server {ServerId}] PLAYER: {DisplayName} ({SteamId})", serverId, player.DisplayName, player.SteamID);
            }

            // Add the latest playercount to the database
            await db.RconServers.Where(r => r.Id == serverId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.LatestPlayerCount, _ => playerList.Count));

            // If the list is empty (error or no players online, return
            if (playerList == null || playerList.Count == 0)
                return;

            // Get all SteamID's from the database
            var steamIds = playerList.Select(p => p.SteamID).ToList();

            var existingPlayers = await db.SteamPlayers
                .Where(p => steamIds.Contains(p.SteamId) && p.ServerId == serverId)
                .ToDictionaryAsync(p => p.SteamId);

            var now = DateTime.UtcNow;

            foreach (var player in playerList)
            {
                if (existingPlayers.TryGetValue(player.SteamID, out var existing))
                {
                    // Update existing player
                    existing.Name = player.DisplayName;
                    existing.LatestPing = player.Ping;
                    existing.LastIp = player.Address?.Contains(':') == true ? player.Address.Split(':')[0] : player.Address;
                    existing.LastSeen = now;
                    existing.IsOnline = true;  // Player is in the current playerlist
                    existing.LatestHealth = (float?)player.Health;

                    // Only update optional fields if present (RustAdmin plugin)
                    if (player.TeamId.HasValue)
                    {
                        existing.LatestTeamId = (int)player.TeamId.Value;
                    }

                    if (player.Position != null)
                    {
                        existing.LatestPositionX = player.Position.x;
                        existing.LatestPositionY = player.Position.y;
                        existing.LatestPositionZ = player.Position.z;
                    }

                    _logger.LogDebug("[Server {ServerId}] PLAYER (updated): {DisplayName} ({SteamId})", serverId, player.DisplayName, player.SteamID);
                    db.SteamPlayers.Update(existing);
                }
                else
                {
                    // New player - Country and VAC ban info will be set by OnPlayerConnected event
                    // This is a fallback in case OnPlayerConnected hasn't fired yet (e.g., panel started when server already has players)
                    var newPlayer = new SteamPlayer
                    {
                        Name = player.DisplayName,
                        SteamId = player.SteamID,
                        LastIp = player.Address?.Contains(':') == true ? player.Address.Split(':')[0] : player.Address,
                        LastSeen = now,
                        FirstSeen = now,
                        CreatedAt = now,
                        ServerId = serverId,
                        LatestPing = player.Ping,
                        LatestHealth = (float?)player.Health,
                        Country = null,  // Will be fetched in background task
                        IsOnline = true
                    };

                    // Only set optional fields if present (RustAdmin plugin)
                    if (player.TeamId.HasValue)
                    {
                        newPlayer.LatestTeamId = (int)player.TeamId.Value;
                    }

                    if (player.Position != null)
                    {
                        newPlayer.LatestPositionX = player.Position.x;
                        newPlayer.LatestPositionY = player.Position.y;
                        newPlayer.LatestPositionZ = player.Position.z;
                    }

                    _logger.LogDebug("[Server {ServerId}] PLAYER (new): {DisplayName} ({SteamId}) - fetching full data in background", serverId, player.DisplayName, player.SteamID);
                    db.SteamPlayers.Add(newPlayer);

                    // Trigger background task to fetch full player data (avatar, country, VAC info)
                    var playerSteamId = player.SteamID;
                    var playerName = player.DisplayName;
                    var playerIp = player.Address;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000); // Wait 2 seconds to let database commit
                        try
                        {
                            using var bgScope = _scopeFactory.CreateScope();
                            await FetchPlayerDataAsync(bgScope, serverId, playerSteamId, playerName, playerIp);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[Server {ServerId}] Error fetching player data for {PlayerName}", serverId, playerName);
                        }
                    });
                }
            }

            await db.SaveChangesAsync();

            // Clear the suppress flag after the first playerlist sync completes
            // From this point on, player join/leave events will trigger notifications normally
            if (_suppressPlayerEvents.TryGetValue(serverId, out var suppressed) && suppressed)
            {
                _suppressPlayerEvents[serverId] = false;
                _logger.LogInformation("[SERVER {ServerId}] Initial player sync complete - notifications enabled", serverId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Server {ServerId}] Unable to parse player list", serverId);
        }
    }


    private async Task ParseFps(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogDebug("[Server {ServerId}] FPS: {Message}", serverId, message);

        try
        {
            string[] splitMessage = message.Split(' ');
            int fps = int.Parse(splitMessage[0]);
            await db.RconServers.Where(r => r.Id == serverId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.LatestFpsCount, _ => fps)
                    .SetProperty(r => r.LastSeen, _ => DateTime.UtcNow)
                );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Server {ServerId}] Unable to store FPS", serverId);
        }

    }

    private async Task ParseServerQueryPort(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogDebug("[Server {ServerId}] SERVER QUERY PORT: {Message}", serverId, message);

        string[] splittedMessage = message.Split(": ");
        string cleanmessage = splittedMessage[1].Trim('"');



        try
        {
            int port = int.Parse(cleanmessage);
            await db.RconServers.Where(r => r.Id == serverId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.QueryPort, _ => port)
                    .SetProperty(r => r.LastSeen, _ => DateTime.UtcNow)
                );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Server {ServerId}] Unable to store query port", serverId);
        }

    }
    private async Task ParseServerPort(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogDebug("[Server {ServerId}] SERVER PORT: {Message}", serverId, message);
        string[] splittedMessage = message.Split(": ");
        string cleanmessage = splittedMessage[1].Trim('"');



        try
        {
            int port = int.Parse(cleanmessage);
            await db.RconServers.Where(r => r.Id == serverId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.GamePort, _ => port)
                    .SetProperty(r => r.LastSeen, _ => DateTime.UtcNow)
                );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Server {ServerId}] Unable to store server port", serverId);
        }
    }

    private async Task ParseServerHostname(int serverId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        string serverHostname = message.Replace("server.hostname: ", "");
        serverHostname = serverHostname.Trim('"');

        await db.RconServers.Where(r => r.Id == serverId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ServerHostname, _ => serverHostname)
            );

        _logger.LogDebug("[Server {ServerId}] SERVER HOSTNAME: {Hostname}", serverId, serverHostname);

    }


    private void FireAndForget(Func<Task> taskFunc)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await taskFunc();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fire-and-forget task crashed");
            }
        });
    }



}
