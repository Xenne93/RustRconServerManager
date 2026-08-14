namespace RustRconServerManager.Shared.Scheduler;

/// <summary>
/// Enumeration for schedule types
/// </summary>
public enum ScheduleType
{
    OneTime = 0,
    Hourly = 1,
    Daily = 2,
    Weekly = 3,
    Monthly = 4,
    EveryXMinutes = 5
}

/// <summary>
/// DTO for retrieving scheduled command information
/// </summary>
public class ScheduledCommandDto
{
    public int Id { get; set; }
    public int RconServerId { get; set; }
    public string Command { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int ScheduleTypeValue { get; set; } // Enum value: 0=OneTime, 1=Hourly, 2=Daily, 3=Weekly, 4=Monthly, 5=EveryXMinutes
    public int? IntervalHours { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? ExecutionHour { get; set; }
    public int? ExecutionMinute { get; set; }
    public string? DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTime? ExecuteAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    public DateTime? NextExecutionAt { get; set; }
    public int ExecutionCount { get; set; }
    public bool? LastExecutionSuccess { get; set; }
    public string? LastExecutionError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Returns a human-readable description of the schedule
    /// </summary>
    public string GetScheduleDescription()
    {
        var scheduleTypeEnum = (ScheduleType)ScheduleTypeValue;

        return scheduleTypeEnum switch
        {
            ScheduleType.OneTime => $"One-time execution at {ExecuteAt:yyyy-MM-dd HH:mm}",
            ScheduleType.EveryXMinutes => $"Every {IntervalMinutes ?? 1} minute(s)",
            ScheduleType.Hourly => $"Every {IntervalHours ?? 1} hour(s)",
            ScheduleType.Daily => $"Every day at {ExecutionHour:D2}:{ExecutionMinute:D2}",
            ScheduleType.Weekly => $"Every {string.Join(", ", ParseDaysOfWeek())} at {ExecutionHour:D2}:{ExecutionMinute:D2}",
            ScheduleType.Monthly => $"Day {DayOfMonth} of each month at {ExecutionHour:D2}:{ExecutionMinute:D2}",
            _ => "Unknown schedule type"
        };
    }

    private List<string> ParseDaysOfWeek()
    {
        if (string.IsNullOrEmpty(DaysOfWeek))
            return new();

        var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        return DaysOfWeek.Split(',')
            .Select(d => int.TryParse(d.Trim(), out var num) && num >= 0 && num < 7 ? dayNames[num] : "")
            .Where(d => !string.IsNullOrEmpty(d))
            .ToList();
    }
}
