using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.PresetCommand;

namespace RustRconServerManager.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PresetCommandsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<PresetCommandsController> _logger;

        public PresetCommandsController(
            AppDbContext dbContext,
            ILogger<PresetCommandsController> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get all preset commands for a specific server (includes global presets).
        /// </summary>
        [HttpGet("server/{serverId}")]
        public async Task<ActionResult<List<PresetCommandDto>>> GetPresetCommands(int serverId)
        {
            try
            {
                if (!await User.HasServerAccess(_dbContext, serverId))
                {
                    _logger.LogWarning($"[PresetCommandsController] User {User.GetEmail()} attempted to access server {serverId} without authorization");
                    return Forbid();
                }

                var commands = await _dbContext.PresetCommands
                    .Where(p => p.RconServerId == serverId || p.IsGlobal)
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.Name)
                    .ToListAsync();

                var dtos = commands.Select(MapToDto).ToList();
                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PresetCommandsController] Error getting preset commands for server {serverId}");
                return StatusCode(500, new { error = "Failed to retrieve preset commands" });
            }
        }

        /// <summary>
        /// Create a new preset command.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<PresetCommandDto>> CreatePresetCommand([FromBody] CreatePresetCommandDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Name is required" });

                if (string.IsNullOrWhiteSpace(dto.Command))
                    return BadRequest(new { error = "Command is required" });

                // If not global, verify server access
                if (!dto.IsGlobal && dto.RconServerId.HasValue)
                {
                    if (!await User.HasServerAccess(_dbContext, dto.RconServerId.Value))
                    {
                        _logger.LogWarning($"[PresetCommandsController] User {User.GetEmail()} attempted to create preset for server {dto.RconServerId} without authorization");
                        return Forbid();
                    }
                }

                var command = new PresetCommand
                {
                    RconServerId = dto.IsGlobal ? null : dto.RconServerId,
                    Name = dto.Name,
                    Command = dto.Command,
                    Description = dto.Description,
                    IsGlobal = dto.IsGlobal,
                    SortOrder = dto.SortOrder,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.PresetCommands.Add(command);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"[PresetCommandsController] User {User.GetEmail()} created preset command {command.Id} '{command.Name}'");

                return CreatedAtAction(nameof(GetPresetCommands), new { serverId = command.RconServerId ?? 0 }, MapToDto(command));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PresetCommandsController] Error creating preset command");
                return StatusCode(500, new { error = "Failed to create preset command" });
            }
        }

        /// <summary>
        /// Update an existing preset command.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<PresetCommandDto>> UpdatePresetCommand(int id, [FromBody] CreatePresetCommandDto dto)
        {
            try
            {
                var existing = await _dbContext.PresetCommands.FindAsync(id);
                if (existing == null)
                    return NotFound();

                // Verify access to the server the preset belongs to
                if (existing.RconServerId.HasValue && !await User.HasServerAccess(_dbContext, existing.RconServerId.Value))
                {
                    _logger.LogWarning($"[PresetCommandsController] User {User.GetEmail()} attempted to update preset {id} without authorization");
                    return Forbid();
                }

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Name is required" });

                if (string.IsNullOrWhiteSpace(dto.Command))
                    return BadRequest(new { error = "Command is required" });

                existing.RconServerId = dto.IsGlobal ? null : dto.RconServerId;
                existing.Name = dto.Name;
                existing.Command = dto.Command;
                existing.Description = dto.Description;
                existing.IsGlobal = dto.IsGlobal;
                existing.SortOrder = dto.SortOrder;
                existing.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"[PresetCommandsController] User {User.GetEmail()} updated preset command {id}");

                return Ok(MapToDto(existing));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PresetCommandsController] Error updating preset command {id}");
                return StatusCode(500, new { error = "Failed to update preset command" });
            }
        }

        /// <summary>
        /// Delete a preset command.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePresetCommand(int id)
        {
            try
            {
                var command = await _dbContext.PresetCommands.FindAsync(id);
                if (command == null)
                    return NotFound();

                // Verify access to the server the preset belongs to
                if (command.RconServerId.HasValue && !await User.HasServerAccess(_dbContext, command.RconServerId.Value))
                {
                    _logger.LogWarning($"[PresetCommandsController] User {User.GetEmail()} attempted to delete preset {id} without authorization");
                    return Forbid();
                }

                _dbContext.PresetCommands.Remove(command);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"[PresetCommandsController] User {User.GetEmail()} deleted preset command {id}");

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PresetCommandsController] Error deleting preset command {id}");
                return StatusCode(500, new { error = "Failed to delete preset command" });
            }
        }

        private static PresetCommandDto MapToDto(PresetCommand command)
        {
            return new PresetCommandDto
            {
                Id = command.Id,
                RconServerId = command.RconServerId,
                Name = command.Name,
                Command = command.Command,
                Description = command.Description,
                IsGlobal = command.IsGlobal,
                SortOrder = command.SortOrder,
                CreatedAt = command.CreatedAt,
                UpdatedAt = command.UpdatedAt
            };
        }
    }
}
