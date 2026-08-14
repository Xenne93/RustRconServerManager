namespace RustRconServerManager.Shared.ManageServers;

public class ManageServers_ServerListEntryDTO
{
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? ServerAddress { get; set; }
    public int? RconPort { get; set; }
    public int? GamePort { get; set; }
    public int? QueryPort { get; set; }
    public bool? IsConnected { get; set; }
    public string? ModFramework { get; set; }
    public bool RustRconServerManagerModInstalled { get; set; }
    public bool RrsmModInitialized { get; set; }

}