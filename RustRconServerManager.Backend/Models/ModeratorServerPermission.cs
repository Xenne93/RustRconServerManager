namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Junction table linking moderator users to the servers they can access
/// </summary>
public class ModeratorServerPermission
{
    public int Id { get; set; }

    /// <summary>
    /// The moderator user (ApplicationUser with IsModerator = true)
    /// </summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; }

    /// <summary>
    /// The server the moderator has access to
    /// </summary>
    public int RconServerId { get; set; }
    public RconServer RconServer { get; set; }

    /// <summary>
    /// When this permission was granted
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
