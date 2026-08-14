namespace RustRconServerManager.Shared.Scheduler;

public class PanelSettingsDto
{
    public int Id { get; set; }
    public int SystemProfileId { get; set; }
    public string TimezoneId { get; set; } = "UTC";
    public string MinimumLogLevel { get; set; } = "Error";
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool DeveloperModeEnabled { get; set; } = false;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DatabaseStatsDto
{
    public List<DatabaseStatEntry> Entries { get; set; } = new();
}

public class DatabaseStatEntry
{
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime? OldestRecord { get; set; }
    public bool CanPurge { get; set; }
}

public class PurgeDataRequestDto
{
    public string Category { get; set; } = string.Empty;
    public int OlderThanDays { get; set; }
}

public class SetLogLevelDto
{
    public string MinimumLogLevel { get; set; } = "Error";
}

public class SetAutoUpdateDto
{
    public bool AutoUpdateEnabled { get; set; } = true;
}

public class SetDeveloperModeDto
{
    public bool DeveloperModeEnabled { get; set; } = false;
}

public class DeveloperVacBanOverrideDto
{
    public int Id { get; set; }
    public string SteamId { get; set; } = string.Empty;
    public bool VACBanned { get; set; } = true;
    public int NumberOfVACBans { get; set; } = 1;
    public int DaysSinceLastBan { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
}

public class UpsertDeveloperVacBanOverrideDto
{
    public string SteamId { get; set; } = string.Empty;
    public bool VACBanned { get; set; } = true;
    public int NumberOfVACBans { get; set; } = 1;
    public int DaysSinceLastBan { get; set; } = 0;
}
