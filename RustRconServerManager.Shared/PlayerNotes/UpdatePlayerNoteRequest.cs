using System.ComponentModel.DataAnnotations;

namespace RustRconServerManager.Shared.PlayerNotes;

public class UpdatePlayerNoteRequest
{
    [Required]
    public int NoteId { get; set; }

    [Required]
    [StringLength(5000, MinimumLength = 1)]
    public string Note { get; set; } = string.Empty;
}
