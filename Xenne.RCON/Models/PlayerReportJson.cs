namespace Xenne.RCON.Models;

/// <summary>
/// Model for deserializing player report JSON data from RCON
/// </summary>
public class PlayerReportJson
{
    /// <summary>
    /// Steam ID of the player making the report
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the player making the report
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Steam ID of the player being reported
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the player being reported
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// The full subject line of the report (includes type and description)
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Additional message/details provided by the reporter
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The type of report (e.g., "break_server_rules", "cheating", etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;
}
