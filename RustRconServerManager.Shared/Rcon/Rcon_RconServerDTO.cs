namespace RustRconServerManager.Shared.Rcon;

public class Rcon_RconServerDTO
{
    public string ServerName { get; set; }
    public string ServerAddress { get; set; }
    public int RconPort { get; set; }
    public string Password { get; set; }
    public string ModFramework { get; set; } = "None";
    public bool RustRconServerManagerModInstalled { get; set; } = false;

    public bool? ConnectionPassed { get; set; }
    public int? ReturnedQueryPort { get; set; }
    public int? ReturnedGamePort { get; set; }
    public string? ReturnedServerName { get; set; }

}