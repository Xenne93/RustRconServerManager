using System.ComponentModel.DataAnnotations;

namespace RustRconServerManager.Backend.Models;

public class PlayerNote
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(17)]
    public string SteamId { get; set; } = string.Empty;

    [Required]
    public int ServerId { get; set; }

    [Required]
    public string Note { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string CreatedBy { get; set; } = string.Empty; // ApplicationUser ID

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
