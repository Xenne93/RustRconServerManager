namespace RustRconServerManager.Backend.Models;

public class ChatMessage
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public bool IsFlagged { get; set; }
    public string? SteamId { get; set; }
    public string? PlayerName { get; set; }
    public string? Message { get; set; }
    public string? Channel { get; set; } // global, team
    public DateTime Timestamp { get; set; }
    
}