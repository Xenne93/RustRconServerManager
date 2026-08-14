namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Represents a preset RCON command that can be quickly executed from the console page.
/// </summary>
public class PresetCommand
{
    public int Id { get; set; }

    /// <summary>
    /// The ID of the server this preset belongs to. Null if global.
    /// </summary>
    public int? RconServerId { get; set; }
    public RconServer? RconServer { get; set; }

    /// <summary>
    /// Display name for this preset command.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The command to execute (e.g., "kick {player} AFK")
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this command does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this preset is available on all servers.
    /// </summary>
    public bool IsGlobal { get; set; }

    /// <summary>
    /// Sort order for display (lower values appear first).
    /// </summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
