using RustRconServerManager.Shared.ServerWebhooks;

namespace RustRconServerManager.Backend.Interfaces;

/// <summary>
/// Interface for Discord webhook service that sends notifications for server events
/// </summary>
public interface IDiscordWebhookService
{
    /// <summary>
    /// Sends a Discord notification when a player connects to the server
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="playerName">Player's display name</param>
    /// <param name="steamId">Player's Steam ID</param>
    /// <param name="ipAddress">Player's IP address</param>
    /// <param name="country">Player's country (from IP geolocation)</param>
    Task SendPlayerConnectAsync(int serverId, string playerName, string steamId, string ipAddress, string? country);

    /// <summary>
    /// Sends a Discord notification when a player disconnects from the server
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="playerName">Player's display name</param>
    /// <param name="steamId">Player's Steam ID</param>
    Task SendPlayerDisconnectAsync(int serverId, string playerName, string steamId);

    /// <summary>
    /// Sends a Discord notification when a player kills another player
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="killerName">Killer's display name</param>
    /// <param name="killerId">Killer's Steam ID</param>
    /// <param name="victimName">Victim's display name</param>
    /// <param name="victimId">Victim's Steam ID</param>
    /// <param name="isPVP">Whether this is a PVP kill</param>
    Task SendPlayerKillAsync(int serverId, string killerName, string killerId, string victimName, string victimId, bool isPVP);

    /// <summary>
    /// Sends a Discord notification when a player is banned from the server
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="playerName">Player's display name</param>
    /// <param name="steamId">Player's Steam ID</param>
    /// <param name="reason">Ban reason</param>
    /// <param name="durationMinutes">Ban duration in minutes (0 = permanent)</param>
    Task SendPlayerBanAsync(int serverId, string playerName, string steamId, string reason, int durationMinutes);

    /// <summary>
    /// Sends a Discord notification when a player is kicked from the server
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="playerName">Player's display name</param>
    /// <param name="steamId">Player's Steam ID</param>
    /// <param name="reason">Kick reason</param>
    Task SendPlayerKickAsync(int serverId, string playerName, string steamId, string reason);

    /// <summary>
    /// Sends a Discord notification when a player report is submitted
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="reporterName">Reporter's display name</param>
    /// <param name="reporterId">Reporter's Steam ID</param>
    /// <param name="reportedName">Reported player's display name</param>
    /// <param name="reportedId">Reported player's Steam ID</param>
    /// <param name="type">Report type</param>
    /// <param name="subject">Report subject</param>
    /// <param name="message">Report message</param>
    Task SendPlayerReportAsync(int serverId, string reporterName, string reporterId, string reportedName, string reportedId, string type, string subject, string message);

    /// <summary>
    /// Sends a Discord notification when the server goes offline
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="serverName">Server name</param>
    Task SendServerOfflineAsync(int serverId, string serverName);

    /// <summary>
    /// Sends a Discord notification when the server comes back online
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="serverName">Server name</param>
    /// <param name="downtimeMinutes">How long the server was offline (in minutes)</param>
    Task SendServerOnlineAsync(int serverId, string serverName, int downtimeMinutes);

    /// <summary>
    /// Sends a Discord notification when a server protection rule blocks a player
    /// </summary>
    /// <param name="serverId">Server ID</param>
    /// <param name="playerName">Player's display name</param>
    /// <param name="steamId">Player's Steam ID</param>
    /// <param name="reason">Protection rule reason</param>
    /// <param name="actionTaken">Action taken (Kicked/Banned)</param>
    Task SendServerProtectionAsync(int serverId, string playerName, string steamId, string reason, string actionTaken);

    /// <summary>
    /// Sends a test Discord webhook message
    /// </summary>
    /// <param name="serverId">Server ID to get format settings</param>
    /// <param name="webhookUrl">Discord webhook URL</param>
    /// <param name="eventType">Event type to test</param>
    Task SendTestWebhookAsync(int serverId, string webhookUrl, WebhookEventType eventType);
}
