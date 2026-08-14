public interface ITokenProvider
{
    Task<string> GetTokenAsync();
}

/// <summary>
/// Token provider that returns an empty string since authentication is now handled
/// via HttpOnly cookies. This is kept for backward compatibility with any code
/// that still references ITokenProvider.
/// </summary>
public class CookieTokenProvider : ITokenProvider
{
    public Task<string> GetTokenAsync()
    {
        // Token is in an HttpOnly cookie — not accessible from client-side code.
        // The browser sends it automatically with requests.
        return Task.FromResult(string.Empty);
    }
}
