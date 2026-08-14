namespace RustRconServerManager.Backend.Interfaces;

/// <summary>
/// Interface for player protection service that enforces server protection rules
/// </summary>
public interface IPlayerProtectionService
{
    /// <summary>
    /// Checks if a player should be allowed to join based on protection settings
    /// </summary>
    /// <param name="serverId">The server ID</param>
    /// <param name="steamId">Player's Steam ID</param>
    /// <param name="playerName">Player's display name</param>
    /// <param name="ipAddress">Player's IP address</param>
    /// <returns>True if player is allowed, False if player should be kicked/banned</returns>
    Task<PlayerProtectionResult> CheckPlayerAsync(int serverId, string steamId, string playerName, string ipAddress);
}

/// <summary>
/// Result of player protection check
/// </summary>
public class PlayerProtectionResult
{
    public bool IsAllowed { get; set; }
    public string? Reason { get; set; }
    public PlayerProtectionAction Action { get; set; }
    // NOTE: Despite the name, this value is actually in HOURS (Rust banid expects hours)
    public int BanDurationMinutes { get; set; } = 0;
}

/// <summary>
/// Action to take when player fails protection check
/// </summary>
public enum PlayerProtectionAction
{
    None = 0,
    Kick = 1,
    Ban = 2
}
