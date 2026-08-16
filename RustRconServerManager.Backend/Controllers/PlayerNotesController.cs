using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.PlayerNotes;
using System.Security.Claims;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlayerNotesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PlayerNotesController> _logger;

    public PlayerNotesController(AppDbContext dbContext, ILogger<PlayerNotesController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all notes for a specific player
    /// </summary>
    [HttpGet("{steamId}")]
    public async Task<ActionResult<PlayerNotesResponseDTO>> GetPlayerNotes(string steamId, [FromQuery] int serverId)
    {
        try
        {
            // Check if user has access to this server
            if (!await User.HasServerAccess(_dbContext, serverId))
            {
                return Forbid();
            }

            var notes = await _dbContext.PlayerNotes
                .Where(n => n.SteamId == steamId && n.ServerId == serverId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            var response = new PlayerNotesResponseDTO
            {
                Notes = notes.Select(n => new PlayerNoteDTO
                {
                    Id = n.Id,
                    SteamId = n.SteamId,
                    ServerId = n.ServerId,
                    Note = n.Note,
                    CreatedBy = GetUsernameFromUserId(n.CreatedBy),
                    CreatedAt = n.CreatedAt,
                    UpdatedAt = n.UpdatedAt
                }).ToList(),
                TotalCount = notes.Count
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlayerNotesController] Error getting notes for player {SteamId}", LogSanitizer.Sanitize(steamId));
            return StatusCode(500, new { error = "Error retrieving player notes" });
        }
    }

    /// <summary>
    /// Create a new note for a player
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PlayerNoteDTO>> CreatePlayerNote([FromBody] CreatePlayerNoteRequest request)
    {
        try
        {
            // Check if user has access to this server
            if (!await User.HasServerAccess(_dbContext, request.ServerId))
            {
                return Forbid();
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var playerNote = new PlayerNote
            {
                SteamId = request.SteamId,
                ServerId = request.ServerId,
                Note = request.Note,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.PlayerNotes.Add(playerNote);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[PlayerNotesController] User {UserId} created note {NoteId} for player {SteamId} on server {ServerId}",
                userId, playerNote.Id, LogSanitizer.Sanitize(request.SteamId), request.ServerId);

            var dto = new PlayerNoteDTO
            {
                Id = playerNote.Id,
                SteamId = playerNote.SteamId,
                ServerId = playerNote.ServerId,
                Note = playerNote.Note,
                CreatedBy = GetUsernameFromUserId(userId),
                CreatedAt = playerNote.CreatedAt,
                UpdatedAt = playerNote.UpdatedAt
            };

            return CreatedAtAction(nameof(GetPlayerNotes), new { steamId = request.SteamId, serverId = request.ServerId }, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlayerNotesController] Error creating note for player {SteamId}", LogSanitizer.Sanitize(request.SteamId));
            return StatusCode(500, new { error = "Error creating player note" });
        }
    }

    /// <summary>
    /// Update an existing note
    /// </summary>
    [HttpPut("{noteId}")]
    public async Task<ActionResult<PlayerNoteDTO>> UpdatePlayerNote(int noteId, [FromBody] UpdatePlayerNoteRequest request)
    {
        try
        {
            if (noteId != request.NoteId)
            {
                return BadRequest(new { error = "Note ID mismatch" });
            }

            var playerNote = await _dbContext.PlayerNotes.FindAsync(noteId);
            if (playerNote == null)
            {
                return NotFound(new { error = "Note not found" });
            }

            // Check if user has access to this server
            if (!await User.HasServerAccess(_dbContext, playerNote.ServerId))
            {
                return Forbid();
            }

            playerNote.Note = request.Note;
            playerNote.UpdatedAt = DateTime.UtcNow;

            _dbContext.PlayerNotes.Update(playerNote);
            await _dbContext.SaveChangesAsync();

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _logger.LogInformation("[PlayerNotesController] User {UserId} updated note {NoteId} for player {SteamId}",
                userId, noteId, LogSanitizer.Sanitize(playerNote.SteamId));

            var dto = new PlayerNoteDTO
            {
                Id = playerNote.Id,
                SteamId = playerNote.SteamId,
                ServerId = playerNote.ServerId,
                Note = playerNote.Note,
                CreatedBy = GetUsernameFromUserId(playerNote.CreatedBy),
                CreatedAt = playerNote.CreatedAt,
                UpdatedAt = playerNote.UpdatedAt
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlayerNotesController] Error updating note {NoteId}", noteId);
            return StatusCode(500, new { error = "Error updating player note" });
        }
    }

    /// <summary>
    /// Delete a note
    /// </summary>
    [HttpDelete("{noteId}")]
    public async Task<ActionResult> DeletePlayerNote(int noteId)
    {
        try
        {
            var playerNote = await _dbContext.PlayerNotes.FindAsync(noteId);
            if (playerNote == null)
            {
                return NotFound(new { error = "Note not found" });
            }

            // Check if user has access to this server
            if (!await User.HasServerAccess(_dbContext, playerNote.ServerId))
            {
                return Forbid();
            }

            _dbContext.PlayerNotes.Remove(playerNote);
            await _dbContext.SaveChangesAsync();

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _logger.LogInformation("[PlayerNotesController] User {UserId} deleted note {NoteId} for player {SteamId}",
                userId, noteId, LogSanitizer.Sanitize(playerNote.SteamId));

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlayerNotesController] Error deleting note {NoteId}", noteId);
            return StatusCode(500, new { error = "Error deleting player note" });
        }
    }

    private string GetUsernameFromUserId(string userId)
    {
        try
        {
            var user = _dbContext.Users.Find(userId);
            return user?.UserName ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
