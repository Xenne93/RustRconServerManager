using Microsoft.AspNetCore.Identity;

namespace RustRconServerManager.Backend.Models;

public class ApplicationUser:IdentityUser
{
    public bool isAdmin { get; set; } = false;
    public bool isLoginBlocked { get; set; } = false;
    public int SystemProfileId { get; set; }

    public SystemProfile SystemProfile { get; set; }
    public string Theme { get; set; } = "light";

    /// <summary>
    /// Name shown across the panel (navbar, moderator lists, audit log) instead of the
    /// email address. Required for every account going forward - existing accounts created
    /// before this field existed are prompted to set one on next login (see MainLayout).
    /// </summary>
    public string? DisplayName { get; set; }
    public string? Website { get; set; }

    /// <summary>
    /// True once this account has an explicitly chosen username (set at creation for every
    /// account going forward). Accounts created before username-based login existed had
    /// their username silently set equal to their email - this stays false for those until
    /// they pick a real one via the one-time forced prompt (see MainLayout).
    /// </summary>
    public bool HasChosenUsername { get; set; } = false;
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