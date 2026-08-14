namespace RustRconServerManager.Backend.Models;

public class PlayerKillLog
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string? KilledPlayerId { get; set; }
    public string? KilledPlayerName { get; set; }
    public string? KilledById { get; set; }
    public string? KilledByName { get; set; }
    public bool IsPVP { get; set; } // True if both killer and victim are players (have Steam IDs)
    public DateTime? CreatedAt { get; set; }
}