using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RustRconServerManager.Shared.ServerWebhooks;

namespace RustRconServerManager.Backend.Models;

[Table("ServerWebhookSettings")]
public class ServerWebhookSettings
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ServerId { get; set; }

    [ForeignKey("ServerId")]
    public RconServer? Server { get; set; }

    // Player Connect Event
    public bool EnablePlayerConnectWebhook { get; set; } = false;
    public string PlayerConnectWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerConnectFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerConnectTextContent { get; set; } = string.Empty;
    public string PlayerConnectCustomContent { get; set; } = string.Empty;

    // Player Disconnect Event
    public bool EnablePlayerDisconnectWebhook { get; set; } = false;
    public string PlayerDisconnectWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerDisconnectFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerDisconnectTextContent { get; set; } = string.Empty;
    public string PlayerDisconnectCustomContent { get; set; } = string.Empty;

    // Player Kill Event
    public bool EnablePlayerKillWebhook { get; set; } = false;
    public string PlayerKillWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerKillFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerKillTextContent { get; set; } = string.Empty;
    public string PlayerKillCustomContent { get; set; } = string.Empty;

    // Player Ban Event
    public bool EnablePlayerBanWebhook { get; set; } = false;
    public string PlayerBanWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerBanFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerBanTextContent { get; set; } = string.Empty;
    public string PlayerBanCustomContent { get; set; } = string.Empty;

    // Player Kick Event
    public bool EnablePlayerKickWebhook { get; set; } = false;
    public string PlayerKickWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerKickFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerKickTextContent { get; set; } = string.Empty;
    public string PlayerKickCustomContent { get; set; } = string.Empty;

    // Player Report Event
    public bool EnablePlayerReportWebhook { get; set; } = false;
    public string PlayerReportWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat PlayerReportFormat { get; set; } = WebhookFormat.Embed;
    public string PlayerReportTextContent { get; set; } = string.Empty;
    public string PlayerReportCustomContent { get; set; } = string.Empty;

    // Server Offline Event
    public bool EnableServerOfflineWebhook { get; set; } = false;
    public string ServerOfflineWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerOfflineFormat { get; set; } = WebhookFormat.Embed;
    public string ServerOfflineTextContent { get; set; } = string.Empty;
    public string ServerOfflineCustomContent { get; set; } = string.Empty;

    // Server Online Event
    public bool EnableServerOnlineWebhook { get; set; } = false;
    public string ServerOnlineWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerOnlineFormat { get; set; } = WebhookFormat.Embed;
    public string ServerOnlineTextContent { get; set; } = string.Empty;
    public string ServerOnlineCustomContent { get; set; } = string.Empty;

    // Server Protection Event
    public bool EnableServerProtectionWebhook { get; set; } = false;
    public string ServerProtectionWebhookUrl { get; set; } = string.Empty;
    public WebhookFormat ServerProtectionFormat { get; set; } = WebhookFormat.Embed;
    public string ServerProtectionTextContent { get; set; } = string.Empty;
    public string ServerProtectionCustomContent { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
