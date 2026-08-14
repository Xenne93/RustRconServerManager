namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Historical record of all player bans (active, expired, and lifted)
/// This table is append-only - records are never deleted, only marked as inactive
/// </summary>
public class PlayerBanHistory
{
    public int Id { get; set; }

    // Link to original ban
    public int? PlayerBanId { get; set; }

    // Player information
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;

    // Server information
    public int ServerId { get; set; }
    public bool IsGlobalBan { get; set; }

    // Ban details
    public string Reason { get; set; } = string.Empty;
    public DateTime BannedAt { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public long? DurationHours { get; set; }

    // Ban status
    public bool IsActive { get; set; }
    public string? LiftedBy { get; set; }
    public DateTime? LiftedAt { get; set; }
    public string? LiftReason { get; set; }

    // Who issued the ban
    public string? BannedBy { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
