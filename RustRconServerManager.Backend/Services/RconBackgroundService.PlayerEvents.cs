using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Interfaces;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Player lifecycle: connect, disconnect, kill, report, data fetching, and offline marking.
/// </summary>
public partial class RconBackgroundService
{
    public async Task PlayerKilled(int serverId, string killerName, string killerId, string victimName, string victimId, string position)
    {
        // Check if at least one participant is a player (Steam ID)
        // Skip logging if both are NPCs (e.g., "scientist killed scientist")
        bool killerIsPlayer = IsSteamId(killerId);
        bool victimIsPlayer = IsSteamId(victimId);

        if (!killerIsPlayer && !victimIsPlayer)
        {
            // Both are NPCs, skip logging
            _logger.LogDebug($"[SERVER {serverId}] Skipping NPC vs NPC kill: {killerName} killed {victimName}");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        PlayerKillLog logEntry = new PlayerKillLog();

        logEntry.CreatedAt = DateTime.UtcNow;
        logEntry.KilledByName = killerName;
        logEntry.KilledById = killerId;
        logEntry.KilledPlayerName = victimName;
        logEntry.KilledPlayerId = victimId;
        logEntry.ServerId = serverId;

        // Determine if this is a PVP kill (both are players)
        // If only one is a player, it's a PVE kill
        logEntry.IsPVP = killerIsPlayer && victimIsPlayer;

        await db.PlayerKillLogs.AddAsync(logEntry);
        await db.SaveChangesAsync();

        // Execute triggers for player kill (killer perspective)
        _ = Task.Run(async () => await _triggerExecutionService.OnPlayerKillAsync(serverId, killerName, victimName));

        // Execute triggers for player death (victim perspective)
        _ = Task.Run(async () => await _triggerExecutionService.OnPlayerDeathAsync(serverId, victimName, killerName));

        // Send Discord webhook for player kill
        var isPVP = logEntry.IsPVP;
        _ = Task.Run(async () =>
        {
            try
            {
                using var webhookScope = _scopeFactory.CreateScope();
                var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                await webhookService.SendPlayerKillAsync(serverId, killerName, killerId, victimName, victimId, isPVP);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Error sending player kill webhook");
            }
        });
    }

    /// <summary>
    /// Fetches full player data (avatar, country, VAC info) for players created by ParsePlayerList
    /// </summary>
    private async Task FetchPlayerDataAsync(IServiceScope scope, int serverId, string steamId, string playerName, string ipAddress)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var steamApiService = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
            var ipGeolocationService = scope.ServiceProvider.GetRequiredService<IIpGeolocationService>();

            _logger.LogInformation($"[SERVER {serverId}] Background fetch started for player {playerName} ({steamId})");

            // Get player from database
            var player = await db.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);

            if (player == null)
            {
                _logger.LogWarning($"[SERVER {serverId}] Player {playerName} not found in database for background fetch");
                return;
            }

            bool updated = false;

            // Fetch country from IP
            if (string.IsNullOrEmpty(player.Country))
            {
                try
                {
                    var cleanIp = ipAddress?.Split(':')[0];
                    if (!string.IsNullOrEmpty(cleanIp))
                    {
                        var country = await ipGeolocationService.GetCountryFromIpAsync(cleanIp);
                        if (country != null)
                        {
                            player.Country = country;
                            updated = true;
                            _logger.LogInformation($"[SERVER {serverId}] Fetched country for {playerName}: {country}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch country for {playerName}");
                }
            }

            // Fetch avatar
            if (string.IsNullOrEmpty(player.Avatar))
            {
                try
                {
                    var avatarUrl = await steamApiService.GetPlayerAvatarAsync(steamId);
                    if (avatarUrl != null)
                    {
                        player.Avatar = avatarUrl;
                        player.AvatarLastUpdated = DateTime.UtcNow;
                        updated = true;
                        _logger.LogInformation($"[SERVER {serverId}] Fetched avatar for {playerName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch avatar for {playerName}");
                }
            }

            // Fetch VAC ban info
            try
            {
                var vacBanInfo = await steamApiService.GetPlayerVACBanInfoAsync(steamId);
                if (vacBanInfo.HasValue)
                {
                    player.VACBanned = vacBanInfo.Value.VACBanned;
                    player.NumberOfVACBans = vacBanInfo.Value.NumberOfVACBans;
                    player.DaysSinceLastVACBan = vacBanInfo.Value.DaysSinceLastBan;
                    updated = true;
                    _logger.LogInformation($"[SERVER {serverId}] Fetched VAC info for {playerName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch VAC info for {playerName}");
            }

            // Fetch Steam account info (account age, Rust playtime, profile visibility)
            try
            {
                var steamInfo = await steamApiService.GetPlayerSteamInfoAsync(steamId);
                if (steamInfo.HasValue)
                {
                    player.SteamAccountCreated = steamInfo.Value.AccountCreated;
                    player.RustPlaytimeMinutes = steamInfo.Value.RustPlaytimeMinutes;
                    player.ProfileVisibility = steamInfo.Value.ProfileVisibility;
                    updated = true;
                    _logger.LogInformation($"[SERVER {serverId}] Fetched Steam info for {playerName}: Created={steamInfo.Value.AccountCreated}, Playtime={steamInfo.Value.RustPlaytimeMinutes}min");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch Steam info for {playerName}");
            }

            // Save changes if anything was updated
            if (updated)
            {
                db.SteamPlayers.Update(player);
                await db.SaveChangesAsync();
                _logger.LogInformation($"[SERVER {serverId}] Successfully updated full data for {playerName}");

                // Send Discord webhook for player connect (for players that connected before panel started)
                var cleanIp = ipAddress?.Split(':')[0];
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var webhookScope = _scopeFactory.CreateScope();
                        var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                        await webhookService.SendPlayerConnectAsync(serverId, playerName, steamId, cleanIp ?? "", player.Country);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[SERVER {serverId}] Error sending delayed player connect webhook for {playerName}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SERVER {serverId}] Error in FetchPlayerDataAsync for {playerName}");
        }
    }

    /// <summary>
    /// Checks if the given ID is a valid Steam ID (17 digits)
    /// </summary>
    private bool IsSteamId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        // Steam IDs are 17 digits long and start with 7656119
        return id.Length == 17 && id.All(char.IsDigit);
    }

    /// <summary>
    /// Handles player connected event - fetches country from IP address
    /// </summary>
    public async Task OnPlayerConnectedAsync(int serverId, string steamId, string playerName, string ipAddress)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ipGeolocationService = scope.ServiceProvider.GetRequiredService<IIpGeolocationService>();
            var steamApiService = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
            var playerProtectionService = scope.ServiceProvider.GetRequiredService<IPlayerProtectionService>();

            _logger.LogInformation($"[SERVER {serverId}] Player connected: {playerName} ({steamId}) from {ipAddress}");

            if (string.IsNullOrEmpty(ipAddress))
            {
                _logger.LogWarning($"[SERVER {serverId}] Player {playerName} has no IP address");
                return;
            }

            // Extract clean IP address (remove port if present)
            string cleanIp = ipAddress;
            if (ipAddress.Contains(':'))
            {
                cleanIp = ipAddress.Split(':')[0];
                _logger.LogInformation($"[SERVER {serverId}] Extracted clean IP from '{ipAddress}' -> '{cleanIp}'");
            }

            // CHECK PLAYER PROTECTION RULES FIRST
            var protectionResult = await playerProtectionService.CheckPlayerAsync(serverId, steamId, playerName, cleanIp);

            if (!protectionResult.IsAllowed)
            {
                _logger.LogWarning($"[SERVER {serverId}] [PROTECTION] Player {playerName} ({steamId}) blocked: {protectionResult.Reason}");

                // Execute ban/kick action
                string actionTaken;
                if (protectionResult.Action == PlayerProtectionAction.Ban)
                {
                    // Use configured ban duration (0 = permanent)
                    string banCommand = protectionResult.BanDurationMinutes > 0
                        ? $"banid {steamId} \"{playerName}\" \"{protectionResult.Reason}\" {protectionResult.BanDurationMinutes}"
                        : $"banid {steamId} \"{playerName}\" \"{protectionResult.Reason}\"";

                    await ExecuteCommandWithResponse(banCommand, serverId);

                    string durationText = protectionResult.BanDurationMinutes > 0
                        ? $"for {protectionResult.BanDurationMinutes} hours"
                        : "permanently";

                    actionTaken = $"Banned {durationText}";
                    _logger.LogWarning($"[SERVER {serverId}] [PROTECTION] Banned player {playerName} ({steamId}) {durationText}: {protectionResult.Reason}");
                }
                else
                {
                    await ExecuteCommandWithResponse($"kick {steamId} \"{protectionResult.Reason}\"", serverId);
                    actionTaken = "Kicked";
                    _logger.LogWarning($"[SERVER {serverId}] [PROTECTION] Kicked player {playerName} ({steamId}): {protectionResult.Reason}");
                }

                // Send Server Protection Discord webhook and push notification
                var protectionReason = protectionResult.Reason ?? "Server protection";
                var protectionAction = actionTaken;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var webhookScope = _scopeFactory.CreateScope();
                        var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                        await webhookService.SendServerProtectionAsync(serverId, playerName, steamId, protectionReason, protectionAction);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[SERVER {serverId}] Error sending server protection webhook");
                    }
                });

                // Update player data in DB in the background so the admin still sees
                // accurate VAC / country info even though the player was blocked.
                _ = Task.Run(() => UpdateBlockedPlayerDbAsync(serverId, steamId, playerName, cleanIp));

                // Don't continue processing this player
                return;
            }

            // Fetch country from IP
            string? country = null;
            try
            {
                country = await ipGeolocationService.GetCountryFromIpAsync(cleanIp);
                _logger.LogInformation($"[SERVER {serverId}] Fetched country for {playerName} ({cleanIp}): {country ?? "NULL"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch country for IP {cleanIp}");
            }

            // Check VPN/proxy if enabled for this server
            var vpnSettings = await db.ServerProtectionSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ServerId == serverId);

            bool isVpn = false;
            if (vpnSettings?.EnableVpnCheck == true)
            {
                try
                {
                    var proxyCheckService = scope.ServiceProvider.GetRequiredService<IProxyCheckService>();
                    var vpnResult = await proxyCheckService.CheckIpAsync(cleanIp);
                    isVpn = vpnResult.IsVpn;
                    if (vpnResult.IsVpn)
                    {
                        _logger.LogWarning($"[SERVER {serverId}] [VPN] Player {playerName} ({steamId}) is using a VPN/Proxy: Type={vpnResult.ProxyType}, Provider={vpnResult.Provider}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[SERVER {serverId}] Failed to check VPN for IP {cleanIp}");
                }
            }

            // Record IP in player IP history
            try
            {
                var existingIpEntry = await db.PlayerIpHistories
                    .FirstOrDefaultAsync(h => h.SteamId == steamId && h.ServerId == serverId && h.IpAddress == cleanIp);

                if (existingIpEntry != null)
                {
                    existingIpEntry.LastUsed = DateTime.UtcNow;
                    existingIpEntry.TimesUsed++;
                    existingIpEntry.IsVpn = isVpn;
                    if (country != null) existingIpEntry.Country = country;
                }
                else
                {
                    db.PlayerIpHistories.Add(new Models.PlayerIpHistory
                    {
                        SteamId = steamId,
                        ServerId = serverId,
                        IpAddress = cleanIp,
                        IsVpn = isVpn,
                        Country = country,
                        FirstUsed = DateTime.UtcNow,
                        LastUsed = DateTime.UtcNow,
                        TimesUsed = 1
                    });
                }
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Failed to record IP history for {playerName} ({cleanIp})");
            }

            // Fetch VAC ban information from Steam API
            bool vacBanned = false;
            int? numberOfVACBans = null;
            int? daysSinceLastBan = null;
            try
            {
                var vacBanInfo = await steamApiService.GetPlayerVACBanInfoAsync(steamId);
                if (vacBanInfo.HasValue)
                {
                    vacBanned = vacBanInfo.Value.VACBanned;
                    numberOfVACBans = vacBanInfo.Value.NumberOfVACBans;
                    daysSinceLastBan = vacBanInfo.Value.DaysSinceLastBan;
                    _logger.LogInformation($"[SERVER {serverId}] Fetched VAC ban info for {playerName}: Banned={vacBanned}, Count={numberOfVACBans}, DaysSince={daysSinceLastBan}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch VAC ban info for {steamId}");
            }

            // Fetch Steam account information (account age, Rust playtime, profile visibility)
            DateTime? steamAccountCreated = null;
            int? rustPlaytimeMinutes = null;
            int? profileVisibility = null;
            try
            {
                var steamInfo = await steamApiService.GetPlayerSteamInfoAsync(steamId);
                if (steamInfo.HasValue)
                {
                    steamAccountCreated = steamInfo.Value.AccountCreated;
                    rustPlaytimeMinutes = steamInfo.Value.RustPlaytimeMinutes;
                    profileVisibility = steamInfo.Value.ProfileVisibility;
                    _logger.LogInformation($"[SERVER {serverId}] Fetched Steam info for {playerName}: Created={steamAccountCreated}, Playtime={rustPlaytimeMinutes}min, Visibility={profileVisibility}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch Steam info for {steamId}");
            }

            // Update or create player with country and VAC ban information
            var player = await db.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);

            if (player != null)
            {
                _logger.LogInformation($"[SERVER {serverId}] Found player {playerName} in database");

                bool updated = false;

                // Mark player online immediately on connect event
                if (!player.IsOnline)
                {
                    player.IsOnline = true;
                    player.LastSeen = DateTime.UtcNow;
                    updated = true;
                }

                // Always update IP on connect
                player.LastIp = cleanIp;
                updated = true;

                if (country != null)
                {
                    player.Country = country;
                    updated = true;
                }

                // Update VAC ban information
                player.VACBanned = vacBanned;
                player.NumberOfVACBans = numberOfVACBans;
                player.DaysSinceLastVACBan = daysSinceLastBan;
                updated = true;

                // Update Steam account information
                player.SteamAccountCreated = steamAccountCreated;
                player.RustPlaytimeMinutes = rustPlaytimeMinutes;
                player.ProfileVisibility = profileVisibility;

                // Check if avatar needs refresh (older than 7 days or never fetched)
                bool needsAvatarRefresh = player.AvatarLastUpdated == null ||
                                         (DateTime.UtcNow - player.AvatarLastUpdated.Value).TotalDays >= 7;

                if (needsAvatarRefresh)
                {
                    _logger.LogInformation($"[SERVER {serverId}] Avatar for {playerName} is outdated (last updated: {player.AvatarLastUpdated?.ToString() ?? "never"}), fetching new avatar URL...");

                    try
                    {
                        var avatarUrl = await steamApiService.GetPlayerAvatarAsync(steamId);
                        if (avatarUrl != null)
                        {
                            player.Avatar = avatarUrl;
                            player.AvatarLastUpdated = DateTime.UtcNow;
                            updated = true;
                            _logger.LogInformation($"[SERVER {serverId}] Successfully updated avatar URL for {playerName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch avatar URL for {steamId}");
                    }
                }
                else
                {
                    _logger.LogDebug($"[SERVER {serverId}] Avatar for {playerName} is still fresh (last updated: {player.AvatarLastUpdated}), skipping refresh");
                }

                if (updated)
                {
                    db.SteamPlayers.Update(player);
                    await db.SaveChangesAsync();
                    _logger.LogInformation($"[SERVER {serverId}] Updated player {playerName} with country and VAC ban info");
                }
            }
            else
            {
                // Player doesn't exist yet - create new player with country and VAC ban info
                _logger.LogInformation($"[SERVER {serverId}] Player {playerName} ({steamId}) not found in database - creating new player");

                var now = DateTime.UtcNow;

                // Fetch avatar URL for new player
                string? avatarUrl = null;
                try
                {
                    avatarUrl = await steamApiService.GetPlayerAvatarAsync(steamId);
                    if (avatarUrl != null)
                    {
                        _logger.LogInformation($"[SERVER {serverId}] Successfully fetched avatar URL for new player {playerName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch avatar URL for new player {steamId}");
                }

                var newPlayer = new SteamPlayer
                {
                    SteamId = steamId,
                    Name = playerName,
                    ServerId = serverId,
                    Country = country,
                    LastIp = cleanIp,
                    IsOnline = true,
                    FirstSeen = now,
                    LastSeen = now,
                    CreatedAt = now,
                    VACBanned = vacBanned,
                    NumberOfVACBans = numberOfVACBans,
                    DaysSinceLastVACBan = daysSinceLastBan,
                    Avatar = avatarUrl,
                    AvatarLastUpdated = avatarUrl != null ? now : null,
                    SteamAccountCreated = steamAccountCreated,
                    RustPlaytimeMinutes = rustPlaytimeMinutes,
                    ProfileVisibility = profileVisibility
                };

                db.SteamPlayers.Add(newPlayer);
                await db.SaveChangesAsync();
                _logger.LogInformation($"[SERVER {serverId}] Created new player {playerName} with country '{country}', VAC ban info, and avatar");
            }

            // CHECK FOR GLOBAL BANS
            _logger.LogInformation($"[SERVER {serverId}] Checking if player {playerName} ({steamId}) has a global ban");
            var globalBan = await db.PlayerBans
                .FirstOrDefaultAsync(b => b.SteamId == steamId && b.IsGlobalBan && !b.IsLifted);

            if (globalBan != null)
            {
                // Check if this global ban has expired
                bool isExpired = globalBan.Expiry != null && globalBan.Expiry != -1
                    && UnixTimeStampToDateTime(globalBan.Expiry.Value) <= DateTime.UtcNow;

                if (isExpired)
                {
                    _logger.LogInformation($"[SERVER {serverId}] Global ban for {playerName} ({steamId}) has expired, removing from database");

                    // Remove the expired global ban
                    db.PlayerBans.Remove(globalBan);

                    // Remove all server-specific copies of this global ban
                    var serverBanCopies = await db.PlayerBans
                        .Where(b => b.SteamId == steamId && !b.IsGlobalBan && b.Notes != null && b.Notes.Contains("[GLOBAL BAN ENFORCEMENT]"))
                        .ToListAsync();

                    if (serverBanCopies.Any())
                    {
                        db.PlayerBans.RemoveRange(serverBanCopies);
                        _logger.LogInformation($"[SERVER {serverId}] Removed {serverBanCopies.Count} expired server-specific global ban copies for {playerName}");
                    }

                    // Mark ban history as inactive
                    var historyRecords = await db.PlayerBanHistories
                        .Where(h => h.SteamId == steamId && h.IsGlobalBan && h.IsActive)
                        .ToListAsync();

                    foreach (var history in historyRecords)
                    {
                        history.IsActive = false;
                        history.LiftedAt = DateTime.UtcNow;
                        history.LiftedBy = "System (expired)";
                        history.LiftReason = "Global ban expired";
                        history.UpdatedAt = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync();
                    // Don't return — let the rest of the player connect logic continue
                }
                else
                {
                    // Ban is still active — enforce it
                    _logger.LogWarning($"[SERVER {serverId}] Player {playerName} ({steamId}) is globally banned! Banning from this server...");

                    // Check if player is already banned on this server
                    var existingServerBan = await db.PlayerBans
                        .FirstOrDefaultAsync(b => b.SteamId == steamId && b.ServerId == serverId);

                    if (existingServerBan == null)
                    {
                        // Create a server-specific ban matching the global ban duration
                        var newServerBan = new PlayerBan
                        {
                            ServerId = serverId,
                            SteamId = steamId,
                            Group = globalBan.Group,
                            Username = playerName,
                            Notes = $"[GLOBAL BAN ENFORCEMENT] {globalBan.Notes}",
                            Expiry = globalBan.Expiry,
                            IsGlobalBan = false, // This is the server-specific copy
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        db.PlayerBans.Add(newServerBan);
                        await db.SaveChangesAsync();
                        _logger.LogWarning($"[SERVER {serverId}] Added server ban for globally banned player {playerName} ({steamId})");
                    }

                    // Send ban command to server
                    try
                    {
                        string banCommand;
                        if (globalBan.Expiry == null || globalBan.Expiry == -1)
                        {
                            banCommand = $"banid {steamId} \"{playerName}\" \"[GLOBAL BAN] {globalBan.Notes}\" 0";
                        }
                        else
                        {
                            long remainingHours = (long)Math.Ceiling((UnixTimeStampToDateTime(globalBan.Expiry.Value) - DateTime.UtcNow).TotalHours);
                            banCommand = $"banid {steamId} \"{playerName}\" \"[GLOBAL BAN] {globalBan.Notes}\" {remainingHours}";
                        }

                        SendRconCommand(banCommand, serverId);
                        _logger.LogWarning($"[SERVER {serverId}] Sent banid command for globally banned player {playerName} ({steamId})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[SERVER {serverId}] Could not send banid command for globally banned player {playerName}");
                    }
                }
            }
            else
            {
                _logger.LogInformation($"[SERVER {serverId}] Player {playerName} ({steamId}) is not globally banned");
            }

            // Execute triggers for player join
            _ = Task.Run(async () => await _triggerExecutionService.OnPlayerJoinAsync(serverId, playerName, steamId));

            // Skip webhooks and push notifications during initial sync after (re)connect
            if (_suppressPlayerEvents.GetValueOrDefault(serverId, false))
            {
                _logger.LogInformation("[SERVER {ServerId}] Suppressing player connect notification for {PlayerName} (initial sync)", serverId, playerName);
            }
            else
            {
                // Send Discord webhook for player connect
                var connectCountry = country;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var webhookScope = _scopeFactory.CreateScope();
                        var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                        await webhookService.SendPlayerConnectAsync(serverId, playerName, steamId, cleanIp, connectCountry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[SERVER {serverId}] Error sending player connect webhook");
                    }
                });

            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SERVER {serverId}] Error handling player connected event for {playerName}");
        }
    }

    /// <summary>
    /// Handles player disconnected event from the game server (immediate, real-time)
    /// </summary>
    public async Task OnPlayerDisconnectedAsync(int serverId, string steamId, string playerName, string reason)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var player = await db.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);

            if (player != null && player.IsOnline)
            {
                player.IsOnline = false;
                player.LastSeen = DateTime.UtcNow;
                db.SteamPlayers.Update(player);
                await db.SaveChangesAsync();

                _logger.LogInformation("[SERVER {ServerId}] PLAYER DISCONNECTED: {PlayerName} ({SteamId}) - Reason: {Reason}",
                    serverId, playerName, steamId, reason);

                // Trigger player leave event
                _ = Task.Run(async () => await _triggerExecutionService.OnPlayerLeaveAsync(serverId, playerName, steamId));

                // Skip webhooks and push notifications during initial sync
                if (_suppressPlayerEvents.GetValueOrDefault(serverId, false))
                {
                    _logger.LogInformation("[SERVER {ServerId}] Suppressing player disconnect notification for {PlayerName} (initial sync)", serverId, playerName);
                    return;
                }

                // Send Discord webhook for player disconnect
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var webhookScope = _scopeFactory.CreateScope();
                        var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                        await webhookService.SendPlayerDisconnectAsync(serverId, playerName, steamId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[SERVER {ServerId}] Error sending player disconnect webhook", serverId);
                    }
                });

            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error handling player disconnected event for {PlayerName}", serverId, playerName);
        }
    }

    /// <summary>
    /// Handles player reported event - saves the report to the database
    /// </summary>
    public async Task OnPlayerReportedAsync(int serverId, string reporterName, string reporterId,
        string reportedName, string reportedId, string subject, string message, string type)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _logger.LogInformation($"[SERVER {serverId}] Player report: {reporterName} ({reporterId}) reported {reportedName} ({reportedId}) for {type}");

            // Create new player report
            var playerReport = new PlayerReport
            {
                ServerId = serverId,
                ReporterId = reporterId,
                ReporterName = reporterName,
                ReportedId = reportedId,
                ReportedName = reportedName,
                Subject = subject,
                Message = message,
                Type = type,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.PlayerReports.Add(playerReport);
            await db.SaveChangesAsync();

            _logger.LogInformation($"[SERVER {serverId}] Successfully saved player report (ID: {playerReport.Id})");

            // Send Discord webhook for player report
            _ = Task.Run(async () =>
            {
                try
                {
                    using var webhookScope = _scopeFactory.CreateScope();
                    var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                    await webhookService.SendPlayerReportAsync(serverId, reporterName, reporterId, reportedName, reportedId, type, subject, message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[SERVER {serverId}] Error sending player report webhook");
                }
            });

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SERVER {serverId}] Error saving player report from {reporterName} about {reportedName}");
        }
    }

    /// <summary>
    /// Marks players as offline if they haven't been seen in 90+ seconds (3 missed cycles).
    /// This is a safety net - real-time disconnect detection is handled by OnPlayerDisconnected.
    /// </summary>
    private async Task MarkOfflinePlayersAsync(int serverId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ninetySecondsAgo = DateTime.UtcNow.AddSeconds(-90);

            // Find all online players who haven't been seen in 90+ seconds (3 missed cycles)
            var playersToMarkOffline = await db.SteamPlayers
                .Where(p => p.ServerId == serverId && p.IsOnline && p.LastSeen < ninetySecondsAgo)
                .ToListAsync();

            if (playersToMarkOffline.Any())
            {
                // Check if we should suppress notifications (initial sync after reconnect)
                bool suppressNotifications = _suppressPlayerEvents.GetValueOrDefault(serverId, false);

                foreach (var player in playersToMarkOffline)
                {
                    player.IsOnline = false;
                    _logger.LogInformation("[SERVER {ServerId}] PLAYER OFFLINE: {PlayerName} ({SteamId}) - Last seen: {LastSeen}",
                        serverId, player.Name, player.SteamId, player.LastSeen);
                    db.SteamPlayers.Update(player);

                    // Trigger player leave event
                    _ = Task.Run(async () => await _triggerExecutionService.OnPlayerLeaveAsync(serverId, player.Name, player.SteamId));

                    if (suppressNotifications)
                    {
                        _logger.LogInformation("[SERVER {ServerId}] Suppressing player offline notification for {PlayerName} (initial sync)", serverId, player.Name);
                        continue;
                    }

                    // Send Discord webhook for player disconnect
                    var playerName = player.Name;
                    var steamId = player.SteamId;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var webhookScope = _scopeFactory.CreateScope();
                            var webhookService = webhookScope.ServiceProvider.GetRequiredService<IDiscordWebhookService>();
                            await webhookService.SendPlayerDisconnectAsync(serverId, playerName, steamId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player disconnect webhook", serverId);
                        }
                    });

                }

                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error marking offline players", serverId);
        }
    }

    /// <summary>
    /// Fetches and saves VAC ban + country data for a player that was blocked by
    /// protection rules, so the admin still sees up-to-date info in the panel.
    /// Only updates existing DB records — if the player has never connected before
    /// they won't have a record yet and are skipped.
    /// </summary>
    private async Task UpdateBlockedPlayerDbAsync(int serverId, string steamId, string playerName, string cleanIp)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var steamApiService = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
            var ipGeolocationService = scope.ServiceProvider.GetRequiredService<IIpGeolocationService>();

            var player = await db.SteamPlayers
                .FirstOrDefaultAsync(p => p.SteamId == steamId && p.ServerId == serverId);

            // Fetch data we need regardless of new/existing player
            string? country = null;
            try { country = await ipGeolocationService.GetCountryFromIpAsync(cleanIp); }
            catch (Exception ex) { _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch country for blocked player {playerName}"); }

            bool vacBanned = false;
            int? numberOfVACBans = null;
            int? daysSinceLastBan = null;
            try
            {
                var vacBanInfo = await steamApiService.GetPlayerVACBanInfoAsync(steamId);
                if (vacBanInfo.HasValue)
                {
                    vacBanned = vacBanInfo.Value.VACBanned;
                    numberOfVACBans = vacBanInfo.Value.NumberOfVACBans;
                    daysSinceLastBan = vacBanInfo.Value.DaysSinceLastBan;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch VAC info for blocked player {playerName}"); }

            if (player == null)
            {
                // First time this player attempted to join — create a record so the
                // admin can see who was blocked and why.
                _logger.LogInformation($"[SERVER {serverId}] Creating DB record for first-time blocked player {playerName} ({steamId})");

                var now = DateTime.UtcNow;

                string? avatarUrl = null;
                try { avatarUrl = await steamApiService.GetPlayerAvatarAsync(steamId); }
                catch (Exception ex) { _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch avatar for blocked player {playerName}"); }

                DateTime? steamAccountCreated = null;
                int? rustPlaytimeMinutes = null;
                int? profileVisibility = null;
                try
                {
                    var steamInfo = await steamApiService.GetPlayerSteamInfoAsync(steamId);
                    if (steamInfo.HasValue)
                    {
                        steamAccountCreated = steamInfo.Value.AccountCreated;
                        rustPlaytimeMinutes = steamInfo.Value.RustPlaytimeMinutes;
                        profileVisibility = steamInfo.Value.ProfileVisibility;
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, $"[SERVER {serverId}] Failed to fetch Steam info for blocked player {playerName}"); }

                var newPlayer = new SteamPlayer
                {
                    SteamId = steamId,
                    Name = playerName,
                    ServerId = serverId,
                    Country = country,
                    LastIp = cleanIp,
                    IsOnline = false,
                    FirstSeen = now,
                    LastSeen = now,
                    CreatedAt = now,
                    VACBanned = vacBanned,
                    NumberOfVACBans = numberOfVACBans,
                    DaysSinceLastVACBan = daysSinceLastBan,
                    Avatar = avatarUrl,
                    AvatarLastUpdated = avatarUrl != null ? now : null,
                    SteamAccountCreated = steamAccountCreated,
                    RustPlaytimeMinutes = rustPlaytimeMinutes,
                    ProfileVisibility = profileVisibility
                };

                db.SteamPlayers.Add(newPlayer);
                await db.SaveChangesAsync();
                _logger.LogInformation($"[SERVER {serverId}] Created DB record for blocked player {playerName} ({steamId})");
            }
            else
            {
                // Existing player — update VAC info and country if missing
                if (!string.IsNullOrEmpty(country))
                    player.Country = country;

                player.VACBanned = vacBanned;
                player.NumberOfVACBans = numberOfVACBans;
                player.DaysSinceLastVACBan = daysSinceLastBan;

                db.SteamPlayers.Update(player);
                await db.SaveChangesAsync();
                _logger.LogInformation($"[SERVER {serverId}] Updated DB data for blocked player {playerName} ({steamId})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SERVER {serverId}] Error updating DB for blocked player {playerName} ({steamId})");
        }
    }
}
