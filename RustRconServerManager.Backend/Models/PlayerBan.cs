namespace RustRconServerManager.Backend.Models;

public class PlayerBan
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string? Group { get; set; }
    public string SteamId { get; set; }
    public string? Username { get; set; }
    public string? Notes { get; set; }
    public long? Expiry { get; set; }
    public string? InternalNote { get; set; }
    public bool IsGlobalBan { get; set; } = false;

    /// <summary>
    /// Whether this ban has been lifted (unbanned but record kept for history)
    /// </summary>
    public bool IsLifted { get; set; } = false;
    public DateTime? LiftedAt { get; set; }
    public string? LiftedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}