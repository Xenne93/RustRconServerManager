namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Represents a trigger that executes commands based on server events.
/// </summary>
public class Trigger
{
    public int Id { get; set; }

    /// <summary>
    /// The ID of the server this trigger belongs to.
    /// </summary>
    public int RconServerId { get; set; }
    public RconServer RconServer { get; set; }

    /// <summary>
    /// Human-readable name for this trigger.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Description of what this trigger does.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The event type that triggers this action.
    /// Examples: PlayerJoin, PlayerLeave, PlayerDeath, PlayerKill, ChatMessage
    /// </summary>
    public string TriggerEvent { get; set; }

    /// <summary>
    /// The action type to execute.
    /// Options: SendServerMessage, TriggerWebhook, CustomCommand
    /// </summary>
    public string ActionType { get; set; }

    /// <summary>
    /// The command or message to execute when triggered.
    /// Supports variable placeholders:
    /// - Event variables: {player}, {steamid}, {killer}, {victim}, {message}, {channel}
    /// - Database variables: {playerCount}, {joiningPlayers}, {framerate}, {entityCount}, {memory}, {uptime}, {serverName}
    /// - RCON variables: {rcon.server.ping}, {rcon.fps}, etc.
    /// </summary>
    public string ActionValue { get; set; }

    /// <summary>
    /// For ChatMessage events: Contains, BeginsWith, EndsWith
    /// </summary>
    public string? ChatConditionType { get; set; }

    /// <summary>
    /// For ChatMessage events: The text to match against
    /// </summary>
    public string? ChatConditionValue { get; set; }

    /// <summary>
    /// For TriggerWebhook action: The webhook URL
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Optional delay in seconds before executing the command.
    /// </summary>
    public int? DelaySeconds { get; set; }

    /// <summary>
    /// Whether this trigger is currently active/enabled.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last time this trigger was activated.
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// How many times this trigger has been activated.
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    /// <summary>
    /// Whether execution succeeded last time.
    /// </summary>
    public bool? LastExecutionSuccess { get; set; }

    /// <summary>
    /// Error message from last execution (if any).
    /// </summary>
    public string? LastExecutionError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
