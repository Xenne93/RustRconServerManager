namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Defines which pages a moderator user can access
/// </summary>
public class ModeratorPagePermission
{
    public int Id { get; set; }

    /// <summary>
    /// The user ID of the moderator
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property to the user
    /// </summary>
    public ApplicationUser User { get; set; }

    /// <summary>
    /// The page route that the moderator can access (e.g., "dashboard", "players", "banlist")
    /// </summary>
    public string PageRoute { get; set; } = string.Empty;

    /// <summary>
    /// When this permission was granted
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
