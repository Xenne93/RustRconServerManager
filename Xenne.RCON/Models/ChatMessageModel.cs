namespace Xenne.RCON.Models;

public class ChatMessageModel
{
    public int Channel { get; set; }
    public string Message { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Color { get; set; } = "";
    public long Time { get; set; }
}
