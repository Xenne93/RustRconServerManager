namespace RustRconServerManager.Shared.PlayerList;

public class PlayerBan_BanDTO
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? Group { get; set; }
    public string SteamId { get; set; }
    public string? Username { get; set; }
    public string? Notes { get; set; }
    public long? Expiry { get; set; }
    public string? InternalNote { get; set; }
    public bool IsGlobalBan { get; set; }
    public bool IsLifted { get; set; }
    public DateTime? LiftedAt { get; set; }
    public string? LiftedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
