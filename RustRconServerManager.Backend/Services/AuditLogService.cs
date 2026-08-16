using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.Services;

public interface IAuditLogService
{
    Task LogAsync(ApplicationUser user, int? serverId, string action, string? details, string ipAddress, int? statusCode = null);
}

/// <summary>
/// Writes AuditLog rows. Shared between AuditLogActionFilter (generic controller actions)
/// and LiveConsoleHub (console/preset commands, which run over SignalR and never pass
/// through MVC action filters at all) so both paths label the user's role the same way.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(AppDbContext dbContext, ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(ApplicationUser user, int? serverId, string action, string? details, string ipAddress, int? statusCode = null)
    {
        try
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id,
                UserEmail = user.Email ?? string.Empty,
                Role = user.isAdmin ? "Admin" : (user.IsModerator ? "Moderator" : "User"),
                ServerId = serverId,
                Action = action,
                Details = details,
                IpAddress = ipAddress,
                StatusCode = statusCode,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit logging must never break the actual action it's recording.
            _logger.LogError(ex, "[AuditLogService] Failed to write audit log entry for action {Action}", action.Replace("\r", "").Replace("\n", ""));
        }
    }
}
