using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Manages automatic generation of cryptographic keys for JWT and RCON encryption
/// when keys are missing from configuration
/// </summary>
public class ConfigurationKeyManager
{
    private readonly string _appsettingsPath;
    private readonly ILogger<ConfigurationKeyManager> _logger;

    public ConfigurationKeyManager(ILogger<ConfigurationKeyManager> logger)
    {
        _logger = logger;
        _appsettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
    }

    /// <summary>
    /// Checks if keys are missing (null/empty) and generates new unique keys if needed
    /// </summary>
    public async Task<bool> EnsureUniqueKeysAsync(IConfiguration configuration)
    {
        var currentJwtKey = configuration["Jwt:Key"];
        var currentRconKey = configuration["RconEncryption:Key"];

        bool needsJwtUpdate = false;
        bool needsRconUpdate = false;

        // Check JWT key: generate ONLY if NULL/empty
        if (string.IsNullOrWhiteSpace(currentJwtKey))
        {
            _logger.LogWarning("JWT key is missing or empty. A unique key will be generated.");
            needsJwtUpdate = true;
        }

        // Check RCON key: generate ONLY if NULL/empty
        if (string.IsNullOrWhiteSpace(currentRconKey))
        {
            _logger.LogWarning("RCON encryption key is missing or empty. A unique key will be generated.");
            needsRconUpdate = true;
        }

        // Only generate and save if at least one key needs updating
        if (needsJwtUpdate || needsRconUpdate)
        {
            return await GenerateAndSaveKeysAsync(needsJwtUpdate, needsRconUpdate);
        }

        _logger.LogInformation("Configuration keys are present. No generation needed.");
        return false;
    }

    /// <summary>
    /// Generates cryptographically secure random keys and updates appsettings.json
    /// </summary>
    private async Task<bool> GenerateAndSaveKeysAsync(bool generateJwt, bool generateRcon)
    {
        try
        {
            // Read the current appsettings.json
            if (!File.Exists(_appsettingsPath))
            {
                _logger.LogError("appsettings.json not found at: {Path}", _appsettingsPath);
                return false;
            }

            var jsonContent = await File.ReadAllTextAsync(_appsettingsPath);
            var jsonDocument = JsonDocument.Parse(jsonContent);
            var rootElement = jsonDocument.RootElement;

            // Parse to a dictionary for easier manipulation
            var config = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
            if (config == null)
            {
                _logger.LogError("Failed to parse appsettings.json");
                return false;
            }

            // Generate new keys if needed
            if (generateJwt)
            {
                var newJwtKey = GenerateSecureKey(32);
                _logger.LogInformation("Generated new JWT key (length: {Length})", newJwtKey.Length);

                if (config.ContainsKey("Jwt"))
                {
                    var jwtSection = JsonSerializer.Deserialize<Dictionary<string, object>>(config["Jwt"].ToString()!);
                    if (jwtSection != null)
                    {
                        jwtSection["Key"] = newJwtKey;
                        config["Jwt"] = jwtSection;
                    }
                }
                else
                {
                    config["Jwt"] = new Dictionary<string, object>
                    {
                        ["Key"] = newJwtKey,
                        ["Issuer"] = "api.RustRconServerManager.com",
                        ["Audience"] = "app.RustRconServerManager.com"
                    };
                }
            }

            if (generateRcon)
            {
                var newRconKey = GenerateSecureKey(32);
                _logger.LogInformation("Generated new RCON encryption key (length: {Length})", newRconKey.Length);

                if (config.ContainsKey("RconEncryption"))
                {
                    var rconSection = JsonSerializer.Deserialize<Dictionary<string, object>>(config["RconEncryption"].ToString()!);
                    if (rconSection != null)
                    {
                        rconSection["Key"] = newRconKey;
                        config["RconEncryption"] = rconSection;
                    }
                }
                else
                {
                    config["RconEncryption"] = new Dictionary<string, object>
                    {
                        ["Key"] = newRconKey
                    };
                }
            }

            // Write the updated configuration back to appsettings.json
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var updatedJson = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(_appsettingsPath, updatedJson);

            _logger.LogWarning("appsettings.json has been updated with new cryptographic keys. Please restart the application for changes to take effect.");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate and save configuration keys");
            return false;
        }
    }

    /// <summary>
    /// Generates a cryptographically secure random key
    /// </summary>
    /// <param name="length">Length of the key in characters</param>
    /// <returns>Base64-encoded random key</returns>
    private static string GenerateSecureKey(int length)
    {
        // Generate random bytes
        var bytes = RandomNumberGenerator.GetBytes(length);

        // Convert to base64 and make it URL-safe
        var base64 = Convert.ToBase64String(bytes);

        // Make it alphanumeric + some special chars (remove padding and replace chars)
        var key = base64
            .Replace("+", "x")
            .Replace("/", "y")
            .Replace("=", "");

        // Ensure we have exactly the requested length
        return key.Length > length ? key.Substring(0, length) : key;
    }
}
