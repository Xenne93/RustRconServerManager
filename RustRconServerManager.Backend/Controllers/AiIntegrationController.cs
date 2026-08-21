using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.AiIntegration;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AiIntegrationController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AiIntegrationController> _logger;
    private readonly IRconPasswordsCryptoService _cryptoService;
    private readonly IAiService _aiService;

    public AiIntegrationController(
        AppDbContext dbContext,
        ILogger<AiIntegrationController> logger,
        IRconPasswordsCryptoService cryptoService,
        IAiService aiService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _cryptoService = cryptoService;
        _aiService = aiService;
    }

    /// <summary>
    /// Gets the current AI integration settings for the caller's SystemProfile. Admin-only,
    /// same as the rest of Panel Settings' provider-credential endpoints.
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var settings = await _dbContext.AiIntegrationSettings
                .FirstOrDefaultAsync(s => s.SystemProfileId == currentUser.SystemProfileId);

            return Ok(MapToDto(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiIntegrationController] Error getting AI integration settings");
            return StatusCode(500, new { error = "Error retrieving AI integration settings" });
        }
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SetSettings([FromBody] SetAiIntegrationSettingsDto dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            if (!AiProviders.All.Contains(dto.Provider))
                return BadRequest(new { error = $"Unknown provider '{dto.Provider}'." });

            var requiresBaseUrl = dto.Provider is AiProviders.Ollama or AiProviders.LmStudio or AiProviders.Universal;
            if (requiresBaseUrl && string.IsNullOrWhiteSpace(dto.BaseUrl))
                return BadRequest(new { error = "Base URL is required for this provider." });

            if (!string.IsNullOrWhiteSpace(dto.BaseUrl) && !Uri.TryCreate(dto.BaseUrl.Trim(), UriKind.Absolute, out _))
                return BadRequest(new { error = "Base URL is not a valid URL." });

            if (string.IsNullOrWhiteSpace(dto.Model))
                return BadRequest(new { error = "A model name is required." });

            var settings = await _dbContext.AiIntegrationSettings
                .FirstOrDefaultAsync(s => s.SystemProfileId == currentUser.SystemProfileId);

            if (settings == null)
            {
                settings = new AiIntegrationSettings
                {
                    SystemProfileId = currentUser.SystemProfileId,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.AiIntegrationSettings.Add(settings);
            }

            settings.Provider = dto.Provider;
            settings.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim();
            settings.Model = dto.Model.Trim();
            settings.IsEnabled = dto.IsEnabled;
            settings.UpdatedAt = DateTime.UtcNow;

            if (dto.RemoveApiKey)
            {
                settings.EncryptedApiKey = null;
            }
            else if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            {
                settings.EncryptedApiKey = _cryptoService.Encrypt(dto.ApiKey.Trim());
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[AiIntegrationController] User {UserId} updated AI integration settings (provider {Provider})",
                currentUser.Id, settings.Provider);

            return Ok(MapToDto(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiIntegrationController] Error saving AI integration settings");
            return StatusCode(500, new { error = "Error saving AI integration settings" });
        }
    }

    /// <summary>
    /// Sends a minimal test prompt to the currently-saved provider config to confirm the
    /// endpoint, model, and credentials actually work.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);
            if (!currentUser.isAdmin)
                return Forbid();

            var result = await _aiService.TestConnectionAsync(currentUser.SystemProfileId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiIntegrationController] Error testing AI connection");
            return StatusCode(500, new { error = "Error testing AI connection" });
        }
    }

    private static AiIntegrationSettingsDto MapToDto(AiIntegrationSettings? settings)
    {
        if (settings == null)
        {
            return new AiIntegrationSettingsDto();
        }

        return new AiIntegrationSettingsDto
        {
            Provider = settings.Provider,
            BaseUrl = settings.BaseUrl,
            Model = settings.Model,
            IsEnabled = settings.IsEnabled,
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.EncryptedApiKey)
        };
    }
}
