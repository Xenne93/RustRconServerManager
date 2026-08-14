namespace RustRconServerManager.Shared.Setup;

/// <summary>
/// DTO for submitting legal consent during setup wizard
/// </summary>
public class LegalConsentSubmitDTO
{
    /// <summary>
    /// Whether user consents to sending anonymous metrics to the developer
    /// </summary>
    public bool ConsentAnonymousMetrics { get; set; }

    /// <summary>
    /// User must accept privacy policy (required)
    /// </summary>
    public bool AcceptedPrivacyPolicy { get; set; }

    /// <summary>
    /// User must accept Terms & Conditions (required)
    /// </summary>
    public bool AcceptedTermsAndConditions { get; set; }
}
