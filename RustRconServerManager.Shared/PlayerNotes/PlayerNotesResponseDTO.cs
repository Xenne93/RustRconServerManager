namespace RustRconServerManager.Shared.PlayerNotes;

public class PlayerNotesResponseDTO
{
    public List<PlayerNoteDTO> Notes { get; set; } = new();
    public int TotalCount { get; set; }
}
