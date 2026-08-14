using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.Scheduler;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Service for managing scheduled commands in the database.
/// All times are stored and compared in UTC.
/// </summary>
public class ScheduledCommandService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledCommandService> _logger;

    public ScheduledCommandService(IServiceScopeFactory scopeFactory, ILogger<ScheduledCommandService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all scheduled commands for a specific server.
    /// </summary>
    public async Task<List<ScheduledCommand>> GetScheduledCommandsByServerIdAsync(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.ScheduledCommands
            .Where(sc => sc.RconServerId == serverId)
            .OrderByDescending(sc => sc.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a specific scheduled command by ID.
    /// </summary>
    public async Task<ScheduledCommand> GetScheduledCommandByIdAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.ScheduledCommands
            .FirstOrDefaultAsync(sc => sc.Id == id);
    }

    /// <summary>
    /// Creates a new scheduled command. All times stored in UTC.
    /// </summary>
    public async Task<ScheduledCommand> CreateScheduledCommandAsync(ScheduledCommand command, int utcOffsetMinutes = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        command.CreatedAt = DateTime.UtcNow;
        command.UtcOffsetMinutes = utcOffsetMinutes;
        command.NextExecutionAt = CalculateNextExecutionTimeUtc(command, utcOffsetMinutes);

        dbContext.ScheduledCommands.Add(command);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation($"[ScheduledCommandService] Created scheduled command {command.Id} for server {command.RconServerId}, NextExecution={command.NextExecutionAt:yyyy-MM-dd HH:mm:ss}Z");
        return command;
    }

    /// <summary>
    /// Updates an existing scheduled command. All times stored in UTC.
    /// </summary>
    public async Task<ScheduledCommand> UpdateScheduledCommandAsync(ScheduledCommand command, int utcOffsetMinutes = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await dbContext.ScheduledCommands.FindAsync(command.Id);
        if (existing == null)
        {
            throw new ArgumentException($"Scheduled command {command.Id} not found");
        }

        existing.Name = command.Name;
        existing.Description = command.Description;
        existing.Command = command.Command;
        existing.ScheduleType = command.ScheduleType;
        existing.IntervalMinutes = command.IntervalMinutes;
        existing.IntervalHours = command.IntervalHours;
        existing.ExecutionHour = command.ExecutionHour;
        existing.ExecutionMinute = command.ExecutionMinute;
        existing.DaysOfWeek = command.DaysOfWeek;
        existing.DayOfMonth = command.DayOfMonth;
        existing.ExecuteAt = command.ExecuteAt;
        existing.IsActive = command.IsActive;
        existing.UtcOffsetMinutes = utcOffsetMinutes;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.NextExecutionAt = CalculateNextExecutionTimeUtc(existing, utcOffsetMinutes);

        dbContext.ScheduledCommands.Update(existing);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation($"[ScheduledCommandService] Updated scheduled command {command.Id}, NextExecution={existing.NextExecutionAt:yyyy-MM-dd HH:mm:ss}Z");
        return existing;
    }

    /// <summary>
    /// Deletes a scheduled command.
    /// </summary>
    public async Task DeleteScheduledCommandAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var command = await dbContext.ScheduledCommands.FindAsync(id);
        if (command != null)
        {
            dbContext.ScheduledCommands.Remove(command);
            await dbContext.SaveChangesAsync();
            _logger.LogInformation($"[ScheduledCommandService] Deleted scheduled command {id}");
        }
    }

    /// <summary>
    /// Gets all active scheduled commands that are due for execution.
    /// All NextExecutionAt values are in UTC, so we compare directly with DateTime.UtcNow.
    /// </summary>
    public async Task<List<ScheduledCommand>> GetDueScheduledCommandsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nowUtc = DateTime.UtcNow;

        // Query only due commands directly for efficiency (runs every 2 seconds)
        var dueCommands = await dbContext.ScheduledCommands
            .Where(sc => sc.IsActive && sc.NextExecutionAt.HasValue && sc.NextExecutionAt <= nowUtc)
            .ToListAsync();

        return dueCommands;
    }

    /// <summary>
    /// Called on startup: advances all stale scheduled commands to their next future execution time.
    /// Uses UTC offset 0 since we just need to advance past "now" in UTC.
    /// </summary>
    public async Task AdvanceStaleCommandsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nowUtc = DateTime.UtcNow;

        var staleCommands = await dbContext.ScheduledCommands
            .Where(sc => sc.IsActive && sc.NextExecutionAt.HasValue && sc.NextExecutionAt <= nowUtc)
            .ToListAsync();

        if (staleCommands.Count == 0)
            return;

        _logger.LogInformation($"[ScheduledCommandService] Found {staleCommands.Count} stale command(s) on startup — advancing to next future execution time");

        foreach (var command in staleCommands)
        {
            var oldNext = command.NextExecutionAt;

            if (command.ScheduleType == ScheduleType.OneTime)
            {
                command.IsActive = false;
                _logger.LogWarning($"[ScheduledCommandService] Deactivated missed one-time command {command.Id} ({command.Name}), was scheduled for {oldNext:yyyy-MM-dd HH:mm:ss}Z");
                continue;
            }

            // For interval-based types, advance from now
            if (command.ScheduleType == ScheduleType.EveryXMinutes)
            {
                command.NextExecutionAt = nowUtc.AddMinutes(command.IntervalMinutes ?? 1);
            }
            else if (command.ScheduleType == ScheduleType.Hourly)
            {
                command.NextExecutionAt = nowUtc.AddHours(command.IntervalHours ?? 1);
            }
            else
            {
                // Daily / Weekly / Monthly — use the stored UTC offset for correct local-to-UTC conversion
                command.NextExecutionAt = CalculateNextExecutionTimeUtc(command, command.UtcOffsetMinutes);
            }

            _logger.LogInformation($"[ScheduledCommandService] Advanced stale command {command.Id} ({command.Name}): {oldNext:yyyy-MM-dd HH:mm:ss}Z -> {command.NextExecutionAt:yyyy-MM-dd HH:mm:ss}Z");
        }

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Marks a scheduled command as executed and calculates next execution time.
    /// </summary>
    public async Task MarkAsExecutedAsync(int id, bool success = true, string errorMessage = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var command = await dbContext.ScheduledCommands.FindAsync(id);
        if (command != null)
        {
            var oldNextExecution = command.NextExecutionAt;
            command.LastExecutedAt = DateTime.UtcNow;
            command.ExecutionCount++;
            command.LastExecutionSuccess = success;
            command.LastExecutionError = errorMessage;

            if (command.ScheduleType != ScheduleType.OneTime)
            {
                // Recalculate next execution using the stored UTC offset
                command.NextExecutionAt = CalculateNextExecutionTimeUtc(command, command.UtcOffsetMinutes);
            }
            else
            {
                command.IsActive = false;
            }

            var oldExecStr = oldNextExecution.HasValue ? oldNextExecution.Value.ToString("yyyy-MM-dd HH:mm:ss") + "Z" : "NULL";
            var newExecStr = command.NextExecutionAt.HasValue ? command.NextExecutionAt.Value.ToString("yyyy-MM-dd HH:mm:ss") + "Z" : "NULL";

            dbContext.ScheduledCommands.Update(command);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation($"[ScheduledCommandService] Marked command {id} ({command.Name}) as executed. Success: {success}. Old NextExec: {oldExecStr}, New NextExec: {newExecStr}");

            if (!string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogWarning($"[ScheduledCommandService] Execution error for command {id}: {errorMessage}");
            }
        }
        else
        {
            _logger.LogWarning($"[ScheduledCommandService] Command {id} not found when marking as executed");
        }
    }

    /// <summary>
    /// Calculates the next execution time in UTC.
    /// ExecutionHour/ExecutionMinute are in the user's local time.
    /// utcOffsetMinutes is the browser's getTimezoneOffset() value (e.g., -60 for UTC+1).
    /// When utcOffsetMinutes is 0, it means either UTC or recalculating from existing UTC-stored NextExecutionAt.
    /// </summary>
    public static DateTime? CalculateNextExecutionTimeUtc(ScheduledCommand command, int utcOffsetMinutes = 0)
    {
        var nowUtc = DateTime.UtcNow;

        // For interval-based schedules, base on last execution or now
        if (command.ScheduleType == ScheduleType.EveryXMinutes)
        {
            if (command.LastExecutedAt.HasValue)
            {
                var next = command.LastExecutedAt.Value.AddMinutes(command.IntervalMinutes ?? 1);
                return next <= nowUtc ? nowUtc.AddMinutes(command.IntervalMinutes ?? 1) : next;
            }
            return nowUtc.AddMinutes(command.IntervalMinutes ?? 1);
        }

        if (command.ScheduleType == ScheduleType.Hourly)
        {
            if (command.LastExecutedAt.HasValue)
            {
                var next = command.LastExecutedAt.Value.AddHours(command.IntervalHours ?? 1);
                return next <= nowUtc ? nowUtc.AddHours(command.IntervalHours ?? 1) : next;
            }
            return nowUtc.AddHours(command.IntervalHours ?? 1);
        }

        if (command.ScheduleType == ScheduleType.OneTime)
        {
            if (command.ExecuteAt.HasValue)
            {
                // ExecuteAt from frontend is in local time — convert to UTC
                return command.ExecuteAt.Value.AddMinutes(utcOffsetMinutes);
            }
            return null;
        }

        // For Daily/Weekly/Monthly: ExecutionHour and ExecutionMinute are in user's local time.
        // Convert to UTC hour/minute for calculation.
        var localHour = command.ExecutionHour ?? 0;
        var localMinute = command.ExecutionMinute ?? 0;

        // Convert local time to UTC: local time + offsetMinutes = UTC
        // (JS getTimezoneOffset returns positive for west of UTC, negative for east)
        // e.g., UTC+2 → offsetMinutes = -120 → UTC = local + (-120) minutes... NO
        // Actually: getTimezoneOffset() returns the difference in minutes between UTC and local time.
        // UTC = local time + getTimezoneOffset() minutes
        // e.g., for UTC+2: getTimezoneOffset() = -120, so UTC = local + (-120) = local - 2h ✓
        var localTimeToday = nowUtc.Date.AddHours(localHour).AddMinutes(localMinute);
        var utcTimeToday = localTimeToday.AddMinutes(utcOffsetMinutes);
        var utcHour = utcTimeToday.Hour;
        var utcMinute = utcTimeToday.Minute;
        // The date might shift when converting (e.g., 01:00 UTC+2 → 23:00 previous day UTC)
        var dayShift = (utcTimeToday.Date - localTimeToday.Date).Days;

        return command.ScheduleType switch
        {
            ScheduleType.Daily => CalculateNextDailyExecution(nowUtc, utcHour, utcMinute),

            ScheduleType.Weekly => CalculateNextWeeklyExecution(nowUtc, command.DaysOfWeek, utcHour, utcMinute, dayShift),

            ScheduleType.Monthly => CalculateNextMonthlyExecution(nowUtc, command.DayOfMonth ?? 1, utcHour, utcMinute, dayShift),

            _ => null
        };
    }

    private static DateTime CalculateNextDailyExecution(DateTime nowUtc, int hour, int minute)
    {
        var next = nowUtc.Date.AddHours(hour).AddMinutes(minute);

        if (next <= nowUtc)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    private static DateTime CalculateNextWeeklyExecution(DateTime nowUtc, string daysOfWeekStr, int hour, int minute, int dayShift = 0)
    {
        if (string.IsNullOrEmpty(daysOfWeekStr))
        {
            return nowUtc.AddDays(7);
        }

        var daysOfWeek = daysOfWeekStr.Split(',')
            .Select(d => int.Parse(d.Trim()))
            .Select(d =>
            {
                // Adjust day of week for timezone day shift
                var adjusted = d + dayShift;
                if (adjusted < 0) adjusted += 7;
                if (adjusted > 6) adjusted -= 7;
                return adjusted;
            })
            .OrderBy(d => d)
            .ToList();

        var current = nowUtc.Date.AddHours(hour).AddMinutes(minute);
        var currentDayOfWeek = (int)nowUtc.DayOfWeek;

        // Check if we can execute today
        if (daysOfWeek.Contains(currentDayOfWeek) && current > nowUtc)
        {
            return current;
        }

        // Find next valid day
        for (var i = 1; i <= 7; i++)
        {
            var checkDate = nowUtc.AddDays(i);
            var checkDayOfWeek = (int)checkDate.DayOfWeek;

            if (daysOfWeek.Contains(checkDayOfWeek))
            {
                return checkDate.Date.AddHours(hour).AddMinutes(minute);
            }
        }

        return nowUtc.AddDays(7);
    }

    private static DateTime CalculateNextMonthlyExecution(DateTime nowUtc, int dayOfMonth, int hour, int minute, int dayShift = 0)
    {
        // Adjust day of month for timezone day shift
        var adjustedDay = dayOfMonth + dayShift;
        if (adjustedDay < 1) adjustedDay = 1;

        var effectiveDay = Math.Min(adjustedDay, DateTime.DaysInMonth(nowUtc.Year, nowUtc.Month));
        var next = new DateTime(nowUtc.Year, nowUtc.Month, effectiveDay)
            .AddHours(hour)
            .AddMinutes(minute);

        if (next <= nowUtc)
        {
            var nextMonth = nowUtc.AddMonths(1);
            effectiveDay = Math.Min(adjustedDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            next = new DateTime(nextMonth.Year, nextMonth.Month, effectiveDay)
                .AddHours(hour)
                .AddMinutes(minute);
        }

        return next;
    }
}
