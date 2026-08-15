using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Exceptions;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Services;

namespace RustRconServerManager.Backend.Filters;

/// <summary>
/// Global action filter that records every mutating (POST/PUT/DELETE/PATCH) controller action
/// taken by an authenticated admin or moderator into the audit log. GET requests are skipped
/// entirely - they're reads, not actions. Never logs full request bodies (only simple/primitive
/// route or body arguments, and never anything whose parameter name looks like a secret), so
/// there's no risk of persisting RCON passwords, API keys, etc. into the audit trail.
///
/// Console/preset commands don't go through this filter at all - they run over SignalR
/// (LiveConsoleHub), which MVC action filters never see, so that path logs itself directly via
/// IAuditLogService.
/// </summary>
public class AuditLogActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };
    private static readonly string[] SensitiveKeywords = { "password", "key", "secret", "token" };

    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AuditLogActionFilter> _logger;

    public AuditLogActionFilter(IAuditLogService auditLogService, ILogger<AuditLogActionFilter> logger)
    {
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!MutatingMethods.Contains(httpContext.Request.Method))
        {
            await next();
            return;
        }

        var executed = await next();

        try
        {
            // Not authenticated (e.g. login/setup) - nothing to attribute this action to.
            var dbContext = httpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = await httpContext.User.GetUser(dbContext);

            var controller = context.ActionDescriptor.RouteValues.TryGetValue("controller", out var c) ? c : "Unknown";
            var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var a) ? a : "Unknown";

            var serverId = ResolveServerId(context.ActionArguments, user.SelectedServerId);
            var details = BuildDetails(context.ActionArguments);
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var statusCode = (executed.Result as IStatusCodeActionResult)?.StatusCode;

            await _auditLogService.LogAsync(user, serverId, $"{httpContext.Request.Method} {controller}/{action}", details, ipAddress, statusCode);
        }
        catch (UserNotAuthenticatedException)
        {
            // Expected for pre-auth endpoints - nothing to log.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuditLogActionFilter] Failed to record audit log entry");
        }
    }

    private static int? ResolveServerId(IDictionary<string, object?> arguments, int? selectedServerId)
    {
        foreach (var key in new[] { "serverId", "rconServerId" })
        {
            if (arguments.TryGetValue(key, out var value) && value is int intValue && intValue > 0)
                return intValue;
        }

        foreach (var value in arguments.Values)
        {
            if (value is null || IsSimpleValue(value)) continue;

            var type = value.GetType();
            var prop = type.GetProperty("ServerId") ?? type.GetProperty("RconServerId");
            if (prop?.GetValue(value) is int nested && nested > 0)
                return nested;
        }

        return selectedServerId is > 0 ? selectedServerId : null;
    }

    private static string? BuildDetails(IDictionary<string, object?> arguments)
    {
        var parts = new List<string>();

        foreach (var (key, value) in arguments)
        {
            if (value is null) continue;
            if (SensitiveKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
            if (!IsSimpleValue(value)) continue;

            parts.Add($"{key}={value}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static bool IsSimpleValue(object value) =>
        value is int or long or bool or double or float or decimal or string or Guid or DateTime or Enum;
}
