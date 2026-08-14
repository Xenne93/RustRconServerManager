using System.ComponentModel.DataAnnotations;

namespace RustRconServerManager.Backend.Models;

public class UserNotificationPreference
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public int ServerId { get; set; }

    public bool ServerOnline { get; set; } = true;
    public bool ServerOffline { get; set; } = true;
    public bool PlayerOnline { get; set; } = false;
    public bool PlayerOffline { get; set; } = false;
    public bool PlayerBanned { get; set; } = true;
    public bool PlayerReported { get; set; } = true;
    public bool ServerProtection { get; set; } = true;

    // Navigation properties
    public ApplicationUser? User { get; set; }
    public RconServer? Server { get; set; }
}
