using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Interfaces;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service responsible for executing triggers based on server events
/// </summary>
public class TriggerExecutionService
{
    private readonly ILogger<TriggerExecutionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private IRconBackgroundService? _rconBackgroundService;

    public TriggerExecutionService(
        ILogger<TriggerExecutionService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// Set the RconBackgroundService reference (called after construction to avoid circular dependency)
    /// </summary>
    public void SetRconBackgroundService(IRconBackgroundService rconBackgroundService)
    {
        _rconBackgroundService = rconBackgroundService;
    }

    /// <summary>
    /// Execute triggers for player join event
    /// </summary>
    public async Task OnPlayerJoinAsync(int serverId, string playerName, string steamId)
    {
        await ExecuteTriggersAsync(serverId, "PlayerJoin", new Dictionary<string, string>
        {
            { "player", playerName },
            { "steamid", steamId }
        });
    }

    /// <summary>
    /// Execute triggers for player leave event
    /// </summary>
    public async Task OnPlayerLeaveAsync(int serverId, string playerName, string steamId)
    {
        await ExecuteTriggersAsync(serverId, "PlayerLeave", new Dictionary<string, string>
        {
            { "player", playerName },
            { "steamid", steamId }
        });
    }

    /// <summary>
    /// Execute triggers for player kill event
    /// </summary>
    public async Task OnPlayerKillAsync(int serverId, string killerName, string victimName)
    {
        await ExecuteTriggersAsync(serverId, "PlayerKill", new Dictionary<string, string>
        {
            { "killer", killerName },
            { "victim", victimName },
            { "player", killerName } // For backward compatibility
        });
    }

    /// <summary>
    /// Execute triggers for player death event (when victim dies)
    /// </summary>
    public async Task OnPlayerDeathAsync(int serverId, string victimName, string killerName)
    {
        await ExecuteTriggersAsync(serverId, "PlayerDeath", new Dictionary<string, string>
        {
            { "player", victimName },
            { "victim", victimName },
            { "killer", killerName }
        });
    }

    /// <summary>
    /// Execute triggers for chat message event
    /// </summary>
    public async Task OnChatMessageAsync(int serverId, string playerName, string steamId, string message, string channel)
    {
        await ExecuteTriggersAsync(serverId, "ChatMessage", new Dictionary<string, string>
        {
            { "player", playerName },
            { "steamid", steamId },
            { "message", message },
            { "channel", channel }
        }, chatMessage: message);
    }

    /// <summary>
    /// Main method to execute all matching triggers for an event
    /// </summary>
    private async Task ExecuteTriggersAsync(int serverId, string eventType, Dictionary<string, string> eventPlaceholders, string? chatMessage = null)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Get all active triggers for this server and event type
            var triggers = await db.Triggers
                .Where(t => t.RconServerId == serverId
                    && t.TriggerEvent == eventType
                    && t.IsActive)
                .ToListAsync();

            if (!triggers.Any())
            {
                _logger.LogDebug($"[Server {serverId}] No active triggers found for event {eventType}");
                return;
            }

            // Filter ChatMessage triggers based on condition
            if (eventType == "ChatMessage" && chatMessage != null)
            {
                triggers = triggers.Where(t => ChatMessageMatchesCondition(t, chatMessage)).ToList();
            }

            if (!triggers.Any())
            {
                _logger.LogDebug($"[Server {serverId}] No triggers matched the conditions for event {eventType}");
                return;
            }

            _logger.LogInformation($"[Server {serverId}] Found {triggers.Count} trigger(s) for event {eventType}");

            foreach (var trigger in triggers)
            {
                // Execute trigger in background to avoid blocking
                _ = Task.Run(async () => await ExecuteSingleTriggerAsync(serverId, trigger, eventPlaceholders));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Server {serverId}] Error executing triggers for event {eventType}");
        }
    }

    /// <summary>
    /// Check if chat message matches trigger condition
    /// </summary>
    private bool ChatMessageMatchesCondition(Trigger trigger, string message)
    {
        if (string.IsNullOrWhiteSpace(trigger.ChatConditionType) || string.IsNullOrWhiteSpace(trigger.ChatConditionValue))
            return true; // No condition, always matches

        return trigger.ChatConditionType.ToLower() switch
        {
            "contains" => message.Contains(trigger.ChatConditionValue, StringComparison.OrdinalIgnoreCase),
            "beginswith" => message.StartsWith(trigger.ChatConditionValue, StringComparison.OrdinalIgnoreCase),
            "endswith" => message.EndsWith(trigger.ChatConditionValue, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    /// <summary>
    /// Execute a single trigger with placeholder replacement
    /// </summary>
    private async Task ExecuteSingleTriggerAsync(int serverId, Trigger trigger, Dictionary<string, string> eventPlaceholders)
    {
        try
        {
            // Apply delay if configured
            if (trigger.DelaySeconds.HasValue && trigger.DelaySeconds.Value > 0)
            {
                _logger.LogInformation($"[Server {serverId}] Trigger '{trigger.Name}' delayed by {trigger.DelaySeconds.Value} seconds");
                await Task.Delay(TimeSpan.FromSeconds(trigger.DelaySeconds.Value));
            }

            // Replace all placeholders in action value
            string actionValue = await ReplaceVariablesAsync(serverId, trigger.ActionValue, eventPlaceholders);

            _logger.LogInformation($"[Server {serverId}] Executing trigger '{trigger.Name}' (Action: {trigger.ActionType}): {actionValue}");

            // Execute based on action type
            bool success = await ExecuteActionAsync(serverId, trigger.ActionType, actionValue, trigger.WebhookUrl);

            // Update trigger execution status
            await UpdateTriggerExecutionStatusAsync(trigger.Id, success, success ? null : "Action execution failed");

            if (success)
            {
                _logger.LogInformation($"[Server {serverId}] Trigger '{trigger.Name}' executed successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Server {serverId}] Error executing trigger '{trigger.Name}'");
            await UpdateTriggerExecutionStatusAsync(trigger.Id, false, ex.Message);
        }
    }

    /// <summary>
    /// Replace all variables in the action value
    /// </summary>
    private async Task<string> ReplaceVariablesAsync(int serverId, string actionValue, Dictionary<string, string> eventPlaceholders)
    {
        string result = actionValue;

        // Replace event-specific placeholders first
        foreach (var kvp in eventPlaceholders)
        {
            result = result.Replace($"{{{kvp.Key}}}", kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        // Replace database variables
        result = await ReplaceDatabaseVariablesAsync(serverId, result);

        // Replace RCON variables (variables starting with {rcon.})
        result = await ReplaceRconVariablesAsync(serverId, result);

        return result;
    }

    /// <summary>
    /// Replace database variables like {playerCount}, {framerate}, etc.
    /// </summary>
    private async Task<string> ReplaceDatabaseVariablesAsync(int serverId, string text)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var server = await db.RconServers.FindAsync(serverId);
        if (server == null)
            return text;

        // Define mappings for database variables
        var dbVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{playerCount}", server.LatestPlayerCount?.ToString() ?? "0" },
            { "{joiningPlayers}", server.LatestJoiningPlayers?.ToString() ?? "0" },
            { "{queuedPlayers}", server.LatestQueuedPlayers?.ToString() ?? "0" },
            { "{framerate}", server.LatestFpsCount?.ToString() ?? "0" },
            { "{fps}", server.LatestFpsCount?.ToString() ?? "0" },
            { "{entityCount}", server.LatestEntityCount?.ToString() ?? "0" },
            { "{memory}", server.LatestMemoryUsage?.ToString() ?? "0" },
            { "{uptime}", server.LatestUptime?.ToString() ?? "0" },
            { "{serverName}", server.ServerHostname ?? "Unknown" },
            { "{hostname}", server.ServerHostname ?? "Unknown" },
            { "{map}", server.LatestMap ?? "Unknown" }
        };

        foreach (var kvp in dbVariables)
        {
            text = text.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    /// <summary>
    /// Replace RCON variables like {rcon.server.ping}
    /// </summary>
    private async Task<string> ReplaceRconVariablesAsync(int serverId, string text)
    {
        if (_rconBackgroundService == null)
        {
            _logger.LogWarning($"[Server {serverId}] RconBackgroundService not set, cannot replace RCON variables");
            return text;
        }

        // Find all {rcon.xxx} patterns
        var rconPattern = @"\{rcon\.([^}]+)\}";
        var matches = Regex.Matches(text, rconPattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            string fullMatch = match.Value; // e.g., {rcon.server.ping}
            string command = match.Groups[1].Value; // e.g., server.ping

            try
            {
                // Execute RCON command and get response
                var response = await _rconBackgroundService.ExecuteCommandWithResponse(command, serverId, timeoutMs: 3000);

                if (!string.IsNullOrEmpty(response))
                {
                    // Clean up response (remove extra whitespace, newlines, etc.)
                    response = response.Trim();
                    text = text.Replace(fullMatch, response, StringComparison.OrdinalIgnoreCase);
                    _logger.LogDebug($"[Server {serverId}] Replaced {fullMatch} with: {response}");
                }
                else
                {
                    _logger.LogWarning($"[Server {serverId}] RCON command '{command}' returned empty response");
                    text = text.Replace(fullMatch, "[no response]", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Server {serverId}] Error executing RCON command '{command}'");
                text = text.Replace(fullMatch, "[error]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }

    /// <summary>
    /// Execute the action based on action type
    /// </summary>
    private async Task<bool> ExecuteActionAsync(int serverId, string actionType, string actionValue, string? webhookUrl)
    {
        if (_rconBackgroundService == null)
        {
            _logger.LogError($"[Server {serverId}] RconBackgroundService not set, cannot execute action");
            return false;
        }

        try
        {
            switch (actionType.ToLower())
            {
                case "sendservermessage":
                    await _rconBackgroundService.SendChatMessageToServer(actionValue, serverId);
                    return true;

                case "customcommand":
                    _rconBackgroundService.SendRconCommand(actionValue, serverId);
                    return true;

                case "discordwebhook":
                case "triggerwebhook":
                    if (string.IsNullOrWhiteSpace(webhookUrl))
                    {
                        _logger.LogWarning("[Server {ServerId}] Webhook URL is empty for Discord Webhook action", serverId);
                        return false;
                    }
                    return await SendDiscordWebhookAsync(serverId, webhookUrl, actionValue);

                case "appnotification":
                    _logger.LogWarning("[Server {ServerId}] App notification trigger fired but push notifications are no longer supported", serverId);
                    return false;

                default:
                    _logger.LogWarning("[Server {ServerId}] Unknown action type: {ActionType}", serverId, actionType);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Server {serverId}] Error executing action {actionType}");
            return false;
        }
    }

    /// <summary>
    /// Send a message to a Discord webhook URL
    /// </summary>
    private async Task<bool> SendDiscordWebhookAsync(int serverId, string webhookUrl, string message)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = JsonSerializer.Serialize(new { content = message });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(webhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Server {ServerId}] Discord webhook sent successfully", serverId);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("[Server {ServerId}] Discord webhook failed with status {StatusCode}: {Body}",
                serverId, response.StatusCode, responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Server {ServerId}] Error sending Discord webhook", serverId);
            return false;
        }
    }

    /// <summary>
    /// Update trigger execution statistics in database
    /// </summary>
    private async Task UpdateTriggerExecutionStatusAsync(int triggerId, bool success, string? errorMessage)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var trigger = await db.Triggers.FindAsync(triggerId);
            if (trigger == null)
                return;

            trigger.LastTriggeredAt = DateTime.UtcNow;
            trigger.TriggerCount++;
            trigger.LastExecutionSuccess = success;
            trigger.LastExecutionError = errorMessage;
            trigger.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating trigger {triggerId} execution status");
        }
    }
}
