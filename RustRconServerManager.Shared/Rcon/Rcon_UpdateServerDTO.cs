namespace RustRconServerManager.Shared.Rcon;

public class Rcon_UpdateServerDTO
{
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = string.Empty;
    public int RconPort { get; set; }
    public string? Password { get; set; }
    public string ModFramework { get; set; } = "None";
    public bool RustRconServerManagerModInstalled { get; set; } = false;
}
