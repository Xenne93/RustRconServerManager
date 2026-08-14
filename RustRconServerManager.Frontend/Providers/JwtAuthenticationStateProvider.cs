using System.Security.Claims;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;

// JwtAuthenticationStateProvider handles Blazor WebAssembly authentication via HttpOnly cookies.
// The JWT is stored in an HttpOnly Secure cookie (set by the backend), so the frontend never
// touches the raw token. Instead, it calls /api/Auth/me to retrieve user claims.
//
// This provider:
// - Calls /api/Auth/me (cookie sent automatically by the browser)
// - Parses the returned claims dictionary into a ClaimsPrincipal
// - Returns an authenticated or anonymous state accordingly

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;

    public JwtAuthenticationStateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            // Cookie is sent automatically — no need to read from localStorage
            var response = await _httpClient.GetAsync("/api/Auth/me");

            if (!response.IsSuccessStatusCode)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claimsDict = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            if (claimsDict == null || claimsDict.Count == 0)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = claimsDict.Select(kvp => new Claim(kvp.Key, kvp.Value)).ToList();
            var identity = new ClaimsIdentity(claims, "jwt");
            var user = new ClaimsPrincipal(identity);

            return new AuthenticationState(user);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthProvider] Error checking authentication: {ex.Message}");
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    /// <summary>
    /// Notifies the framework that the authentication state has changed (e.g., after login or logout).
    /// </summary>
    public void NotifyAuthenticationStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
