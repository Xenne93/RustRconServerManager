using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RustRconServerManager.Backend.Exceptions;

namespace RustRconServerManager.Backend.Filters;

/// <summary>
/// Global exception filter that catches UserNotAuthenticatedException
/// and returns a 401 Unauthorized response instead of a 500.
/// </summary>
public class UserNotAuthenticatedExceptionFilter : IExceptionFilter
{
    private readonly ILogger<UserNotAuthenticatedExceptionFilter> _logger;

    public UserNotAuthenticatedExceptionFilter(ILogger<UserNotAuthenticatedExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is UserNotAuthenticatedException ex)
        {
            _logger.LogWarning("User session expired or invalid: {Message}", ex.Message);

            context.Result = new UnauthorizedObjectResult(new { message = "Session expired. Please log in again." });
            context.ExceptionHandled = true;
        }
    }
}
