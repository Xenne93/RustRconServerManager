namespace RustRconServerManager.Shared.PlayerNotes;

public class PlayerNoteDTO
{
    public int Id { get; set; }
    public string SteamId { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public string Note { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty; // Username of the admin
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
