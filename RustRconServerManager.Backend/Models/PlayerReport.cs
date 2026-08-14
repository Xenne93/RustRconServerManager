using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Represents a player report submitted in-game
/// </summary>
public class PlayerReport
{
    /// <summary>
    /// Unique identifier for the report
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The server ID where the report was made
    /// </summary>
    [Required]
    public int ServerId { get; set; }

    /// <summary>
    /// Steam ID of the player who made the report
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ReporterId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the player who made the report
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ReporterName { get; set; } = string.Empty;

    /// <summary>
    /// Steam ID of the player being reported
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ReportedId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the player being reported
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ReportedName { get; set; } = string.Empty;

    /// <summary>
    /// The subject/title of the report (includes type and description)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Additional message/details provided by the reporter
    /// </summary>
    [MaxLength(2000)]
    public string? Message { get; set; }

    /// <summary>
    /// The type of report (e.g., "break_server_rules", "cheating", "racism", etc.)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the report
    /// Possible values: "Pending", "Reviewed", "Actioned", "Dismissed"
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Admin notes/comments about the report
    /// </summary>
    [MaxLength(2000)]
    public string? AdminNotes { get; set; }

    /// <summary>
    /// User ID of the admin who reviewed the report
    /// </summary>
    public string? ReviewedByUserId { get; set; }

    /// <summary>
    /// Navigation property to the admin who reviewed the report
    /// </summary>
    [ForeignKey(nameof(ReviewedByUserId))]
    public ApplicationUser? ReviewedBy { get; set; }

    /// <summary>
    /// When the report was reviewed by an admin
    /// </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Whether the report has been archived
    /// </summary>
    [Required]
    public bool IsArchived { get; set; } = false;

    /// <summary>
    /// When the report was created
    /// </summary>
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the report was last updated
    /// </summary>
    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
