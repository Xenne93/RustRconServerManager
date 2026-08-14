namespace RustRconServerManager.Backend.Models;

public class ToolCupboardData
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string OwnerSteamId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    /// <summary>
    /// Comma-separated list of authorized player names
    /// </summary>
    public string AuthorizedPlayers { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
