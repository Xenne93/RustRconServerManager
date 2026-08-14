namespace RustRconServerManager.Backend.Models;

public class StatsHistory
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    
    // The name of the stat (fps, memory, entitycount, playercount, latency)
    public string Stat { get; set; }
    
    public string Value { get; set; }
    public DateTime CreatedAt { get; set; }
}