using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Shared.ServerWebhooks;
using System.Text;
using System.Text.Json;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service for sending Discord webhook notifications for server events
/// </summary>
public class DiscordWebhookService : IDiscordWebhookService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscordWebhookService> _logger;

    public DiscordWebhookService(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        ILogger<DiscordWebhookService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task SendPlayerConnectAsync(int serverId, string playerName, string steamId, string ipAddress, string? country)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnablePlayerConnectWebhook || string.IsNullOrWhiteSpace(settings.PlayerConnectWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "steamId", steamId },
                { "ipAddress", ipAddress },
                { "country", string.IsNullOrEmpty(country) ? "Unknown" : country },
                { "serverName", serverName }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.PlayerConnectTextContent)
                ? $"**{playerName}** joined {serverName}"
                : settings.PlayerConnectTextContent;

            // Default embed format (NO SENSITIVE DATA - no SteamID, IP, or Country)
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Player Connected",
                        description = $"**{playerName}** joined the server",
                        color = 65280, // Green (#00FF00)
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.PlayerConnectFormat,
                settings.PlayerConnectCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.PlayerConnectWebhookUrl, payload, "Player Connect");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player connect webhook for {PlayerName}", serverId, LogSanitizer.Sanitize(playerName));
        }
    }

    public async Task SendPlayerDisconnectAsync(int serverId, string playerName, string steamId)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnablePlayerDisconnectWebhook || string.IsNullOrWhiteSpace(settings.PlayerDisconnectWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "steamId", steamId },
                { "serverName", serverName }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.PlayerDisconnectTextContent)
                ? $"**{playerName}** left {serverName}"
                : ReplaceVariables(settings.PlayerDisconnectTextContent, variables);

            // Default embed format (NO STEAM ID)
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Player Disconnected",
                        description = $"**{playerName}** left the server",
                        color = 16711680, // Red (#FF0000)
                        fields = new[]
                        {
                            new { name = "Player", value = playerName, inline = true }
                        },
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.PlayerDisconnectFormat,
                settings.PlayerDisconnectCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.PlayerDisconnectWebhookUrl, payload, "Player Disconnect");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player disconnect webhook for {PlayerName}", serverId, LogSanitizer.Sanitize(playerName));
        }
    }

    public async Task SendPlayerKillAsync(int serverId, string killerName, string killerId, string victimName, string victimId, bool isPVP)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnablePlayerKillWebhook || string.IsNullOrWhiteSpace(settings.PlayerKillWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);
            var color = isPVP ? 16737280 : 6710886; // Orange (#FF6600) for PVP, Gray (#666666) for PVE
            var killType = isPVP ? "PVP" : "PVE";

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "killerName", killerName },
                { "killerId", killerId },
                { "victimName", victimName },
                { "victimId", victimId },
                { "killType", killType },
                { "serverName", serverName }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.PlayerKillTextContent)
                ? $"**{killerName}** killed **{victimName}** ({killType}) on {serverName}"
                : ReplaceVariables(settings.PlayerKillTextContent, variables);

            // Default embed format (NO STEAM IDS)
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"Player Kill ({killType})",
                        description = $"**{killerName}** killed **{victimName}**",
                        color = color,
                        fields = new[]
                        {
                            new { name = "Killer", value = killerName, inline = true },
                            new { name = "Victim", value = victimName, inline = true },
                            new { name = "Type", value = killType, inline = true }
                        },
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.PlayerKillFormat,
                settings.PlayerKillCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.PlayerKillWebhookUrl, payload, "Player Kill");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player kill webhook", serverId);
        }
    }

    public async Task SendPlayerBanAsync(int serverId, string playerName, string steamId, string reason, int durationMinutes)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnablePlayerBanWebhook || string.IsNullOrWhiteSpace(settings.PlayerBanWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);
            var duration = durationMinutes == 0 ? "Permanent" : $"{durationMinutes} minutes";

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "steamId", steamId },
                { "reason", reason },
                { "duration", duration },
                { "durationMinutes", durationMinutes.ToString() },
                { "serverName", serverName }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.PlayerBanTextContent)
                ? $"**{playerName}** has been banned from {serverName} ({duration}) - Reason: {reason}"
                : ReplaceVariables(settings.PlayerBanTextContent, variables);

            // Default embed format (NO STEAM ID)
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Player Banned",
                        description = $"**{playerName}** has been banned",
                        color = 9109504, // Dark Red (#8B0000)
                        fields = new[]
                        {
                            new { name = "Player", value = playerName, inline = true },
                            new { name = "Duration", value = duration, inline = true },
                            new { name = "Reason", value = reason, inline = false }
                        },
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.PlayerBanFormat,
                settings.PlayerBanCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.PlayerBanWebhookUrl, payload, "Player Ban");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player ban webhook for {PlayerName}", serverId, LogSanitizer.Sanitize(playerName));
        }
    }

    public async Task SendPlayerKickAsync(int serverId, string playerName, string steamId, string reason)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnablePlayerKickWebhook || string.IsNullOrWhiteSpace(settings.PlayerKickWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "steamId", steamId },
                { "reason", reason },
                { "serverName", serverName }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.PlayerKickTextContent)
                ? $"**{playerName}** has been kicked from {serverName} - Reason: {reason}"
                : ReplaceVariables(settings.PlayerKickTextContent, variables);

            // Default embed format (NO STEAM ID)
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Player Kicked",
                        description = $"**{playerName}** has been kicked",
                        color = 16753920, // Orange (#FFA500)
                        fields = new[]
                        {
                            new { name = "Player", value = playerName, inline = true },
                            new { name = "Reason", value = reason, inline = false }
                        },
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.PlayerKickFormat,
                settings.PlayerKickCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.PlayerKickWebhookUrl, payload, "Player Kick");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player kick webhook for {PlayerName}", serverId, LogSanitizer.Sanitize(playerName));
        }
    }

    public async Task SendPlayerReportAsync(int serverId, string reporterName, string reporterId, string reportedName, string reportedId, string type, string subject, string message)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnablePlayerReportWebhook || string.IsNullOrWhiteSpace(settings.PlayerReportWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "reporterName", reporterName },
                { "reporterId", reporterId },
                { "reportedName", reportedName },
                { "reportedId", reportedId },
                { "type", type },
                { "subject", subject },
                { "message", message },
                { "serverName", serverName }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.PlayerReportTextContent)
                ? $"**{reporterName}** reported **{reportedName}** for **{type}** on {serverName}"
                : ReplaceVariables(settings.PlayerReportTextContent, variables);

            // Default embed format (NO STEAM IDS)
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Player Report",
                        description = $"**{reporterName}** reported **{reportedName}**",
                        color = 16776960, // Yellow (#FFFF00)
                        fields = new[]
                        {
                            new { name = "Reporter", value = reporterName, inline = true },
                            new { name = "Reported Player", value = reportedName, inline = true },
                            new { name = "Type", value = type, inline = false },
                            new { name = "Subject", value = subject, inline = false },
                            new { name = "Message", value = string.IsNullOrWhiteSpace(message) ? "_No message provided_" : message, inline = false }
                        },
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.PlayerReportFormat,
                settings.PlayerReportCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.PlayerReportWebhookUrl, payload, "Player Report");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending player report webhook", serverId);
        }
    }

    public async Task SendServerOfflineAsync(int serverId, string serverName)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnableServerOfflineWebhook || string.IsNullOrWhiteSpace(settings.ServerOfflineWebhookUrl))
            {
                return; // Early return if not configured
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "serverName", serverName },
                { "timestamp", timestamp }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.ServerOfflineTextContent)
                ? $"🔴 **{serverName}** is offline!"
                : ReplaceVariables(settings.ServerOfflineTextContent, variables);

            // Default embed format
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "🔴 Server Offline",
                        description = $"**{serverName}** is no longer reachable",
                        color = 15158332, // Red
                        fields = new[]
                        {
                            new { name = "Server", value = serverName, inline = true },
                            new { name = "Status", value = "❌ Offline", inline = true },
                            new { name = "Time", value = $"<t:{timestamp}:F>", inline = false }
                        },
                        footer = new { text = "Server Status Monitor" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.ServerOfflineFormat,
                settings.ServerOfflineCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.ServerOfflineWebhookUrl, payload, "Server Offline");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending server offline webhook for {ServerName}", serverId, LogSanitizer.Sanitize(serverName));
        }
    }

    public async Task SendServerOnlineAsync(int serverId, string serverName, int downtimeMinutes)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnableServerOnlineWebhook || string.IsNullOrWhiteSpace(settings.ServerOnlineWebhookUrl))
            {
                return; // Early return if not configured
            }

            string downtimeText;
            if (downtimeMinutes < 1)
            {
                downtimeText = "Less than 1 minute";
            }
            else if (downtimeMinutes < 60)
            {
                downtimeText = $"{downtimeMinutes} minute{(downtimeMinutes != 1 ? "s" : "")}";
            }
            else
            {
                int hours = downtimeMinutes / 60;
                int minutes = downtimeMinutes % 60;
                downtimeText = $"{hours} hour{(hours != 1 ? "s" : "")}";
                if (minutes > 0)
                {
                    downtimeText += $" {minutes} minute{(minutes != 1 ? "s" : "")}";
                }
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // Variables for custom content
            var variables = new Dictionary<string, string>
            {
                { "serverName", serverName },
                { "downtime", downtimeText },
                { "downtimeMinutes", downtimeMinutes.ToString() },
                { "timestamp", timestamp }
            };

            // Text message format - use custom text content if provided, otherwise default
            var textMessage = string.IsNullOrWhiteSpace(settings.ServerOnlineTextContent)
                ? $"✅ **{serverName}** is back online! (Downtime: {downtimeText})"
                : ReplaceVariables(settings.ServerOnlineTextContent, variables);

            // Default embed format
            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "✅ Server Online",
                        description = $"**{serverName}** is back online!",
                        color = 3066993, // Green
                        fields = new[]
                        {
                            new { name = "Server", value = serverName, inline = true },
                            new { name = "Status", value = "✅ Online", inline = true },
                            new { name = "Downtime", value = downtimeText, inline = false },
                            new { name = "Back Online At", value = $"<t:{timestamp}:F>", inline = false }
                        },
                        footer = new { text = "Server Status Monitor" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(
                settings.ServerOnlineFormat,
                settings.ServerOnlineCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.ServerOnlineWebhookUrl, payload, "Server Online");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending server online webhook for {ServerName}", serverId, LogSanitizer.Sanitize(serverName));
        }
    }

    public async Task SendServerProtectionAsync(int serverId, string playerName, string steamId, string reason, string actionTaken)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            if (settings == null || !settings.EnableServerProtectionWebhook || string.IsNullOrWhiteSpace(settings.ServerProtectionWebhookUrl))
            {
                return;
            }

            var serverName = await GetServerNameAsync(serverId);

            var variables = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "steamId", steamId },
                { "reason", reason },
                { "action", actionTaken },
                { "serverName", serverName }
            };

            var textMessage = string.IsNullOrWhiteSpace(settings.ServerProtectionTextContent)
                ? $"**{playerName}** was {actionTaken.ToLower()} by server protection on {serverName}: {reason}"
                : ReplaceVariables(settings.ServerProtectionTextContent, variables);

            var defaultEmbed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Server Protection",
                        description = $"**{playerName}** was blocked by server protection",
                        color = 15105570, // Orange (#E67E22)
                        fields = new[]
                        {
                            new { name = "Player", value = playerName, inline = true },
                            new { name = "Action", value = actionTaken, inline = true },
                            new { name = "Reason", value = reason, inline = false }
                        },
                        footer = new { text = serverName },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var payload = BuildWebhookPayload(
                settings.ServerProtectionFormat,
                settings.ServerProtectionCustomContent,
                defaultEmbed,
                textMessage,
                variables
            );

            await SendWebhookAsync(settings.ServerProtectionWebhookUrl, payload, "Server Protection");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SERVER {ServerId}] Error sending server protection webhook for {PlayerName}", serverId, LogSanitizer.Sanitize(playerName));
        }
    }

    public async Task SendTestWebhookAsync(int serverId, string webhookUrl, WebhookEventType eventType)
    {
        try
        {
            var settings = await GetWebhookSettingsAsync(serverId);
            var serverName = await GetServerNameAsync(serverId);

            // Get format and custom content based on event type
            var (format, customContent, testVariables, defaultEmbed, textMessage) = eventType switch
            {
                WebhookEventType.PlayerConnect => (
                    settings?.PlayerConnectFormat ?? WebhookFormat.Embed,
                    settings?.PlayerConnectCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "playerName", "TestPlayer" },
                        { "steamId", "76561198000000000" },
                        { "ipAddress", "127.0.0.1" },
                        { "country", "Test Country" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Player Connected (TEST)",
                                description = "**TestPlayer** joined the server",
                                color = 65280,
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestPlayer** joined Test Server"
                ),

                WebhookEventType.PlayerDisconnect => (
                    settings?.PlayerDisconnectFormat ?? WebhookFormat.Embed,
                    settings?.PlayerDisconnectCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "playerName", "TestPlayer" },
                        { "steamId", "76561198000000000" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Player Disconnected (TEST)",
                                description = "**TestPlayer** left the server",
                                color = 16711680,
                                fields = new[]
                                {
                                    new { name = "Player", value = "TestPlayer", inline = true }
                                },
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestPlayer** left Test Server"
                ),

                WebhookEventType.PlayerKill => (
                    settings?.PlayerKillFormat ?? WebhookFormat.Embed,
                    settings?.PlayerKillCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "killerName", "TestKiller" },
                        { "killerId", "76561198000000001" },
                        { "victimName", "TestVictim" },
                        { "victimId", "76561198000000002" },
                        { "killType", "PVP" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Player Kill (PVP) (TEST)",
                                description = "**TestKiller** killed **TestVictim**",
                                color = 16737280,
                                fields = new[]
                                {
                                    new { name = "Killer", value = "TestKiller", inline = true },
                                    new { name = "Victim", value = "TestVictim", inline = true },
                                    new { name = "Type", value = "PVP", inline = true }
                                },
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestKiller** killed **TestVictim** (PVP) on Test Server"
                ),

                WebhookEventType.PlayerBan => (
                    settings?.PlayerBanFormat ?? WebhookFormat.Embed,
                    settings?.PlayerBanCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "playerName", "TestPlayer" },
                        { "steamId", "76561198000000000" },
                        { "reason", "Test ban reason" },
                        { "duration", "1440 minutes" },
                        { "durationMinutes", "1440" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Player Banned (TEST)",
                                description = "**TestPlayer** has been banned",
                                color = 9109504,
                                fields = new[]
                                {
                                    new { name = "Player", value = "TestPlayer", inline = true },
                                    new { name = "Duration", value = "1440 minutes", inline = true },
                                    new { name = "Reason", value = "Test ban reason", inline = false }
                                },
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestPlayer** has been banned from Test Server (1440 minutes) - Reason: Test ban reason"
                ),

                WebhookEventType.PlayerKick => (
                    settings?.PlayerKickFormat ?? WebhookFormat.Embed,
                    settings?.PlayerKickCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "playerName", "TestPlayer" },
                        { "steamId", "76561198000000000" },
                        { "reason", "Test kick reason" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Player Kicked (TEST)",
                                description = "**TestPlayer** has been kicked",
                                color = 16753920,
                                fields = new[]
                                {
                                    new { name = "Player", value = "TestPlayer", inline = true },
                                    new { name = "Reason", value = "Test kick reason", inline = false }
                                },
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestPlayer** has been kicked from Test Server - Reason: Test kick reason"
                ),

                WebhookEventType.PlayerReport => (
                    settings?.PlayerReportFormat ?? WebhookFormat.Embed,
                    settings?.PlayerReportCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "reporterName", "TestReporter" },
                        { "reporterId", "76561198000000001" },
                        { "reportedName", "TestOffender" },
                        { "reportedId", "76561198000000002" },
                        { "type", "break_server_rules" },
                        { "subject", "[break_server_rules] Test report subject" },
                        { "message", "This is a test report message" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Player Report (TEST)",
                                description = "**TestReporter** reported **TestOffender**",
                                color = 16776960,
                                fields = new[]
                                {
                                    new { name = "Reporter", value = "TestReporter", inline = true },
                                    new { name = "Reported Player", value = "TestOffender", inline = true },
                                    new { name = "Type", value = "break_server_rules", inline = false },
                                    new { name = "Subject", value = "[break_server_rules] Test report subject", inline = false },
                                    new { name = "Message", value = "This is a test report message", inline = false }
                                },
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestReporter** reported **TestOffender** for **break_server_rules** on Test Server"
                ),

                WebhookEventType.ServerOffline => (
                    settings?.ServerOfflineFormat ?? WebhookFormat.Embed,
                    settings?.ServerOfflineCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "serverName", serverName },
                        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "🔴 Server Offline (TEST)",
                                description = $"**{serverName}** is no longer reachable",
                                color = 15158332,
                                fields = new[]
                                {
                                    new { name = "Server", value = serverName, inline = true },
                                    new { name = "Status", value = "❌ Offline", inline = true },
                                    new { name = "Time", value = $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>", inline = false }
                                },
                                footer = new { text = "Server Status Monitor" },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    $"🔴 **{serverName}** is offline!"
                ),

                WebhookEventType.ServerOnline => (
                    settings?.ServerOnlineFormat ?? WebhookFormat.Embed,
                    settings?.ServerOnlineCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "serverName", serverName },
                        { "downtime", "15 minutes" },
                        { "downtimeMinutes", "15" },
                        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "✅ Server Online (TEST)",
                                description = $"**{serverName}** is back online!",
                                color = 3066993,
                                fields = new[]
                                {
                                    new { name = "Server", value = serverName, inline = true },
                                    new { name = "Status", value = "✅ Online", inline = true },
                                    new { name = "Downtime", value = "15 minutes", inline = false },
                                    new { name = "Back Online At", value = $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>", inline = false }
                                },
                                footer = new { text = "Server Status Monitor" },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    $"✅ **{serverName}** is back online! (Downtime: 15 minutes)"
                ),

                WebhookEventType.ServerProtection => (
                    settings?.ServerProtectionFormat ?? WebhookFormat.Embed,
                    settings?.ServerProtectionCustomContent ?? string.Empty,
                    new Dictionary<string, string>
                    {
                        { "playerName", "TestPlayer" },
                        { "steamId", "76561198000000000" },
                        { "reason", "VPN/Proxy connections are not allowed on this server." },
                        { "action", "Kicked" },
                        { "serverName", serverName }
                    },
                    (object)new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "Server Protection (TEST)",
                                description = "**TestPlayer** was blocked by server protection",
                                color = 15105570,
                                fields = new[]
                                {
                                    new { name = "Player", value = "TestPlayer", inline = true },
                                    new { name = "Action", value = "Kicked", inline = true },
                                    new { name = "Reason", value = "VPN/Proxy connections are not allowed on this server.", inline = false }
                                },
                                footer = new { text = serverName },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    },
                    "**TestPlayer** was kicked by server protection on Test Server: VPN/Proxy connections are not allowed on this server."
                ),

                _ => throw new ArgumentException("Invalid event type")
            };

            // Build payload based on format
            var payload = BuildWebhookPayload(format, customContent, defaultEmbed, textMessage, testVariables);

            await SendWebhookAsync(webhookUrl, payload, $"Test {eventType}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test webhook for {EventType}", eventType);
            throw;
        }
    }

    private async Task<Models.ServerWebhookSettings?> GetWebhookSettingsAsync(int serverId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await dbContext.ServerWebhookSettings
                .FirstOrDefaultAsync(s => s.ServerId == serverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading webhook settings for server {ServerId}", serverId);
            return null;
        }
    }

    private async Task<string> GetServerNameAsync(int serverId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var server = await dbContext.RconServers.FindAsync(serverId);
            return server?.Name ?? $"Server {serverId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading server name for server {ServerId}", serverId);
            return $"Server {serverId}";
        }
    }

    private async Task SendWebhookAsync(string webhookUrl, object payload, string eventName)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(webhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent {EventName} webhook", eventName);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send {EventName} webhook. Status: {StatusCode}, Response: {ResponseBody}", eventName, response.StatusCode, LogSanitizer.Sanitize(responseBody));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {EventName} webhook to {WebhookUrl}", eventName, LogSanitizer.Sanitize(webhookUrl));
        }
    }

    /// <summary>
    /// Replaces variables in custom content with actual values
    /// </summary>
    private string ReplaceVariables(string content, Dictionary<string, string> variables)
    {
        foreach (var kvp in variables)
        {
            content = content.Replace($"{{{kvp.Key}}}", kvp.Value);
        }
        return content;
    }

    /// <summary>
    /// Builds webhook payload based on format (text, embed, or custom)
    /// </summary>
    private object BuildWebhookPayload(WebhookFormat format, string customContent, object defaultEmbed, string textMessage, Dictionary<string, string> variables)
    {
        return format switch
        {
            WebhookFormat.Text => new { content = ReplaceVariables(textMessage, variables) },
            WebhookFormat.Embed => defaultEmbed,
            WebhookFormat.Custom => ParseCustomContent(customContent, variables),
            _ => defaultEmbed
        };
    }

    /// <summary>
    /// Parses custom JSON content and replaces variables
    /// </summary>
    private object ParseCustomContent(string customContent, Dictionary<string, string> variables)
    {
        try
        {
            // Check if custom content is empty
            if (string.IsNullOrWhiteSpace(customContent))
            {
                _logger.LogWarning("Custom content is empty, using default message");
                return new { content = "Custom content is not configured" };
            }

            // Replace variables first
            var processedContent = ReplaceVariables(customContent, variables);

            // Parse as JSON and return as JsonElement for proper serialization
            using var jsonDoc = JsonDocument.Parse(processedContent);

            // Clone the element so it can be used after the document is disposed
            var element = jsonDoc.RootElement.Clone();

            // Return the JsonElement which will be properly serialized by SendWebhookAsync
            return element;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error in custom webhook content: {Content}", LogSanitizer.Sanitize(customContent));
            return new { content = $"Error: Invalid JSON format - {ex.Message}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing custom webhook content");
            return new { content = "Error: Invalid custom content format" };
        }
    }
}
