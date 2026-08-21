using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.AiIntegration;

namespace RustRconServerManager.Backend.Services;

public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRconPasswordsCryptoService _cryptoService;
    private readonly ILogger<AiService> _logger;

    private const string DefaultOpenAiBaseUrl = "https://api.openai.com";
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    private const string AnthropicVersion = "2023-06-01";

    public AiService(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        IRconPasswordsCryptoService cryptoService,
        ILogger<AiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // AI responses can take a while, especially local models
    }

    public async Task<AiIntegrationSettings?> GetSettingsAsync(int systemProfileId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AiIntegrationSettings
            .FirstOrDefaultAsync(s => s.SystemProfileId == systemProfileId);
    }

    public async Task<TestAiConnectionResultDto> TestConnectionAsync(int systemProfileId)
    {
        var settings = await GetSettingsAsync(systemProfileId);

        if (settings == null)
        {
            return new TestAiConnectionResultDto { Success = false, Message = "No AI provider has been configured yet." };
        }

        // Deliberately does NOT check settings.IsEnabled - testing the connection is an
        // explicit, admin-initiated action, not an automated AI feature firing on its own,
        // so it should work even while AI integration is still switched off (e.g. verifying
        // credentials before turning the feature on for real).
        var messages = new List<AiChatMessage>
        {
            new() { Role = "user", Content = "Reply with only the single word: OK" }
        };

        var result = await SendChatInternalAsync(systemProfileId, settings, messages, CancellationToken.None);

        if (!result.Success)
        {
            return new TestAiConnectionResultDto { Success = false, Message = result.ErrorMessage ?? "Connection test failed." };
        }

        var preview = (result.Content ?? string.Empty).Trim();
        if (preview.Length > 200)
        {
            preview = preview.Substring(0, 200) + "...";
        }

        return new TestAiConnectionResultDto { Success = true, Message = $"Connected successfully. Response: \"{preview}\"" };
    }

    public async Task<AiChatResult> SendChatAsync(int systemProfileId, List<AiChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(systemProfileId);

        if (settings == null || !settings.IsEnabled)
        {
            return new AiChatResult { Success = false, ErrorMessage = "AI integration is not configured or is disabled for this system profile." };
        }

        return await SendChatInternalAsync(systemProfileId, settings, messages, cancellationToken);
    }

    private async Task<AiChatResult> SendChatInternalAsync(int systemProfileId, AiIntegrationSettings settings, List<AiChatMessage> messages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            return new AiChatResult { Success = false, ErrorMessage = "No AI model configured." };
        }

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(settings.EncryptedApiKey))
        {
            try
            {
                apiKey = _cryptoService.Decrypt(settings.EncryptedApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AiService] Failed to decrypt API key for SystemProfile {SystemProfileId}", systemProfileId);
                return new AiChatResult { Success = false, ErrorMessage = "Stored API key could not be decrypted." };
            }
        }

        try
        {
            return settings.Provider switch
            {
                AiProviders.Anthropic => await SendAnthropicAsync(settings, apiKey, messages, cancellationToken),
                AiProviders.Ollama => await SendOllamaAsync(settings, apiKey, messages, cancellationToken),
                AiProviders.LmStudio or AiProviders.OpenAI or AiProviders.Universal
                    => await SendOpenAiCompatibleAsync(settings, apiKey, messages, cancellationToken),
                _ => new AiChatResult { Success = false, ErrorMessage = $"Unknown AI provider '{settings.Provider}'." }
            };
        }
        catch (TaskCanceledException)
        {
            return new AiChatResult { Success = false, ErrorMessage = "Request to the AI provider timed out." };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[AiService] HTTP error calling {Provider} for SystemProfile {SystemProfileId}", settings.Provider, systemProfileId);
            return new AiChatResult { Success = false, ErrorMessage = $"Could not reach the AI provider: {ex.Message}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiService] Unexpected error calling {Provider} for SystemProfile {SystemProfileId}", settings.Provider, systemProfileId);
            return new AiChatResult { Success = false, ErrorMessage = "Unexpected error while contacting the AI provider." };
        }
    }

    /// <summary>
    /// OpenAI, LM Studio, and a generic "Universal" OpenAI-compatible endpoint all speak the
    /// same request/response shape (POST {base}/v1/chat/completions), so one implementation
    /// covers all three.
    /// </summary>
    private async Task<AiChatResult> SendOpenAiCompatibleAsync(AiIntegrationSettings settings, string? apiKey, List<AiChatMessage> messages, CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl(settings, settings.Provider == AiProviders.OpenAI ? DefaultOpenAiBaseUrl : null);
        if (baseUrl == null)
        {
            return new AiChatResult { Success = false, ErrorMessage = "No base URL configured for this provider." };
        }

        var requestBody = new
        {
            model = settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = 1024
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new AiChatResult { Success = false, ErrorMessage = ExtractErrorMessage(body, response.StatusCode) };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return new AiChatResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiService] Unexpected response shape from OpenAI-compatible provider");
            return new AiChatResult { Success = false, ErrorMessage = "Received an unexpected response from the AI provider." };
        }
    }

    private async Task<AiChatResult> SendAnthropicAsync(AiIntegrationSettings settings, string? apiKey, List<AiChatMessage> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiChatResult { Success = false, ErrorMessage = "An API key is required for Anthropic." };
        }

        var baseUrl = ResolveBaseUrl(settings, DefaultAnthropicBaseUrl)!;

        // Anthropic wants the system prompt as its own top-level field, not in the messages array.
        var systemPrompt = string.Join("\n\n", messages.Where(m => m.Role == "system").Select(m => m.Content));
        var conversationMessages = messages
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToList();

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["max_tokens"] = 1024,
            ["messages"] = conversationMessages
        };
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            requestBody["system"] = systemPrompt;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new AiChatResult { Success = false, ErrorMessage = ExtractErrorMessage(body, response.StatusCode) };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            return new AiChatResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiService] Unexpected response shape from Anthropic");
            return new AiChatResult { Success = false, ErrorMessage = "Received an unexpected response from the AI provider." };
        }
    }

    private async Task<AiChatResult> SendOllamaAsync(AiIntegrationSettings settings, string? apiKey, List<AiChatMessage> messages, CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl(settings, null);
        if (baseUrl == null)
        {
            return new AiChatResult { Success = false, ErrorMessage = "No base URL configured for Ollama." };
        }

        var requestBody = new
        {
            model = settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        // A plain local Ollama install has no auth, but some setups sit behind a
        // reverse proxy that does - send it if one was configured.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new AiChatResult { Success = false, ErrorMessage = ExtractErrorMessage(body, response.StatusCode) };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return new AiChatResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiService] Unexpected response shape from Ollama");
            return new AiChatResult { Success = false, ErrorMessage = "Received an unexpected response from Ollama. Is the model name correct?" };
        }
    }

    private static string? ResolveBaseUrl(AiIntegrationSettings settings, string? fallback)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? fallback : settings.BaseUrl.Trim();
        return baseUrl?.TrimEnd('/');
    }

    private static string ExtractErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                if (errorEl.ValueKind == JsonValueKind.Object && errorEl.TryGetProperty("message", out var msgEl))
                {
                    return $"{(int)statusCode}: {msgEl.GetString()}";
                }
                if (errorEl.ValueKind == JsonValueKind.String)
                {
                    return $"{(int)statusCode}: {errorEl.GetString()}";
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message below.
        }

        return $"AI provider returned {(int)statusCode}.";
    }
}
