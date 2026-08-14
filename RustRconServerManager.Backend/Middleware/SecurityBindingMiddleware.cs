using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;

namespace RustRconServerManager.Backend.Middleware;


// This middleware checks if the user's IP and User-Agent match the ones stored in the JWT claims

public class SecurityBindingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityBindingMiddleware> _logger;

    public SecurityBindingMiddleware(RequestDelegate next, ILogger<SecurityBindingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var _dbContext = context.RequestServices.GetRequiredService<AppDbContext>();
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var tokenIp = user.FindFirst("ip")?.Value;
            var tokenUa = user.FindFirst("ua")?.Value;

            var currentIp = context.Connection.RemoteIpAddress?.ToString();
            var currentUa = context.Request.Headers["User-Agent"].ToString();

            if (!string.IsNullOrEmpty(tokenIp) && tokenIp != currentIp)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: IP mismatch.");
                return;
            }

            if (!string.IsNullOrEmpty(tokenUa) && tokenUa != currentUa)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: User-Agent mismatch.");
                return;
            }

            var userEmail = user.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
            var tokenHash = user.FindFirst(System.Security.Claims.ClaimTypes.Hash)?.Value;

            if (string.IsNullOrEmpty(userEmail) || string.IsNullOrEmpty(tokenHash))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: Missing claims.");
                return;
            }

            var dbUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            if (dbUser == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: User not found.");
                return;
            }

            // Check if session exists in UserSessions table (new multi-session support)
            // Look for an active, non-revoked session with this hash
            var userSession = await _dbContext.UserSessions
                .FirstOrDefaultAsync(s => s.UserId == dbUser.Id &&
                                        s.SessionHash == tokenHash &&
                                        !s.IsRevoked &&
                                        s.ExpiresAt > DateTime.UtcNow);

            if (userSession != null)
            {
                // Update last activity time
                userSession.LastActivityAt = DateTime.UtcNow;
                _dbContext.UserSessions.Update(userSession);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                // Fallback to legacy single-session mode for backwards compatibility
                if (dbUser.SessionHash != tokenHash)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized: Invalid session.");
                    return;
                }
            }
        }

        await _next(context);
    }
}
