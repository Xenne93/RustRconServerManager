namespace RustRconServerManager.Backend.Models;

public class SleepingBagData
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string OwnerSteamId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    /// <summary>
    /// "sleepingbag" or "bed"
    /// </summary>
    public string Type { get; set; } = "sleepingbag";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
