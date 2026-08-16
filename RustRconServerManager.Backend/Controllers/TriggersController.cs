using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using System.Security.Claims;

namespace RustRconServerManager.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TriggersController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<TriggersController> _logger;

        public TriggersController(AppDbContext dbContext, ILogger<TriggersController> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get all triggers for the user's selected server
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<Trigger>>> GetTriggers()
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

                var triggers = await _dbContext.Triggers
                    .Where(t => t.RconServerId == serverId)
                    .OrderBy(t => t.Name)
                    .ToListAsync();

                return Ok(triggers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting triggers");
                return StatusCode(500, new { error = "Failed to retrieve triggers" });
            }
        }

        /// <summary>
        /// Get a specific trigger by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<Trigger>> GetTrigger(int id)
        {
            try
            {
                var trigger = await _dbContext.Triggers.FindAsync(id);

                if (trigger == null)
                    return NotFound();

                // Verify user has access to this server
                if (!await User.HasServerAccess(_dbContext, trigger.RconServerId))
                {
                    _logger.LogWarning("User {UserId} attempted to access trigger {TriggerId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, trigger.RconServerId);
                    return Forbid();
                }

                return Ok(trigger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trigger {TriggerId}", id);
                return StatusCode(500, new { error = "Failed to retrieve trigger" });
            }
        }

        /// <summary>
        /// Create a new trigger
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Trigger>> CreateTrigger([FromBody] CreateTriggerDto dto)
        {
            try
            {
                var user = await User.GetUser(_dbContext);
                int serverId = user.SelectedServerId ?? 0;

                if (serverId <= 0)
                {
                    return BadRequest("No server selected");
                }

                // Verify user has access to this server
                if (!await User.HasServerAccess(_dbContext, serverId))
                {
                    _logger.LogWarning("User {UserId} attempted to create trigger for server {ServerId} without authorization", user.Id, serverId);
                    return Forbid();
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Name is required" });

                if (string.IsNullOrWhiteSpace(dto.TriggerEvent))
                    return BadRequest(new { error = "Trigger event is required" });

                if (string.IsNullOrWhiteSpace(dto.ActionType))
                    return BadRequest(new { error = "Action type is required" });

                if (string.IsNullOrWhiteSpace(dto.ActionValue))
                    return BadRequest(new { error = "Action value is required" });

                // Create trigger entity
                var trigger = new Trigger
                {
                    RconServerId = serverId,
                    Name = dto.Name,
                    Description = dto.Description ?? string.Empty,
                    TriggerEvent = dto.TriggerEvent,
                    ActionType = dto.ActionType,
                    ActionValue = dto.ActionValue,
                    ChatConditionType = dto.ChatConditionType,
                    ChatConditionValue = dto.ChatConditionValue,
                    WebhookUrl = dto.WebhookUrl,
                    DelaySeconds = dto.DelaySeconds,
                    IsActive = dto.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = null
                };

                _dbContext.Triggers.Add(trigger);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("User {UserId} created trigger {TriggerId} for server {ServerId}", user.Id, trigger.Id, serverId);

                return CreatedAtAction(nameof(GetTrigger), new { id = trigger.Id }, trigger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating trigger");
                return StatusCode(500, new { error = "Failed to create trigger" });
            }
        }

        /// <summary>
        /// Update an existing trigger
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<Trigger>> UpdateTrigger(int id, [FromBody] CreateTriggerDto dto)
        {
            try
            {
                var existing = await _dbContext.Triggers.FindAsync(id);
                if (existing == null)
                    return NotFound();

                // Verify user has access to this server
                if (!await User.HasServerAccess(_dbContext, existing.RconServerId))
                {
                    _logger.LogWarning("User {UserId} attempted to update trigger {TriggerId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, existing.RconServerId);
                    return Forbid();
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Name is required" });

                if (string.IsNullOrWhiteSpace(dto.TriggerEvent))
                    return BadRequest(new { error = "Trigger event is required" });

                if (string.IsNullOrWhiteSpace(dto.ActionType))
                    return BadRequest(new { error = "Action type is required" });

                if (string.IsNullOrWhiteSpace(dto.ActionValue))
                    return BadRequest(new { error = "Action value is required" });

                // Update fields
                existing.Name = dto.Name;
                existing.Description = dto.Description ?? string.Empty;
                existing.TriggerEvent = dto.TriggerEvent;
                existing.ActionType = dto.ActionType;
                existing.ActionValue = dto.ActionValue;
                existing.ChatConditionType = dto.ChatConditionType;
                existing.ChatConditionValue = dto.ChatConditionValue;
                existing.WebhookUrl = dto.WebhookUrl;
                existing.DelaySeconds = dto.DelaySeconds;
                existing.IsActive = dto.IsActive;
                existing.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("User {UserId} updated trigger {TriggerId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id);

                return Ok(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating trigger {TriggerId}", id);
                return StatusCode(500, new { error = "Failed to update trigger" });
            }
        }

        /// <summary>
        /// Delete a trigger
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTrigger(int id)
        {
            try
            {
                var trigger = await _dbContext.Triggers.FindAsync(id);
                if (trigger == null)
                    return NotFound();

                // Verify user has access to this server
                if (!await User.HasServerAccess(_dbContext, trigger.RconServerId))
                {
                    _logger.LogWarning("User {UserId} attempted to delete trigger {TriggerId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, trigger.RconServerId);
                    return Forbid();
                }

                _dbContext.Triggers.Remove(trigger);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("User {UserId} deleted trigger {TriggerId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting trigger {TriggerId}", id);
                return StatusCode(500, new { error = "Failed to delete trigger" });
            }
        }

    }

    /// <summary>
    /// DTO for creating/updating triggers
    /// </summary>
    public class CreateTriggerDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string TriggerEvent { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionValue { get; set; } = string.Empty;
        public string? ChatConditionType { get; set; }
        public string? ChatConditionValue { get; set; }
        public string? WebhookUrl { get; set; }
        public int? DelaySeconds { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
