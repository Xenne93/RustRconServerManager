using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Shared.AuditLog;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(AppDbContext dbContext, ILogger<AuditLogsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Paginated, filterable audit log listing - admin only, so moderators can't review
    /// (or hide) their own or each other's activity.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AuditLogPagedResultDto>> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? serverId = null,
        [FromQuery] string? role = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var serverIds = await _dbContext.RconServers
                .Where(s => s.SystemProfileId == currentUser.SystemProfileId)
                .Select(s => s.Id)
                .ToListAsync();

            var query = _dbContext.AuditLogs
                .Where(a => a.ServerId == null || serverIds.Contains(a.ServerId.Value));

            if (serverId.HasValue)
                query = query.Where(a => a.ServerId == serverId.Value);

            if (!string.IsNullOrWhiteSpace(role))
                query = query.Where(a => a.Role == role);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(a =>
                    a.UserEmail.Contains(search) ||
                    a.Action.Contains(search) ||
                    (a.Details != null && a.Details.Contains(search)));

            if (from.HasValue)
                query = query.Where(a => a.CreatedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(a => a.CreatedAt <= to.Value);

            var totalCount = await query.CountAsync();

            var serverNames = await _dbContext.RconServers
                .Where(s => serverIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);

            var entries = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogDto
                {
                    Id = a.Id,
                    UserEmail = a.UserEmail,
                    Role = a.Role,
                    ServerId = a.ServerId,
                    Action = a.Action,
                    Details = a.Details,
                    IpAddress = a.IpAddress,
                    StatusCode = a.StatusCode,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            foreach (var entry in entries)
            {
                if (entry.ServerId.HasValue && serverNames.TryGetValue(entry.ServerId.Value, out var name))
                    entry.ServerName = name;
            }

            return Ok(new AuditLogPagedResultDto
            {
                Entries = entries,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuditLogsController] Error retrieving audit logs");
            return StatusCode(500, new { error = "Error retrieving audit logs" });
        }
    }
}
