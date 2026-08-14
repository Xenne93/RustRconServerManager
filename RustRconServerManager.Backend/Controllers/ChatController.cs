using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.LiveChatHub;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ChatController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get chat messages for a specific server with pagination
    /// </summary>
    /// <param name="serverId">Server ID to get messages for</param>
    /// <param name="skip">Number of messages to skip (for pagination/lazy-loading)</param>
    /// <param name="take">Number of messages to take (default 50)</param>
    /// <returns>List of chat messages ordered by timestamp descending</returns>
    [HttpGet("messages/{serverId}")]
    public async Task<ActionResult<List<LiveChatHub_ChatMessageDto>>> GetChatMessages(int serverId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        try
        {
            // Verify user has access to this server
            if (!await User.HasServerAccess(_dbContext, serverId))
            {
                return Unauthorized("No access to this server");
            }

            // Fetch chat messages in descending order (newest first)
            var messages = await _dbContext.ChatMessages
                .Where(m => m.ServerId == serverId)
                .OrderByDescending(m => m.Timestamp)
                .Skip(skip)
                .Take(take)
                .Select(m => new LiveChatHub_ChatMessageDto
                {
                    ServerId = m.ServerId,
                    Username = m.PlayerName ?? "Unknown",
                    SteamId = m.SteamId,
                    Message = m.Message ?? "",
                    Timestamp = m.Timestamp,
                    Channel = m.Channel ?? "0"
                })
                .ToListAsync();

            // Reverse to get chronological order (oldest first)
            messages.Reverse();

            return Ok(messages);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
        }
    }

    /// <summary>
    /// Get recent chat messages (last N messages)
    /// Useful for initial load of chat log
    /// </summary>
    [HttpGet("recent/{serverId}")]
    public async Task<ActionResult<List<LiveChatHub_ChatMessageDto>>> GetRecentChatMessages(int serverId, [FromQuery] int count = 100)
    {
        try
        {
            // Verify user has access to this server
            if (!await User.HasServerAccess(_dbContext, serverId))
            {
                return Unauthorized("No access to this server");
            }

            // Fetch the most recent messages
            var messages = await _dbContext.ChatMessages
                .Where(m => m.ServerId == serverId)
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .OrderBy(m => m.Timestamp) // Order chronologically
                .Select(m => new LiveChatHub_ChatMessageDto
                {
                    ServerId = m.ServerId,
                    Username = m.PlayerName ?? "Unknown",
                    SteamId = m.SteamId,
                    Message = m.Message ?? "",
                    Timestamp = m.Timestamp,
                    Channel = m.Channel ?? "0"
                })
                .ToListAsync();

            return Ok(messages);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
        }
    }

    /// <summary>
    /// Search chat messages for a specific server
    /// </summary>
    /// <param name="serverId">Server ID to search messages for</param>
    /// <param name="query">Search query (case-insensitive, searches in message and player name)</param>
    /// <param name="skip">Number of results to skip (for pagination)</param>
    /// <param name="take">Maximum number of results (default 200)</param>
    /// <returns>List of matching chat messages ordered by timestamp descending</returns>
    [HttpGet("search/{serverId}")]
    public async Task<ActionResult<List<LiveChatHub_ChatMessageDto>>> SearchChatMessages(
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

            // Search chat messages (case-insensitive) in message content or player name
            var messages = await _dbContext.ChatMessages
                .Where(m => m.ServerId == serverId &&
                    ((m.Message != null && EF.Functions.Like(m.Message, $"%{query}%")) ||
                     (m.PlayerName != null && EF.Functions.Like(m.PlayerName, $"%{query}%"))))
                .OrderByDescending(m => m.Timestamp)
                .Skip(skip)
                .Take(take)
                .Select(m => new LiveChatHub_ChatMessageDto
                {
                    ServerId = m.ServerId,
                    Username = m.PlayerName ?? "Unknown",
                    SteamId = m.SteamId,
                    Message = m.Message ?? "",
                    Timestamp = m.Timestamp,
                    Channel = m.Channel ?? "0"
                })
                .ToListAsync();

            return Ok(messages);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ApiErrorHelper.FormatError(ex) });
        }
    }
}
