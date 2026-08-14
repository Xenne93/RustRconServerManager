namespace RustRconServerManager.Shared.ServerProtection
{
    /// <summary>
    /// Response DTO for server protection settings
    /// </summary>
    public class ServerProtection_ResponseDTO
    {
        public string Message { get; set; } = string.Empty;
        public ServerProtectionSettingsDTO? Settings { get; set; }
    }

    /// <summary>
    /// Server protection settings
    /// </summary>
    public class ServerProtectionSettingsDTO
    {
        public int Id { get; set; }
        public int ServerId { get; set; }

        // Whitelist-only server mode
        public bool EnableWhitelistOnly { get; set; } = false;
        public string WhitelistOnlyKickMessage { get; set; } = "This server is restricted to whitelisted players only.";

        // Country filtering
        public CountryFilterMode CountryFilterMode { get; set; } = CountryFilterMode.None;
        public List<string> CountryList { get; set; } = new();

        // NOTE: Despite the name, this value is actually in HOURS (Rust banid expects hours)
        public int BanDurationMinutes { get; set; } = 24;

        // VAC ban protection
        public bool EnableVacBanProtection { get; set; } = false;
        public int MaxVacBans { get; set; } = 0;
        public int MinDaysSinceLastVACBan { get; set; } = 0;
        public VacBanAction VacBanAction { get; set; } = VacBanAction.Kick;

        // Private Steam profile protection
        public bool BlockPrivateSteamProfiles { get; set; } = false;
        public PrivateSteamProfileAction PrivateSteamProfileAction { get; set; } = PrivateSteamProfileAction.Kick;

        // VPN/Proxy detection
        public bool EnableVpnCheck { get; set; } = false;

        // VPN/Proxy protection
        public bool EnableVpnProtection { get; set; } = false;
        public VpnProtectionAction VpnProtectionAction { get; set; } = VpnProtectionAction.Kick;

        // SteamIDs that bypass all protection checks
        public List<WhitelistEntryDTO> WhitelistedSteamIds { get; set; } = new();

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Request DTO for updating server protection settings
    /// </summary>
    public class ServerProtection_UpdateSettingsDTO
    {
        // Whitelist-only server mode
        public bool EnableWhitelistOnly { get; set; } = false;
        public string WhitelistOnlyKickMessage { get; set; } = "This server is restricted to whitelisted players only.";

        public CountryFilterMode CountryFilterMode { get; set; } = CountryFilterMode.None;
        public List<string> CountryList { get; set; } = new();

        // NOTE: Despite the name, this value is actually in HOURS (Rust banid expects hours)
        public int BanDurationMinutes { get; set; } = 24;

        public bool EnableVacBanProtection { get; set; } = false;
        public int MaxVacBans { get; set; } = 0;
        public int MinDaysSinceLastVACBan { get; set; } = 0;
        public VacBanAction VacBanAction { get; set; } = VacBanAction.Kick;

        // Private Steam profile protection
        public bool BlockPrivateSteamProfiles { get; set; } = false;
        public PrivateSteamProfileAction PrivateSteamProfileAction { get; set; } = PrivateSteamProfileAction.Kick;

        // VPN/Proxy detection
        public bool EnableVpnCheck { get; set; } = false;

        // VPN/Proxy protection
        public bool EnableVpnProtection { get; set; } = false;
        public VpnProtectionAction VpnProtectionAction { get; set; } = VpnProtectionAction.Kick;

        // SteamIDs that bypass all protection checks
        public List<WhitelistEntryDTO> WhitelistedSteamIds { get; set; } = new();
    }

    /// <summary>
    /// Country filter mode enum
    /// </summary>
    public enum CountryFilterMode
    {
        None = 0,
        Whitelist = 1,
        Blacklist = 2
    }

    /// <summary>
    /// VAC ban action enum
    /// </summary>
    public enum VacBanAction
    {
        Kick = 0,
        Ban = 1
    }

    /// <summary>
    /// Legacy: kept only because the dormant `PublicBanAction` DB column still references it.
    /// No application logic uses it.
    /// </summary>
    public enum PublicBanAction
    {
        Kick = 0,
        Ban = 1
    }

    /// <summary>
    /// Private Steam profile action enum
    /// </summary>
    public enum PrivateSteamProfileAction
    {
        Kick = 0,
        Ban = 1
    }

    /// <summary>
    /// VPN protection action enum
    /// </summary>
    public enum VpnProtectionAction
    {
        Kick = 0,
        Ban = 1
    }

    /// <summary>
    /// Country DTO for dropdown
    /// </summary>
    public class CountryDTO
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// A single entry in the "ignore protection rules" list
    /// </summary>
    public class WhitelistEntryDTO
    {
        public string SteamId { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }
}
