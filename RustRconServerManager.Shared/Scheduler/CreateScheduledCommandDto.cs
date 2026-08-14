namespace RustRconServerManager.Shared.Scheduler;

/// <summary>
/// DTO for creating a new scheduled command
/// </summary>
public class CreateScheduledCommandDto
{
    public int RconServerId { get; set; }
    public string Command { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int ScheduleTypeValue { get; set; } // Enum value: 0=OneTime, 1=Hourly, 2=Daily, 3=Weekly, 4=Monthly, 5=EveryXMinutes
    public int? IntervalHours { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? ExecutionHour { get; set; }
    public int? ExecutionMinute { get; set; }
    public string? DaysOfWeek { get; set; } // Comma-separated values like "1,3,5" for Mon,Wed,Fri
    public int? DayOfMonth { get; set; }
    public DateTime? ExecuteAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The browser's UTC offset in minutes (e.g., -60 for UTC+1, 60 for UTC-1).
    /// This is the value from JavaScript's new Date().getTimezoneOffset().
    /// Used to convert local execution times to UTC.
    /// </summary>
    public int? UtcOffsetMinutes { get; set; }
}
