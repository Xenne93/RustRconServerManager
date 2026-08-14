namespace RustRconServerManager.Shared.Dashboard;

public class Dashboard_MapDataDTO
{
    public int ServerId { get; set; }
    public string? ImageUrl { get; set; }
    public int? MapSize { get; set; }
    public int? MapSeed { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool HasMapData { get; set; }
    public bool RrsmModInitialized { get; set; }
    public List<MapPlayerDTO> Players { get; set; } = new();
    public List<MapSleepingBagDTO> SleepingBags { get; set; } = new();
    public List<MapToolCupboardDTO> ToolCupboards { get; set; } = new();
}

public class MapPlayerDTO
{
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public class MapSleepingBagDTO
{
    public string OwnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// <summary>
    /// "sleepingbag" or "bed"
    /// </summary>
    public string Type { get; set; } = "sleepingbag";
}

public class MapToolCupboardDTO
{
    public string OwnerName { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public string AuthorizedPlayers { get; set; } = string.Empty;
}
