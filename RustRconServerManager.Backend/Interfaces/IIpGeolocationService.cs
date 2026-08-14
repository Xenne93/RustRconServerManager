namespace RustRconServerManager.Backend.Interfaces;

/// <summary>
/// Interface for IP geolocation services to determine player country from IP address
/// </summary>
public interface IIpGeolocationService
{
    /// <summary>
    /// Gets the country code for an IP address
    /// </summary>
    /// <param name="ipAddress">The IP address to look up</param>
    /// <returns>Country code (e.g., "US", "GB") or null if not found</returns>
    Task<string?> GetCountryFromIpAsync(string ipAddress);
}
