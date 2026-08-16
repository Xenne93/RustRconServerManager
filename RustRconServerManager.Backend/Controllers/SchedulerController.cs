using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.Scheduler;
using ScheduleType = RustRconServerManager.Shared.Scheduler.ScheduleType;
using System.Security.Claims;
using Xenne.RCON;

namespace RustRconServerManager.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SchedulerController : ControllerBase
    {
        private readonly ScheduledCommandService _scheduledCommandService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<SchedulerController> _logger;
        private readonly RconConnectionManager _rconConnectionManager;

        public SchedulerController(
            ScheduledCommandService scheduledCommandService,
            AppDbContext dbContext,
            ILogger<SchedulerController> logger,
            RconConnectionManager rconConnectionManager)
        {
            _scheduledCommandService = scheduledCommandService ?? throw new ArgumentNullException(nameof(scheduledCommandService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rconConnectionManager = rconConnectionManager ?? throw new ArgumentNullException(nameof(rconConnectionManager));
        }

        /// <summary>
        /// Get all scheduled commands for a specific server
        /// </summary>
        [HttpGet("server/{serverId}")]
        public async Task<ActionResult<List<ScheduledCommandDto>>> GetScheduledCommands(int serverId)
        {
            try
            {
                if (!await User.HasServerAccess(_dbContext, serverId))
                {
                    _logger.LogWarning("[SchedulerController] User {UserId} attempted to access server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, serverId);
                    return Forbid();
                }

                var commands = await _scheduledCommandService.GetScheduledCommandsByServerIdAsync(serverId);
                var dtos = commands.Select(MapToDto).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerController] Error getting scheduled commands for server {ServerId}", serverId);
                return StatusCode(500, new { error = "Failed to retrieve scheduled commands" });
            }
        }


        /// <summary>
        /// Create a new scheduled command
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ScheduledCommandDto>> CreateScheduledCommand([FromBody] CreateScheduledCommandDto dto)
        {
            try
            {
                if (!await User.HasServerAccess(_dbContext, dto.RconServerId))
                {
                    _logger.LogWarning("[SchedulerController] User {UserId} attempted to create command for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, dto.RconServerId);
                    return Forbid();
                }

                if (string.IsNullOrWhiteSpace(dto.Command))
                    return BadRequest(new { error = "Command is required" });

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Name is required" });

                var validationError = ValidateScheduleType(dto);
                if (validationError != null)
                    return BadRequest(new { error = validationError });

                var command = new ScheduledCommand
                {
                    RconServerId = dto.RconServerId,
                    Command = dto.Command,
                    Name = dto.Name,
                    Description = dto.Description ?? "",
                    ScheduleType = (ScheduleType)dto.ScheduleTypeValue,
                    IntervalMinutes = dto.IntervalMinutes,
                    IntervalHours = dto.IntervalHours,
                    ExecutionHour = dto.ExecutionHour,
                    ExecutionMinute = dto.ExecutionMinute,
                    DaysOfWeek = dto.DaysOfWeek,
                    DayOfMonth = dto.DayOfMonth,
                    ExecuteAt = dto.ExecuteAt,
                    IsActive = dto.IsActive
                };

                var utcOffset = dto.UtcOffsetMinutes ?? 0;
                var created = await _scheduledCommandService.CreateScheduledCommandAsync(command, utcOffset);

                _logger.LogInformation("[SchedulerController] User {UserId} created scheduled command {CommandId} for server {ServerId} (utcOffset={UtcOffset})", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, created.Id, created.RconServerId, utcOffset);

                return Ok(MapToDto(created));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerController] Error creating scheduled command");
                return StatusCode(500, new { error = "Failed to create scheduled command" });
            }
        }

        /// <summary>
        /// Update an existing scheduled command
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<ScheduledCommandDto>> UpdateScheduledCommand(int id, [FromBody] CreateScheduledCommandDto dto)
        {
            try
            {
                var existing = await _scheduledCommandService.GetScheduledCommandByIdAsync(id);
                if (existing == null)
                    return NotFound();

                if (!await User.HasServerAccess(_dbContext, existing.RconServerId))
                {
                    _logger.LogWarning("[SchedulerController] User {UserId} attempted to update command {CommandId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, existing.RconServerId);
                    return Forbid();
                }

                if (string.IsNullOrWhiteSpace(dto.Command))
                    return BadRequest(new { error = "Command is required" });

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Name is required" });

                var validationError = ValidateScheduleType(dto);
                if (validationError != null)
                    return BadRequest(new { error = validationError });

                existing.Command = dto.Command;
                existing.Name = dto.Name;
                existing.Description = dto.Description ?? "";
                existing.ScheduleType = (ScheduleType)dto.ScheduleTypeValue;
                existing.IntervalMinutes = dto.IntervalMinutes;
                existing.IntervalHours = dto.IntervalHours;
                existing.ExecutionHour = dto.ExecutionHour;
                existing.ExecutionMinute = dto.ExecutionMinute;
                existing.DaysOfWeek = dto.DaysOfWeek;
                existing.DayOfMonth = dto.DayOfMonth;
                existing.ExecuteAt = dto.ExecuteAt;
                existing.IsActive = dto.IsActive;

                var utcOffset = dto.UtcOffsetMinutes ?? 0;
                var updated = await _scheduledCommandService.UpdateScheduledCommandAsync(existing, utcOffset);

                _logger.LogInformation("[SchedulerController] User {UserId} updated scheduled command {CommandId} (utcOffset={UtcOffset})", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, utcOffset);

                return Ok(MapToDto(updated));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerController] Error updating scheduled command {CommandId}", id);
                return StatusCode(500, new { error = "Failed to update scheduled command" });
            }
        }

        /// <summary>
        /// Delete a scheduled command
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteScheduledCommand(int id)
        {
            try
            {
                var command = await _scheduledCommandService.GetScheduledCommandByIdAsync(id);
                if (command == null)
                    return NotFound();

                if (!await User.HasServerAccess(_dbContext, command.RconServerId))
                {
                    _logger.LogWarning("[SchedulerController] User {UserId} attempted to delete command {CommandId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, command.RconServerId);
                    return Forbid();
                }

                await _scheduledCommandService.DeleteScheduledCommandAsync(id);

                _logger.LogInformation("[SchedulerController] User {UserId} deleted scheduled command {CommandId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerController] Error deleting scheduled command {CommandId}", id);
                return StatusCode(500, new { error = "Failed to delete scheduled command" });
            }
        }

        /// <summary>
        /// Execute a scheduled command immediately without updating the schedule
        /// </summary>
        [HttpPost("{id}/execute-now")]
        public async Task<IActionResult> ExecuteNow(int id)
        {
            try
            {
                var command = await _scheduledCommandService.GetScheduledCommandByIdAsync(id);
                if (command == null)
                    return NotFound();

                if (!await User.HasServerAccess(_dbContext, command.RconServerId))
                {
                    _logger.LogWarning("[SchedulerController] User {UserId} attempted to execute command {CommandId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, command.RconServerId);
                    return Forbid();
                }

                if (!_rconConnectionManager.TryGetClient(command.RconServerId, out var client) || !client.IsConnected)
                {
                    _logger.LogWarning("[SchedulerController] Server {ServerId} is not connected", command.RconServerId);
                    await _scheduledCommandService.MarkAsExecutedAsync(id, false, "Server not connected");
                    return BadRequest(new { error = "Server is not connected" });
                }

                try
                {
                    _logger.LogInformation("[SchedulerController] User {UserId} executing command {CommandId} ({CommandName}) manually: {Command}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, LogSanitizer.Sanitize(command.Name), LogSanitizer.Sanitize(command.Command));
                    await client.SendCommandAsync(command.Command);

                    _logger.LogInformation("[SchedulerController] Command {CommandId} executed successfully", id);

                    return Ok(new { message = "Command executed successfully" });
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "[SchedulerController] Failed to send command {CommandId} to RCON", id);
                    return BadRequest(new { error = $"Failed to execute command: {sendEx.Message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerController] Error executing command {CommandId}", id);
                return StatusCode(500, new { error = "Failed to execute command" });
            }
        }

        /// <summary>
        /// Toggle the active status of a scheduled command
        /// </summary>
        [HttpPut("{id}/toggle-active")]
        public async Task<ActionResult<ScheduledCommandDto>> ToggleActive(int id)
        {
            try
            {
                var command = await _scheduledCommandService.GetScheduledCommandByIdAsync(id);
                if (command == null)
                    return NotFound();

                if (!await User.HasServerAccess(_dbContext, command.RconServerId))
                {
                    _logger.LogWarning("[SchedulerController] User {UserId} attempted to toggle command {CommandId} for server {ServerId} without authorization", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, command.RconServerId);
                    return Forbid();
                }

                command.IsActive = !command.IsActive;
                var updated = await _scheduledCommandService.UpdateScheduledCommandAsync(command);

                _logger.LogInformation("[SchedulerController] User {UserId} toggled active status of command {CommandId} to {IsActive}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, id, updated.IsActive);

                return Ok(MapToDto(updated));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchedulerController] Error toggling active status of command {CommandId}", id);
                return StatusCode(500, new { error = "Failed to toggle active status" });
            }
        }

        private string ValidateScheduleType(CreateScheduledCommandDto dto)
        {
            var scheduleType = (ScheduleType)dto.ScheduleTypeValue;

            return scheduleType switch
            {
                ScheduleType.OneTime => dto.ExecuteAt == null ? "ExecuteAt is required for one-time schedules" : null,
                ScheduleType.EveryXMinutes => dto.IntervalMinutes == null || dto.IntervalMinutes <= 0 ? "IntervalMinutes is required and must be greater than 0" : null,
                ScheduleType.Hourly => dto.IntervalHours == null || dto.IntervalHours <= 0 ? "IntervalHours is required and must be greater than 0" : null,
                ScheduleType.Daily => dto.ExecutionHour == null || dto.ExecutionMinute == null ? "ExecutionHour and ExecutionMinute are required for daily schedules" : null,
                ScheduleType.Weekly => string.IsNullOrEmpty(dto.DaysOfWeek) || dto.ExecutionHour == null || dto.ExecutionMinute == null ? "DaysOfWeek, ExecutionHour and ExecutionMinute are required for weekly schedules" : null,
                ScheduleType.Monthly => dto.DayOfMonth == null || dto.ExecutionHour == null || dto.ExecutionMinute == null ? "DayOfMonth, ExecutionHour and ExecutionMinute are required for monthly schedules" : null,
                _ => "Invalid schedule type"
            };
        }

        private ScheduledCommandDto MapToDto(ScheduledCommand command)
        {
            return new ScheduledCommandDto
            {
                Id = command.Id,
                RconServerId = command.RconServerId,
                Command = command.Command,
                Name = command.Name,
                Description = command.Description,
                ScheduleTypeValue = (int)command.ScheduleType,
                IntervalMinutes = command.IntervalMinutes,
                IntervalHours = command.IntervalHours,
                ExecutionHour = command.ExecutionHour,
                ExecutionMinute = command.ExecutionMinute,
                DaysOfWeek = command.DaysOfWeek,
                DayOfMonth = command.DayOfMonth,
                ExecuteAt = command.ExecuteAt,
                IsActive = command.IsActive,
                LastExecutedAt = command.LastExecutedAt,
                NextExecutionAt = command.NextExecutionAt,
                ExecutionCount = command.ExecutionCount,
                LastExecutionSuccess = command.LastExecutionSuccess,
                LastExecutionError = command.LastExecutionError,
                CreatedAt = command.CreatedAt,
                UpdatedAt = command.UpdatedAt
            };
        }
    }
}
