namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Panel settings for a SystemProfile including timezone and other preferences.
/// </summary>
public class PanelSettings
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to SystemProfile
    /// </summary>
    public int SystemProfileId { get; set; }

    /// <summary>
    /// Navigation property to SystemProfile
    /// </summary>
    public SystemProfile SystemProfile { get; set; } = null!;

    /// <summary>
    /// Timezone ID for the SystemProfile (e.g., "Europe/Berlin", "America/New_York", "UTC")
    /// All users in this SystemProfile will use this timezone for scheduling
    /// </summary>
    public string TimezoneId { get; set; } = "UTC";

    /// <summary>
    /// Minimum severity level the app logs (Trace, Debug, Information, Warning, Error,
    /// Critical). Adjustable at runtime from the Panel Settings page - defaults to Error
    /// so a fresh self-hosted install only logs genuine problems instead of routine RCON
    /// poll traffic.
    /// </summary>
    public string MinimumLogLevel { get; set; } = "Error";

    /// <summary>
    /// Whether the Docker container / standalone launcher should automatically download
    /// and install a newer release on startup. Adjustable at runtime from the Panel
    /// Settings page - mirrored to a flag file (see AutoUpdateFlagFileService) that the
    /// update-check scripts read before the app process even starts.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>
    /// When enabled, admins can assign fake VAC-ban profiles to specific SteamIDs (see
    /// DeveloperVacBanOverride) so the VAC-ban protection rules can be tested without a
    /// real banned account. Ignored entirely while disabled, even if overrides still exist.
    /// </summary>
    public bool DeveloperModeEnabled { get; set; } = false;

    /// <summary>
    /// Whether this installation sends anonymous usage statistics (install check-ins,
    /// server/player/user counts - no personally identifiable data) to the developer via
    /// a self-hosted Plausible instance. Opt-in: asked once during initial setup, and
    /// adjustable at any time from the Panel Settings page.
    /// </summary>
    public bool AnalyticsEnabled { get; set; } = false;

    /// <summary>
    /// When the settings were created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the settings were last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
