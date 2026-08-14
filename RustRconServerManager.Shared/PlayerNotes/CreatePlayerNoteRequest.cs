using System.ComponentModel.DataAnnotations;

namespace RustRconServerManager.Shared.PlayerNotes;

public class CreatePlayerNoteRequest
{
    [Required]
    [StringLength(17)]
    public string SteamId { get; set; } = string.Empty;

    [Required]
    public int ServerId { get; set; }

    [Required]
    [StringLength(5000, MinimumLength = 1)]
    public string Note { get; set; } = string.Empty;
}
