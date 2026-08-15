namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Records a mutating action taken by an admin or moderator - who, when, on which server,
/// what endpoint/command, and from which IP. Written by AuditLogActionFilter (covers all
/// controller POST/PUT/DELETE/PATCH actions) and directly by LiveConsoleHub (console/preset
/// commands, which run over SignalR and never pass through MVC action filters at all).
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;

    /// <summary>"Admin" or "Moderator" at the time the action was taken.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The server this action targeted, if one could be resolved (from a route/body
    /// parameter, or the user's currently selected server). Null for actions that aren't
    /// tied to a specific server (e.g. panel-wide settings).
    /// </summary>
    public int? ServerId { get; set; }

    /// <summary>e.g. "POST Rcon/SendCommand" or, for console/preset commands, "Console command".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable detail - the actual RCON command text for console/preset actions, or a
    /// compact "key=value" summary of simple (non-sensitive) route/body parameters for
    /// regular API actions. Never contains full request bodies or anything matching a
    /// password/key/secret/token parameter name.
    /// </summary>
    public string? Details { get; set; }

    public string IpAddress { get; set; } = string.Empty;
    public int? StatusCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
