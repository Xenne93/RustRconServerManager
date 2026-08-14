namespace RustRconServerManager.Shared.ServerWebhooks;

/// <summary>
/// Response DTO for webhook settings operations
/// </summary>
public class ServerWebhooks_ResponseDTO
{
    public string Message { get; set; } = string.Empty;
    public ServerWebhookSettingsDTO? Settings { get; set; }
}

/// <summary>
/// DTO for server webhook settings (response)
/// </summary>
public class ServerWebhookSettingsDTO
{
    public int Id { get; set; }
    public int ServerId { get; set; }

    // Player Connect Event
    public bool EnablePlayerConnectWebhook { get; set; }
    public string PlayerConnectWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerConnectFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerConnectTextContent { get; set; } = string.Empty;
    public string PlayerConnectCustomContent { get; set; } = string.Empty;

    // Player Disconnect Event
    public bool EnablePlayerDisconnectWebhook { get; set; }
    public string PlayerDisconnectWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerDisconnectFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerDisconnectTextContent { get; set; } = string.Empty;
    public string PlayerDisconnectCustomContent { get; set; } = string.Empty;

    // Player Kill Event
    public bool EnablePlayerKillWebhook { get; set; }
    public string PlayerKillWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerKillFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerKillTextContent { get; set; } = string.Empty;
    public string PlayerKillCustomContent { get; set; } = string.Empty;

    // Player Ban Event
    public bool EnablePlayerBanWebhook { get; set; }
    public string PlayerBanWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerBanFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerBanTextContent { get; set; } = string.Empty;
    public string PlayerBanCustomContent { get; set; } = string.Empty;

    // Player Kick Event
    public bool EnablePlayerKickWebhook { get; set; }
    public string PlayerKickWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerKickFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerKickTextContent { get; set; } = string.Empty;
    public string PlayerKickCustomContent { get; set; } = string.Empty;

    // Player Report Event
    public bool EnablePlayerReportWebhook { get; set; }
    public string PlayerReportWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerReportFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerReportTextContent { get; set; } = string.Empty;
    public string PlayerReportCustomContent { get; set; } = string.Empty;

    // Server Offline Event
    public bool EnableServerOfflineWebhook { get; set; }
    public string ServerOfflineWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerOfflineFormat { get; set; } = WebhookFormat.Embed;
    public string ServerOfflineTextContent { get; set; } = string.Empty;
    public string ServerOfflineCustomContent { get; set; } = string.Empty;

    // Server Online Event
    public bool EnableServerOnlineWebhook { get; set; }
    public string ServerOnlineWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerOnlineFormat { get; set; } = WebhookFormat.Embed;
    public string ServerOnlineTextContent { get; set; } = string.Empty;
    public string ServerOnlineCustomContent { get; set; } = string.Empty;

    // Server Protection Event
    public bool EnableServerProtectionWebhook { get; set; }
    public string ServerProtectionWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerProtectionFormat { get; set; } = WebhookFormat.Embed;
    public string ServerProtectionTextContent { get; set; } = string.Empty;
    public string ServerProtectionCustomContent { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO for updating webhook settings (request)
/// </summary>
public class ServerWebhooks_UpdateSettingsDTO
{
    // Player Connect Event
    public bool EnablePlayerConnectWebhook { get; set; }
    public string PlayerConnectWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerConnectFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerConnectTextContent { get; set; } = string.Empty;
    public string PlayerConnectCustomContent { get; set; } = string.Empty;

    // Player Disconnect Event
    public bool EnablePlayerDisconnectWebhook { get; set; }
    public string PlayerDisconnectWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerDisconnectFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerDisconnectTextContent { get; set; } = string.Empty;
    public string PlayerDisconnectCustomContent { get; set; } = string.Empty;

    // Player Kill Event
    public bool EnablePlayerKillWebhook { get; set; }
    public string PlayerKillWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerKillFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerKillTextContent { get; set; } = string.Empty;
    public string PlayerKillCustomContent { get; set; } = string.Empty;

    // Player Ban Event
    public bool EnablePlayerBanWebhook { get; set; }
    public string PlayerBanWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerBanFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerBanTextContent { get; set; } = string.Empty;
    public string PlayerBanCustomContent { get; set; } = string.Empty;

    // Player Kick Event
    public bool EnablePlayerKickWebhook { get; set; }
    public string PlayerKickWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerKickFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerKickTextContent { get; set; } = string.Empty;
    public string PlayerKickCustomContent { get; set; } = string.Empty;

    // Player Report Event
    public bool EnablePlayerReportWebhook { get; set; }
    public string PlayerReportWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerReportFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerReportTextContent { get; set; } = string.Empty;
    public string PlayerReportCustomContent { get; set; } = string.Empty;

    // Server Offline Event
    public bool EnableServerOfflineWebhook { get; set; }
    public string ServerOfflineWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerOfflineFormat { get; set; } = WebhookFormat.Embed;
    public string ServerOfflineTextContent { get; set; } = string.Empty;
    public string ServerOfflineCustomContent { get; set; } = string.Empty;

    // Server Online Event
    public bool EnableServerOnlineWebhook { get; set; }
    public string ServerOnlineWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerOnlineFormat { get; set; } = WebhookFormat.Embed;
    public string ServerOnlineTextContent { get; set; } = string.Empty;
    public string ServerOnlineCustomContent { get; set; } = string.Empty;

    // Server Protection Event
    public bool EnableServerProtectionWebhook { get; set; }
    public string ServerProtectionWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerProtectionFormat { get; set; } = WebhookFormat.Embed;
    public string ServerProtectionTextContent { get; set; } = string.Empty;
    public string ServerProtectionCustomContent { get; set; } = string.Empty;
}

/// <summary>
/// DTO for testing a webhook
/// </summary>
public class ServerWebhooks_TestWebhookDTO
{
    public string WebhookUrl { get; set; } = string.Empty;
    public WebhookEventType EventType { get; set; }
}

/// <summary>
/// Enum for webhook event types
/// </summary>
public enum WebhookEventType
{
    PlayerConnect = 0,
    PlayerDisconnect = 1,
    PlayerKill = 2,
    PlayerBan = 3,
    PlayerKick = 4,
    PlayerReport = 5,
    ServerOffline = 6,
    ServerOnline = 7,
    ServerProtection = 8
}

/// <summary>
/// Enum for webhook format types
/// </summary>
public enum WebhookFormat
{
    Text = 0,    // Simple text message
    Embed = 1,   // Default Discord embed
    Custom = 2   // Custom JSON with variables
}
