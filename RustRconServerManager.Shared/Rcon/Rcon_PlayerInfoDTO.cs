namespace RustRconServerManager.Shared.Rcon;

public class Rcon_PlayerInfoDTO
{
    // Core fields (always present)
    public string SteamID { get; set; } = string.Empty;
    public string OwnerSteamID { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Ping { get; set; }
    public string Address { get; set; } = string.Empty;
    public double ConnectedSeconds { get; set; }
    public double Health { get; set; }

    // Violation level (different spelling with/without plugin)
    public double? VoiationLevel { get; set; } // RustAdmin plugin (typo)
    public double? ViolationLevel { get; set; } // Vanilla (correct spelling)

    // RustAdmin plugin fields (only present with plugin)
    public Position? Position { get; set; }
    public long? TeamId { get; set; }
    public long? NetworkId { get; set; }

    // Vanilla fields (only present without plugin)
    public long? EntityId { get; set; }
    public double? CurrentLevel { get; set; }
    public double? UnspentXp { get; set; }
}

public class Position
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}