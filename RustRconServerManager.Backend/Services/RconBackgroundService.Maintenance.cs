using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Cleanup and stats aggregation loops.
/// </summary>
public partial class RconBackgroundService
{
    /// <summary>
    /// Cleanup loop that runs every hour to delete old data (RconLogEntries, StatsHistories, ChatMessages)
    /// </summary>
    private async Task CleanupLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RconBackgroundService] Cleanup loop starting... (Retention: {Minutes} minutes, Interval: {Seconds}s)",
            _dataRetentionMinutes, _cleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_cleanupInterval), stoppingToken);
                _logger.LogInformation("[RconBackgroundService] Running cleanup task...");

                await CleanupOldDataAsync();
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("[RconBackgroundService] Cleanup loop cancelled.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RconBackgroundService] Unexpected error during cleanup task.");
            }
        }

        _logger.LogInformation("[RconBackgroundService] Cleanup loop stopped.");
    }

    /// <summary>
    /// Deletes old RconLogEntries, StatsHistories, and ChatMessages older than the retention period
    /// </summary>
    private async Task CleanupOldDataAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoffDate = DateTime.UtcNow.AddMinutes(-_dataRetentionMinutes);
            _logger.LogInformation("[CLEANUP] Deleting data older than {CutoffDate} ({Minutes} minutes ago)",
                cutoffDate, _dataRetentionMinutes);

            // Delete old RconLogEntries
            var deletedLogs = await db.RconLogEntries
                .Where(e => e.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync();

            _logger.LogInformation("[CLEANUP] Deleted {Count} old RconLogEntries", deletedLogs);

            // Delete old StatsHistories
            var deletedStats = await db.StatsHistories
                .Where(s => s.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync();

            _logger.LogInformation("[CLEANUP] Deleted {Count} old StatsHistories", deletedStats);

            // Delete old ChatMessages
            var deletedChat = await db.ChatMessages
                .Where(c => c.Timestamp < cutoffDate)
                .ExecuteDeleteAsync();

            _logger.LogInformation("[CLEANUP] Deleted {Count} old ChatMessages", deletedChat);

            // Delete SteamPlayer records that have no SteamId (stale/invalid entries)
            var deletedOrphanPlayers = await db.SteamPlayers
                .Where(p => p.SteamId == null || p.SteamId == "")
                .ExecuteDeleteAsync();

            if (deletedOrphanPlayers > 0)
            {
                _logger.LogInformation("[CLEANUP] Deleted {Count} SteamPlayer records without SteamId", deletedOrphanPlayers);
            }

            // Delete expired bans (Expiry is a Unix timestamp, not null and not -1 means temporary)
            var nowUnix = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var expiredBans = await db.PlayerBans
                .Where(b => b.Expiry != null && b.Expiry != -1 && b.Expiry < nowUnix)
                .ToListAsync();

            if (expiredBans.Any())
            {
                // Mark ban history as inactive for expired bans
                var expiredSteamIds = expiredBans.Select(b => b.SteamId).Distinct().ToList();
                var historyRecords = await db.PlayerBanHistories
                    .Where(h => expiredSteamIds.Contains(h.SteamId) && h.IsActive)
                    .ToListAsync();

                foreach (var history in historyRecords)
                {
                    history.IsActive = false;
                    history.LiftedAt = DateTime.UtcNow;
                    history.LiftedBy = "System (expired)";
                    history.LiftReason = "Ban expired";
                    history.UpdatedAt = DateTime.UtcNow;
                }

                db.PlayerBans.RemoveRange(expiredBans);
                await db.SaveChangesAsync();

                _logger.LogInformation("[CLEANUP] Removed {Count} expired bans", expiredBans.Count);
            }

            _logger.LogInformation("[CLEANUP] Cleanup completed. Total deleted: {Total} records",
                deletedLogs + deletedStats + deletedChat + expiredBans.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CLEANUP] Error during cleanup task");
        }
    }

    /// <summary>
    /// Aggregation loop that periodically checks if the previous hour needs to be aggregated.
    /// Stats are aggregated into HOURLY buckets (e.g., 13:00-14:00, 14:00-15:00).
    /// The loop runs frequently to ensure timely aggregation, but each hour is only aggregated once.
    /// </summary>
    private async Task AggregationLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RconBackgroundService] Stats aggregation loop starting... (Check interval: {Seconds}s, Raw retention: {Days} days)",
            _aggregationInterval, _rawStatsRetentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_aggregationInterval), stoppingToken);
                _logger.LogDebug("[AGGREGATION] Checking if previous hour needs aggregation...");

                await AggregateStatsAsync();
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("[RconBackgroundService] Aggregation loop cancelled.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RconBackgroundService] Unexpected error during aggregation task.");
            }
        }

        _logger.LogInformation("[RconBackgroundService] Aggregation loop stopped.");
    }

    /// <summary>
    /// Aggregates raw StatsHistories data into hourly AggregatedStats and cleans up old raw data.
    ///
    /// How it works:
    /// - Calculates the PREVIOUS complete hour (e.g., at 14:30 UTC, it processes 13:00-14:00 UTC)
    /// - For each server and stat type, creates ONE aggregated record with min/avg/max values
    /// - Skips if aggregation already exists for that hour (idempotent)
    /// - After aggregation, cleans up raw StatsHistories older than _rawStatsRetentionDays
    ///
    /// This approach ensures:
    /// - Only complete hours are aggregated (never partial data)
    /// - Long-term storage is efficient (1 record per stat per hour instead of hundreds)
    /// - Raw data is kept for 7 days for detailed analysis, then only aggregated data remains
    /// </summary>
    private async Task AggregateStatsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Calculate the previous complete hour bucket
            // Example: At 14:30 UTC, lastHourStart = 13:00 UTC, lastHourEnd = 14:00 UTC
            var now = DateTime.UtcNow;
            var lastHourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-1);
            var lastHourEnd = lastHourStart.AddHours(1);

            _logger.LogDebug("[AGGREGATION] Processing hour bucket: {Start} to {End}", lastHourStart, lastHourEnd);

            // Get all servers
            var serverIds = await db.RconServers.Select(s => s.Id).ToListAsync();

            int totalAggregated = 0;

            foreach (var serverId in serverIds)
            {
                // Get raw stats for the last hour
                var hourlyStats = await db.StatsHistories
                    .Where(s => s.ServerId == serverId &&
                               s.CreatedAt >= lastHourStart &&
                               s.CreatedAt < lastHourEnd)
                    .ToListAsync();

                if (hourlyStats.Count == 0)
                {
                    _logger.LogDebug("[AGGREGATION] Server {ServerId}: No stats to aggregate for {Hour}", serverId, lastHourStart);
                    continue;
                }

                // Get unique stat types
                var statTypes = hourlyStats.Select(s => s.Stat).Distinct().ToList();

                int serverAggregatedCount = 0;

                // Create aggregated stat for each stat type
                foreach (var statType in statTypes)
                {
                    // Check if we already have aggregated data for this hour and stat type
                    var existingAgg = await db.AggregatedStats
                        .FirstOrDefaultAsync(a => a.ServerId == serverId &&
                                                 a.Timestamp == lastHourStart &&
                                                 a.Stat == statType &&
                                                 a.AggregationType == AggregationType.Hourly);

                    if (existingAgg != null)
                    {
                        _logger.LogDebug("[AGGREGATION] Server {ServerId}: Hourly aggregation already exists for {Hour} - {StatType}",
                            serverId, lastHourStart, statType);
                        continue;
                    }

                    // Get values for this stat type
                    var values = hourlyStats
                        .Where(s => s.Stat == statType)
                        .Select(s => double.TryParse(s.Value, out double v) ? v : 0)
                        .ToList();

                    if (values.Count == 0)
                        continue;

                    // Create aggregated stat
                    var aggregated = new AggregatedStat
                    {
                        ServerId = serverId,
                        Timestamp = lastHourStart,
                        AggregationType = AggregationType.Hourly,
                        Stat = statType,
                        Avg = values.Average(),
                        Min = values.Min(),
                        Max = values.Max(),
                        SampleCount = values.Count,
                        CreatedAt = DateTime.UtcNow
                    };

                    db.AggregatedStats.Add(aggregated);
                    serverAggregatedCount++;
                    totalAggregated++;
                }

                if (serverAggregatedCount > 0)
                {
                    _logger.LogInformation("[AGGREGATION] Server {ServerId}: Created {Count} hourly aggregations for {Hour}",
                        serverId, serverAggregatedCount, lastHourStart);
                }
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("[AGGREGATION] Created {Count} hourly aggregations", totalAggregated);

            // Clean up old raw stats (older than retention period)
            var rawStatsRetentionCutoff = DateTime.UtcNow.AddDays(-_rawStatsRetentionDays);
            var deletedRawStats = await db.StatsHistories
                .Where(s => s.CreatedAt < rawStatsRetentionCutoff)
                .ExecuteDeleteAsync();

            if (deletedRawStats > 0)
            {
                _logger.LogInformation("[AGGREGATION] Deleted {Count} old raw StatsHistories (older than {Days} days)",
                    deletedRawStats, _rawStatsRetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AGGREGATION] Error during stats aggregation");
        }
    }
}
