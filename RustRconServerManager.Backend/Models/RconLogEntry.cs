namespace RustRconServerManager.Backend.Models;

// Raw RCON log entry
public class RconLogEntry
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}