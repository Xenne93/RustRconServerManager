namespace RustRconServerManager.Shared.Dashboard;

public class Dashboard_ServerBaseInformationDTO
{
    
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? ServerHostname { get; set; }
    public string? ServerAddress { get; set; }
    public int? GamePort { get; set; }
    public int? QueryPort { get; set; }
    public int? RconPort { get; set; }
    public int? LatestPlayerCount { get; set; }
    public int? LatestFpsCount { get; set; }
    public int? LatestEntityCount { get; set; }
    public int? LatestUptime { get; set; }
    public int? LatestMemory { get; set; }
}