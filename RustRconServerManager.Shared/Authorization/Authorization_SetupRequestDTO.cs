namespace RustRconServerManager.Shared.Authorization;

public class Authorization_SetupRequestDTO
{
    public string Email { get; set; }
    public string Password { get; set; }
    public string ConfirmPassword { get; set; }

    /// <summary>
    /// Id of the LegalConsent record created earlier in the setup wizard (via /api/auth/consent),
    /// so it can be linked to the admin account once it's created.
    /// </summary>
    public int? ConsentId { get; set; }

    /// <summary>
    /// Whether the admin opted in to sending anonymous usage statistics (install
    /// check-ins, server/player/user counts - no personally identifiable data) during
    /// setup. Seeds PanelSettings.AnalyticsEnabled; adjustable later from Panel Settings.
    /// </summary>
    public bool EnableAnonymousAnalytics { get; set; }
}
