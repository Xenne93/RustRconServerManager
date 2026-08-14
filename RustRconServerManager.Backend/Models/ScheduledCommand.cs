using RustRconServerManager.Shared.Scheduler;

namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Represents a command scheduled to be executed at specific times on a server.
/// </summary>
public class ScheduledCommand
{
    public int Id { get; set; }

    /// <summary>
    /// The ID of the server this schedule belongs to.
    /// </summary>
    public int RconServerId { get; set; }
    public RconServer RconServer { get; set; }

    /// <summary>
    /// The command to execute (e.g., "say Hello everyone!")
    /// </summary>
    public string Command { get; set; }

    /// <summary>
    /// Human-readable name for this scheduled command.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Description of what this command does.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The schedule type: OneTime, Hourly, Daily, Weekly, Monthly
    /// </summary>
    public ScheduleType ScheduleType { get; set; }

    /// <summary>
    /// For EveryXMinutes: how many minutes between executions (e.g., 5, 10, 15, 30, 60)
    /// </summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>
    /// For Hourly: how many hours between executions (e.g., 1, 2, 6, 12, 24)
    /// </summary>
    public int? IntervalHours { get; set; }

    /// <summary>
    /// For Daily: the hour of day to execute (0-23)
    /// </summary>
    public int? ExecutionHour { get; set; }

    /// <summary>
    /// For Daily: the minute of the hour to execute (0-59)
    /// </summary>
    public int? ExecutionMinute { get; set; }

    /// <summary>
    /// For Weekly: day of week (0 = Sunday, 1 = Monday, ..., 6 = Saturday)
    /// Can be stored as comma-separated values for multiple days.
    /// </summary>
    public string? DaysOfWeek { get; set; }

    /// <summary>
    /// For Monthly: day of month (1-31)
    /// </summary>
    public int? DayOfMonth { get; set; }

    /// <summary>
    /// The specific date/time for one-time execution
    /// </summary>
    public DateTime? ExecuteAt { get; set; }

    /// <summary>
    /// Whether this schedule is currently active/enabled
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last time this command was executed
    /// </summary>
    public DateTime? LastExecutedAt { get; set; }

    /// <summary>
    /// Next scheduled execution time (calculated field for convenience)
    /// </summary>
    public DateTime? NextExecutionAt { get; set; }

    /// <summary>
    /// How many times this command has been executed
    /// </summary>
    public int ExecutionCount { get; set; } = 0;

    /// <summary>
    /// Whether execution succeeded last time
    /// </summary>
    public bool? LastExecutionSuccess { get; set; }

    /// <summary>
    /// Error message from last execution (if any)
    /// </summary>
    public string? LastExecutionError { get; set; }

    /// <summary>
    /// The user's browser UTC offset in minutes (from JS getTimezoneOffset).
    /// Stored so background recalculations use the correct offset.
    /// e.g., -120 for UTC+2, 60 for UTC-1.
    /// </summary>
    public int UtcOffsetMinutes { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
