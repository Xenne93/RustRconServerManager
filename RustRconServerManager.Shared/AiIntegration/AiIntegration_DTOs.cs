namespace RustRconServerManager.Shared.AiIntegration;

/// <summary>
/// The set of AI providers the panel knows how to talk to. Kept as plain strings (not a
/// C# enum) so they round-trip through the DB/DTOs the same way other provider-style
/// fields do elsewhere in this codebase (e.g. RconServer.ModFramework).
/// </summary>
public static class AiProviders
{
    public const string Ollama = "Ollama";
    public const string LmStudio = "LmStudio";
    public const string OpenAI = "OpenAI";
    public const string Anthropic = "Anthropic";
    public const string Universal = "Universal";

    public static readonly string[] All = { Ollama, LmStudio, OpenAI, Anthropic, Universal };
}

public class AiIntegrationSettingsDto
{
    public string Provider { get; set; } = AiProviders.OpenAI;
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public bool IsEnabled { get; set; }
    public bool ApiKeyConfigured { get; set; }
}

public class SetAiIntegrationSettingsDto
{
    public string Provider { get; set; } = AiProviders.OpenAI;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// New API key to store. Empty/omitted leaves the current key unchanged unless RemoveApiKey is set.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// When true, clears the stored key regardless of ApiKey.
    /// </summary>
    public bool RemoveApiKey { get; set; } = false;

    public string? Model { get; set; }
    public bool IsEnabled { get; set; }
}

public class TestAiConnectionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
