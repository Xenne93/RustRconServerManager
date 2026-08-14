using System.ComponentModel.DataAnnotations;

namespace RustRconServerManager.Backend.Models;

public class DevicePushToken
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string Platform { get; set; } = string.Empty; // "android" or "ios"

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ApplicationUser? User { get; set; }
}
