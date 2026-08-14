namespace RustRconServerManager.Backend.Exceptions;

/// <summary>
/// Thrown when a user cannot be resolved from the current request context.
/// Caught by the global exception filter and converted to a 401 Unauthorized response.
/// </summary>
public class UserNotAuthenticatedException : Exception
{
    public UserNotAuthenticatedException()
        : base("User could not be resolved from the current session.") { }

    public UserNotAuthenticatedException(string message)
        : base(message) { }
}
