namespace RustRconServerManager.Frontend.Utilities;

/// <summary>
/// Helper class to provide flag image URLs for country codes
/// Uses flagcdn.com CDN for reliable flag image delivery
/// </summary>
public static class CountryFlagHelper
{
    private const string FlagCdnBaseUrl = "https://flagcdn.com/w40";  // 40px width flags

    /// <summary>
    /// Gets the flag image URL for a country code from CDN
    /// Example: "US" -> "https://flagcdn.com/w40/us.png"
    /// </summary>
    /// <param name="countryCode">Two-letter ISO country code (e.g., "US", "GB")</param>
    /// <returns>Full URL to flag image or empty string if invalid</returns>
    public static string GetFlagImageUrl(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            return string.Empty;

        // Convert to lowercase for the URL
        string code = countryCode.ToLower();

        // Validate it's only letters
        if (!char.IsLetter(code[0]) || !char.IsLetter(code[1]))
            return string.Empty;

        return $"{FlagCdnBaseUrl}/{code}.png";
    }

    /// <summary>
    /// Gets formatted HTML with flag image and country code
    /// </summary>
    /// <param name="countryCode">Two-letter ISO country code</param>
    /// <returns>HTML img tag with flag and country code, or just the code</returns>
    public static string GetFlagWithCode(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return "-";

        var imageUrl = GetFlagImageUrl(countryCode);
        if (string.IsNullOrEmpty(imageUrl))
            return countryCode;

        return $"<img src=\"{imageUrl}\" alt=\"{countryCode}\" title=\"{countryCode}\" style=\"width: 30px; height: 20px; margin-right: 5px; border-radius: 2px;\" />";
    }

    /// <summary>
    /// Gets just the flag image URL
    /// </summary>
    /// <param name="countryCode">Two-letter ISO country code</param>
    /// <returns>Flag image URL or empty string</returns>
    public static string GetFlagImageUrlOnly(string countryCode)
    {
        return GetFlagImageUrl(countryCode);
    }
}
