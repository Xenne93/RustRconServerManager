using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.LiveConsole;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConsoleController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ConsoleController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get recent console logs (last N entries)
    /// Useful for initial load of console log
    /// </summary>
    [HttpGet("recent/{serverId}")]
    public async Task<ActionResult<List<LiveConsole_RconLiveConsoleEntryDto>>> GetRecentConsoleLogs(int serverId, [FromQuery] int count = 100)
    {
        try
        {
            // Verify user has access to this server
            if (!await User.HasServerAccess(_dbContext, serverId))
            {
                return Unauthorized("No access to this server");
            }

            // Fetch the most recent log entries
            var logs = await _dbContext.RconLogEntries
                .Where(m => m.ServerId == serverId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(count)
                .OrderBy(m => m.CreatedAt) // Order chronologically
                .Select(m => new LiveConsole_RconLiveConsoleEntryDto
                {
                    Message = m.Message ?? "",
                    TimeStamp = m.CreatedAt
                })
                .ToListAsync();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
        }
    }

    /// <summary>
    /// Get console logs for a specific server with pagination
    /// </summary>
    /// <param name="serverId">Server ID to get logs for</param>
    /// <param name="skip">Number of logs to skip (for pagination/lazy-loading)</param>
    /// <param name="take">Number of logs to take (default 50)</param>
    /// <returns>List of console logs ordered by timestamp descending</returns>
    [HttpGet("messages/{serverId}")]
    public async Task<ActionResult<List<LiveConsole_RconLiveConsoleEntryDto>>> GetConsoleLogs(int serverId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        try
        {
            // Verify user has access to this server
            if (!await User.HasServerAccess(_dbContext, serverId))
            {
                return Unauthorized("No access to this server");
            }

            // Fetch console logs in descending order (newest first)
            var logs = await _dbContext.RconLogEntries
                .Where(m => m.ServerId == serverId)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(m => new LiveConsole_RconLiveConsoleEntryDto
                {
                    Message = m.Message ?? "",
                    TimeStamp = m.CreatedAt
                })
                .ToListAsync();

            // Reverse to get chronological order (oldest first)
            logs.Reverse();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
        }
    }

    /// <summary>
    /// Search console logs for a specific server
    /// </summary>
    /// <param name="serverId">Server ID to search logs for</param>
    /// <param name="query">Search query (case-insensitive)</param>
    /// <param name="skip">Number of results to skip (for pagination)</param>
    /// <param name="take">Maximum number of results (default 200)</param>
    /// <returns>List of matching console logs ordered by timestamp descending</returns>
    [HttpGet("search/{serverId}")]
    public async Task<ActionResult<List<LiveConsole_RconLiveConsoleEntryDto>>> SearchConsoleLogs(
        int serverId,
        [FromQuery] string query,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 200)
    {
        try
        {
            // Verify user has access to this server
            if (!await User.HasServerAccess(_dbContext, serverId))
            {
                return Unauthorized("No access to this server");
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required");
            }

            // Search console logs (case-insensitive)
            var logs = await _dbContext.RconLogEntries
                .Where(m => m.ServerId == serverId && m.Message != null && EF.Functions.Like(m.Message, $"%{query}%"))
                .OrderByDescending(m => m.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(m => new LiveConsole_RconLiveConsoleEntryDto
                {
                    Message = m.Message ?? "",
                    TimeStamp = m.CreatedAt
                })
                .ToListAsync();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
        }
    }
}
