using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Stores legal consent records including Terms & Conditions and Privacy agreements
/// Tracks when users accept T&C, privacy policy, and consent to telemetry/data sharing
/// </summary>
public class LegalConsent
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// IP address of the user at the time of consent
    /// </summary>
    [Required]
    [MaxLength(45)] // IPv6 max length
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// When the user accepted the terms
    /// </summary>
    [Required]
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether user consents to sending anonymous metrics to the developer
    /// </summary>
    [Required]
    public bool ConsentAnonymousMetrics { get; set; }

    /// <summary>
    /// Whether user accepted the privacy policy
    /// </summary>
    [Required]
    public bool AcceptedPrivacyPolicy { get; set; }

    /// <summary>
    /// Whether user accepted the Terms & Conditions
    /// </summary>
    [Required]
    public bool AcceptedTermsAndConditions { get; set; }

    /// <summary>
    /// Version of T&C accepted (for future reference if T&C changes)
    /// </summary>
    [MaxLength(20)]
    public string? TermsVersion { get; set; } = "1.0";

    /// <summary>
    /// Version of Privacy Policy accepted
    /// </summary>
    [MaxLength(20)]
    public string? PrivacyVersion { get; set; } = "1.0";

    /// <summary>
    /// Optional: Associated user ID if available at time of setup
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Navigation property to the user (if available)
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }
}
