namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Represents an active user session (login from a specific browser/device).
/// Allows users to be logged in from multiple locations simultaneously.
/// </summary>
public class UserSession
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to ApplicationUser
    /// </summary>
    public string UserId { get; set; }
    public ApplicationUser User { get; set; }

    /// <summary>
    /// Unique session identifier used in JWT token claims
    /// </summary>
    public string SessionHash { get; set; }

    /// <summary>
    /// IP address where this session was created
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    /// User-Agent of the browser/app where this session was created
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// Device/browser name for user-friendly session management
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    /// When this session was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this session was last used
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this session expires (based on token expiration)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether this session has been revoked/logged out
    /// </summary>
    public bool IsRevoked { get; set; } = false;
}
