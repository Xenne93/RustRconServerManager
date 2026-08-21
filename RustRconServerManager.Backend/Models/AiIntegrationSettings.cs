namespace RustRconServerManager.Backend.Models;

/// <summary>
/// AI provider configuration for a SystemProfile. One row per profile, created on first
/// save from the AI Integration page. Holds the connection details the AiService needs to
/// talk to whichever provider is configured - the actual AI-powered features (monitoring,
/// rule-breaker detection, etc.) are built on top of this and don't live here.
/// </summary>
public class AiIntegrationSettings
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to SystemProfile
    /// </summary>
    public int SystemProfileId { get; set; }

    /// <summary>
    /// Navigation property to SystemProfile
    /// </summary>
    public SystemProfile SystemProfile { get; set; } = null!;

    /// <summary>
    /// Which provider to talk to - "Ollama", "LmStudio", "OpenAI", "Anthropic", or
    /// "Universal" (a generic OpenAI-compatible endpoint, for providers/proxies not
    /// explicitly listed). Validated against AiProviders.All at the controller layer
    /// rather than modeled as a DB enum, matching how ModFramework etc. are stored elsewhere
    /// in this codebase.
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// Base URL of the provider's API. Required for Ollama/LmStudio/Universal (self-hosted,
    /// no sane default); optional override for OpenAI/Anthropic, which fall back to their
    /// public API endpoints when left blank.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// API key, encrypted at rest via IRconPasswordsCryptoService (same scheme as
    /// PanelSettings.SteamApiKeyEncrypted - the crypto service isn't actually RCON-specific,
    /// just named after its original use). Not required for a typical local Ollama/LM
    /// Studio setup with no auth in front of it.
    /// </summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>
    /// Model identifier to request, e.g. "gpt-4o", "claude-sonnet-4-5-20250929", "llama3.1".
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Master on/off switch. AI-powered features should check this (not just "is a provider
    /// configured") before calling out, so an admin can pause AI usage without clearing
    /// their saved configuration.
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
