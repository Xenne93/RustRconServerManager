using Microsoft.AspNetCore.Identity;

namespace RustRconServerManager.Backend.Models;

public class ApplicationUser:IdentityUser
{
    public bool isAdmin { get; set; } = false;
    public bool isLoginBlocked { get; set; } = false;
    public int SystemProfileId { get; set; }

    public SystemProfile SystemProfile { get; set; }
    public string Theme { get; set; } = "light";
    public string? Nickname { get; set; }
    public string? Website { get; set; }
    public string? DiscordId { get; set; }
    public string? SteamId { get; set; }
    public string? SessionHash { get; set; } // Legacy single-session support, kept for backwards compatibility
    public int? SelectedServerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Collection of active sessions for this user (multi-session support)
    /// </summary>
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();

    /// <summary>
    /// Indicates if this user is a moderator with limited server access
    /// </summary>
    public bool IsModerator { get; set; } = false;

    /// <summary>
    /// Last login timestamp for moderators
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Servers this moderator has access to (only used if IsModerator is true)
    /// </summary>
    public ICollection<ModeratorServerPermission> ServerPermissions { get; set; } = new List<ModeratorServerPermission>();

    /// <summary>
    /// Pages this moderator can access (only used if IsModerator is true)
    /// </summary>
    public ICollection<ModeratorPagePermission> PagePermissions { get; set; } = new List<ModeratorPagePermission>();

    /// <summary>
    /// SHA256-hashed 6-digit password reset code
    /// </summary>
    public string? PasswordResetCode { get; set; }

    /// <summary>
    /// Expiry time for the password reset code (15 minutes from generation)
    /// </summary>
    public DateTime? PasswordResetCodeExpiry { get; set; }
}