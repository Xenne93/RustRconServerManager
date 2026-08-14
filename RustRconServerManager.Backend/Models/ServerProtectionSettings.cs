using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RustRconServerManager.Shared.ServerProtection;

namespace RustRconServerManager.Backend.Models
{
    [Table("ServerProtectionSettings")]
    public class ServerProtectionSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ServerId { get; set; }

        [ForeignKey("ServerId")]
        public RconServer? Server { get; set; }

        // Whitelist-only server mode
        public bool EnableWhitelistOnly { get; set; } = false;
        public string WhitelistOnlyKickMessage { get; set; } = "This server is restricted to whitelisted players only.";

        // Country filtering
        public CountryFilterMode CountryFilterMode { get; set; } = CountryFilterMode.None;

        // Store countries as comma-separated list (e.g., "US,GB,DE")
        public string CountryList { get; set; } = string.Empty;

        // NOTE: Despite the name "BanDurationMinutes", this value is actually in HOURS.
        // Rust's banid command expects hours. Renaming would require a database migration.
        // 0 = permanent, default 24 = 24 hours
        public int BanDurationMinutes { get; set; } = 24;

        // VAC ban protection
        public bool EnableVacBanProtection { get; set; } = false;
        public int MaxVacBans { get; set; } = 0;
        public int MinDaysSinceLastVACBan { get; set; } = 0;
        public VacBanAction VacBanAction { get; set; } = VacBanAction.Kick;

        // Public ban protection
        public bool EnablePublicBanProtection { get; set; } = false;
        public int MaxPublicBans { get; set; } = 0;
        public PublicBanAction PublicBanAction { get; set; } = PublicBanAction.Kick;

        // Private Steam profile protection
        public bool BlockPrivateSteamProfiles { get; set; } = false;
        public PrivateSteamProfileAction PrivateSteamProfileAction { get; set; } = PrivateSteamProfileAction.Kick;

        // VPN/Proxy detection via proxycheck.io
        public bool EnableVpnCheck { get; set; } = false;

        // VPN/Proxy protection (kick/ban players using VPN)
        public bool EnableVpnProtection { get; set; } = false;
        public VpnProtectionAction VpnProtectionAction { get; set; } = VpnProtectionAction.Kick;

        // Whitelist - SteamIDs that bypass all protection checks
        // Stored as comma-separated list (e.g., "76561198000000001,76561198000000002")
        public string WhitelistedSteamIds { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
