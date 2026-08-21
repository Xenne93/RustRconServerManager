using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.AiIntegration;

namespace RustRconServerManager.Backend.Interfaces;

/// <summary>
/// A single message in an AI chat request. Provider clients translate this into whatever
/// shape that provider's API actually expects (OpenAI/LM Studio/Ollama/Universal all use an
/// OpenAI-style {role, content} messages array already; Anthropic separates the system
/// prompt out, which AiService handles internally).
/// </summary>
public class AiChatMessage
{
    public string Role { get; set; } = "user"; // "system" | "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}

public class AiChatResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Talks to whichever AI provider a SystemProfile has configured. This is the framework
/// other AI-powered features (server monitoring, script/mod repair suggestions, rule-breaker
/// detection, admin notifications, etc.) are meant to build on - none of those exist yet,
/// this just makes "send this profile's configured AI provider a chat request" possible.
/// </summary>
public interface IAiService
{
    /// <summary>
    /// Loads the raw settings row for a profile, or null if none has been saved yet.
    /// </summary>
    Task<AiIntegrationSettings?> GetSettingsAsync(int systemProfileId);

    /// <summary>
    /// Sends a minimal request to the configured provider to confirm the endpoint, model,
    /// and credentials actually work - used by the "Test Connection" button.
    /// </summary>
    Task<TestAiConnectionResultDto> TestConnectionAsync(int systemProfileId);

    /// <summary>
    /// Sends a chat request to the profile's configured provider. Returns
    /// Success = false (with ErrorMessage set) if AI isn't configured/enabled for this
    /// profile, or if the provider call fails - callers should treat that as "AI isn't
    /// available right now" rather than throwing.
    /// </summary>
    Task<AiChatResult> SendChatAsync(int systemProfileId, List<AiChatMessage> messages, CancellationToken cancellationToken = default);
}
