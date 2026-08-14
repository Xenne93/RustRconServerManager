namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Aggregated statistics for efficient long-term storage and graphing
/// Stores min/avg/max values per hour or day for each stat type
/// Flexible design allows easy addition of new stat types without schema changes
/// </summary>
public class AggregatedStat
{
    public int Id { get; set; }
    public int ServerId { get; set; }

    /// <summary>
    /// The timestamp bucket (start of hour or day)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Aggregation type: 0 = Hourly, 1 = Daily
    /// </summary>
    public AggregationType AggregationType { get; set; }

    /// <summary>
    /// Stat type: entitycount, players, memory, framerate, uptime, queued, joining, etc.
    /// </summary>
    public string Stat { get; set; }

    /// <summary>
    /// Average value for this stat in the time bucket
    /// </summary>
    public double? Avg { get; set; }

    /// <summary>
    /// Minimum value for this stat in the time bucket
    /// </summary>
    public double? Min { get; set; }

    /// <summary>
    /// Maximum value for this stat in the time bucket
    /// </summary>
    public double? Max { get; set; }

    /// <summary>
    /// Number of data points aggregated
    /// </summary>
    public int SampleCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum AggregationType
{
    Hourly = 0,
    Daily = 1
}
