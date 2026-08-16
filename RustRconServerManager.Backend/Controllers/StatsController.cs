using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Helpers;

namespace RustRconServerManager.Backend.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class StatsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<StatsController> _logger;

        public StatsController(AppDbContext dbContext, ILogger<StatsController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Get stats history for a specific stat type and time range
        /// </summary>
        /// <param name="statType">Type of stat: fps, players, memory</param>
        /// <param name="timeRange">Time range: hour, day, week, month</param>
        [HttpGet("{statType}/{timeRange}")]
        public async Task<IActionResult> GetStats(string statType, string timeRange)
        {
            try
            {
                var user = await User.GetUser(_dbContext);
                int serverId = user.SelectedServerId ?? 0;

                if (serverId <= 0)
                {
                    return BadRequest("No server selected");
                }

                // Check if user has access to this server
                bool hasAccess = await User.HasServerAccess(_dbContext, serverId);
                if (!hasAccess)
                {
                    return Unauthorized("User does not have access to this server");
                }

                // Calculate the start time based on time range
                DateTime startTime = timeRange.ToLower() switch
                {
                    "hour" => DateTime.UtcNow.AddHours(-1),
                    "day" => DateTime.UtcNow.AddDays(-1),
                    "week" => DateTime.UtcNow.AddDays(-7),
                    "month" => DateTime.UtcNow.AddDays(-30),
                    _ => DateTime.UtcNow.AddHours(-1)
                };

                // Use hybrid approach: raw stats for last 7 days, aggregated for older
                var rawStatsRetentionDays = 7;
                var rawStatsCutoff = DateTime.UtcNow.AddDays(-rawStatsRetentionDays);

                List<dynamic> stats = new List<dynamic>();

                // If time range extends beyond 7 days, use aggregated data for old portion
                if (startTime < rawStatsCutoff)
                {
                    // Query aggregated stats for old data (before 7 days ago)
                    var aggregatedStats = await _dbContext.AggregatedStats
                        .Where(s => s.ServerId == serverId
                            && s.Stat == statType
                            && s.AggregationType == AggregationType.Hourly
                            && s.Timestamp >= startTime
                            && s.Timestamp < rawStatsCutoff)
                        .OrderBy(s => s.Timestamp)
                        .ToListAsync();

                    // Convert aggregated stats to same format as raw stats (using Avg value)
                    foreach (var aggStat in aggregatedStats)
                    {
                        stats.Add(new
                        {
                            timestamp = aggStat.Timestamp,
                            value = aggStat.Avg?.ToString() ?? "0"
                        });
                    }
                }

                // Query raw stats for recent data (last 7 days)
                var recentStartTime = startTime < rawStatsCutoff ? rawStatsCutoff : startTime;
                var recentStats = await _dbContext.StatsHistories
                    .Where(s => s.ServerId == serverId
                        && s.Stat == statType
                        && s.CreatedAt >= recentStartTime)
                    .OrderBy(s => s.CreatedAt)
                    .Select(s => new
                    {
                        timestamp = s.CreatedAt,
                        value = s.Value
                    })
                    .ToListAsync();

                // Combine aggregated and recent stats
                stats.AddRange(recentStats);

                _logger.LogInformation("Retrieved {StatsCount} stats records for server {ServerId}, stat type '{StatType}', time range '{TimeRange}'",
                    stats.Count, serverId, LogSanitizer.Sanitize(statType), LogSanitizer.Sanitize(timeRange));

                // For player count, round to whole numbers; for other stats, use decimals
                bool isPlayerCount = statType.ToLower() == "players";

                // Generate complete timeline with all intervals filled
                var now = DateTime.UtcNow;
                List<object> completeTimeline;

                switch (timeRange.ToLower())
                {
                    case "hour":
                        // Last hour: 10-minute intervals (6 intervals)
                        completeTimeline = new List<object>();
                        for (int i = 5; i >= 0; i--)
                        {
                            var intervalStart = now.AddMinutes(-(i + 1) * 10);
                            var intervalEnd = now.AddMinutes(-i * 10);
                            var bucketTime = new DateTime(intervalStart.Year, intervalStart.Month, intervalStart.Day, intervalStart.Hour, (intervalStart.Minute / 10) * 10, 0);

                            // Half-open [start, end) so a data point exactly on a bucket boundary
                            // (e.g. an hourly aggregate timestamped at exact midnight) is counted in
                            // only one bucket instead of both the preceding and following one.
                            var dataInInterval = stats.Where(s => s.timestamp >= intervalStart && s.timestamp < intervalEnd).ToList();
                            var avgValue = dataInInterval.Any()
                                ? dataInInterval.Average(x => double.TryParse(x.value, out double v) ? v : 0)
                                : 0;

                            completeTimeline.Add(new
                            {
                                timestamp = bucketTime,
                                value = isPlayerCount
                                    ? Math.Round(avgValue, 0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                                    : avgValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            });
                        }
                        break;

                    case "day":
                        // Last 24 hours: hourly intervals (24 intervals)
                        completeTimeline = new List<object>();
                        for (int i = 23; i >= 0; i--)
                        {
                            var intervalStart = now.AddHours(-(i + 1));
                            var intervalEnd = now.AddHours(-i);
                            var bucketTime = new DateTime(intervalStart.Year, intervalStart.Month, intervalStart.Day, intervalStart.Hour, 0, 0);

                            // Half-open [start, end) so a data point exactly on a bucket boundary
                            // (e.g. an hourly aggregate timestamped at exact midnight) is counted in
                            // only one bucket instead of both the preceding and following one.
                            var dataInInterval = stats.Where(s => s.timestamp >= intervalStart && s.timestamp < intervalEnd).ToList();
                            var avgValue = dataInInterval.Any()
                                ? dataInInterval.Average(x => double.TryParse(x.value, out double v) ? v : 0)
                                : 0;

                            completeTimeline.Add(new
                            {
                                timestamp = bucketTime,
                                value = isPlayerCount
                                    ? Math.Round(avgValue, 0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                                    : avgValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            });
                        }
                        break;

                    case "week":
                        // Last 7 days: daily intervals (7 intervals)
                        completeTimeline = new List<object>();
                        for (int i = 6; i >= 0; i--)
                        {
                            var intervalStart = now.AddDays(-(i + 1)).Date;
                            var intervalEnd = now.AddDays(-i).Date;
                            if (i == 0) intervalEnd = now; // Last day goes up to now
                            var bucketTime = intervalStart;

                            // Half-open [start, end) so a data point exactly on a bucket boundary
                            // (e.g. an hourly aggregate timestamped at exact midnight) is counted in
                            // only one bucket instead of both the preceding and following one.
                            var dataInInterval = stats.Where(s => s.timestamp >= intervalStart && s.timestamp < intervalEnd).ToList();
                            var avgValue = dataInInterval.Any()
                                ? dataInInterval.Average(x => double.TryParse(x.value, out double v) ? v : 0)
                                : 0;

                            completeTimeline.Add(new
                            {
                                timestamp = bucketTime,
                                value = isPlayerCount
                                    ? Math.Round(avgValue, 0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                                    : avgValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            });
                        }
                        break;

                    case "month":
                        // Last 30 days: daily intervals (30 intervals)
                        completeTimeline = new List<object>();
                        for (int i = 29; i >= 0; i--)
                        {
                            var intervalStart = now.AddDays(-(i + 1)).Date;
                            var intervalEnd = now.AddDays(-i).Date;
                            if (i == 0) intervalEnd = now; // Last day goes up to now
                            var bucketTime = intervalStart;

                            // Half-open [start, end) so a data point exactly on a bucket boundary
                            // (e.g. an hourly aggregate timestamped at exact midnight) is counted in
                            // only one bucket instead of both the preceding and following one.
                            var dataInInterval = stats.Where(s => s.timestamp >= intervalStart && s.timestamp < intervalEnd).ToList();
                            var avgValue = dataInInterval.Any()
                                ? dataInInterval.Average(x => double.TryParse(x.value, out double v) ? v : 0)
                                : 0;

                            completeTimeline.Add(new
                            {
                                timestamp = bucketTime,
                                value = isPlayerCount
                                    ? Math.Round(avgValue, 0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                                    : avgValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            });
                        }
                        break;

                    default:
                        completeTimeline = stats.Select(s => new
                        {
                            timestamp = s.timestamp,
                            value = s.value
                        } as object).ToList();
                        break;
                }

                return Ok(completeTimeline);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats for {StatType} over {TimeRange}", LogSanitizer.Sanitize(statType), LogSanitizer.Sanitize(timeRange));
                return StatusCode(500, ApiErrorHelper.FormatError("Error getting stats", ex));
            }
        }
    }
}
