using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Shared.Authorization;
using RustRconServerManager.Shared.Setup;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Helpers;

[ApiController]
[Route("api/[controller]")]
public class
    AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserManager<ApplicationUser> userManager,
                          SignInManager<ApplicationUser> signInManager,
                          IConfiguration configuration, AppDbContext dbContext,
                          IEmailService emailService,
                          ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet("setup/status")]
    public async Task<IActionResult> GetSetupStatus()
    {
        // Check if any admin users exist
        var hasAdminUsers = await _dbContext.Users.AnyAsync(u => u.isAdmin);

        var isSetupRequired = !hasAdminUsers;

        return Ok(new Authorization_SetupStatusDTO
        {
            IsSetupRequired = isSetupRequired,
            Message = isSetupRequired
                ? "Initial setup required. Create the first admin account to get started."
                : "Setup already completed."
        });
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(Authorization_SetupRequestDTO model)
    {
        // Check if any admin users already exist
        var hasAdminUsers = await _dbContext.Users.AnyAsync(u => u.isAdmin);
        if (hasAdminUsers)
        {
            return BadRequest(new { message = "Setup has already been completed. Admin users exist." });
        }

        // Validate passwords match
        if (model.Password != model.ConfirmPassword)
        {
            return BadRequest(new { message = "Passwords do not match" });
        }

        if (string.IsNullOrWhiteSpace(model.DisplayName))
        {
            return BadRequest(new { message = "Display name is required" });
        }

        // Get or create default SystemProfile
        var systemProfile = await _dbContext.SystemProfiles.FirstOrDefaultAsync();
        if (systemProfile == null)
        {
            systemProfile = new SystemProfile
            {
                Name = "Default Profile",
                Description = "System profile created during initial setup",
                Secret = Generate256BitHash(),
                Hash = Generate256BitHash(),
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.SystemProfiles.Add(systemProfile);
            await _dbContext.SaveChangesAsync();
        }

        var panelSettings = await _dbContext.PanelSettings
            .FirstOrDefaultAsync(ps => ps.SystemProfileId == systemProfile.Id);
        if (panelSettings == null)
        {
            _dbContext.PanelSettings.Add(new PanelSettings
            {
                SystemProfileId = systemProfile.Id,
                AnalyticsEnabled = model.EnableAnonymousAnalytics,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }

        // Create the admin user
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            DisplayName = model.DisplayName.Trim(),
            SystemProfileId = systemProfile.Id,
            isAdmin = true,
            EmailConfirmed = true, // Auto-confirm email for initial admin
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = "Failed to create admin user", errors = result.Errors });
        }

        // Log the user in automatically
        var sessionHash = Generate256BitHash();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers["User-Agent"].ToString() ?? "unknown";
        var deviceName = ExtractDeviceName(userAgent);

        var userSession = new UserSession
        {
            UserId = user.Id,
            SessionHash = sessionHash,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceName = deviceName,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(3),
            IsRevoked = false
        };

        _dbContext.UserSessions.Add(userSession);

        ApplicationUser appUser = await _dbContext.Users.SingleAsync(u => u.Id == user.Id);
        appUser.SessionHash = sessionHash;
        appUser.LastLoginAt = DateTime.UtcNow;
        _dbContext.Users.Update(appUser);

        // Link the consent record (submitted earlier in the wizard) to the admin account just created
        if (model.ConsentId.HasValue)
        {
            var legalConsent = await _dbContext.LegalConsents.FindAsync(model.ConsentId.Value);
            if (legalConsent != null)
            {
                legalConsent.UserId = user.Id;
                _dbContext.LegalConsents.Update(legalConsent);
            }
        }

        await _dbContext.SaveChangesAsync();

        var token = GenerateJwtToken(user, sessionHash);

        // Set HttpOnly cookie with JWT
        SetAuthCookie(token);

        return Ok(new Authorization_LoginResponseDTO
        {
            Token = null,
            RequiresTwoFactor = false,
            Message = "Setup completed successfully. Admin account created and logged in."
        });
    }

    [HttpPost("consent")]
    public async Task<IActionResult> SubmitLegalConsent(LegalConsentSubmitDTO model)
    {
        try
        {
            // Get IP address of the user
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Create legal consent record
            var legalConsent = new LegalConsent
            {
                IpAddress = ipAddress,
                AcceptedAt = DateTime.UtcNow,
                ConsentAnonymousMetrics = model.ConsentAnonymousMetrics,
                AcceptedPrivacyPolicy = model.AcceptedPrivacyPolicy,
                AcceptedTermsAndConditions = model.AcceptedTermsAndConditions,
                TermsVersion = "1.0",
                PrivacyVersion = "1.0",
                UserId = null // Will be set later after user is created in setup
            };

            _dbContext.LegalConsents.Add(legalConsent);
            await _dbContext.SaveChangesAsync();

            return Ok(new {
                message = "Legal consent recorded successfully",
                consentId = legalConsent.Id
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to record legal consent", error = ApiErrorHelper.FormatError(ex) });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(Authorization_UserRegisterDTO model)
    {
        // SECURITY: Public registration is disabled
        // User accounts can only be created by admins through the Moderator management system
        // or by direct database setup for initial admin accounts
        return StatusCode(403, new { message = "Public registration is disabled. Contact your administrator." });
    }
    
    

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // Get the token hash from claims
        var tokenHash = User.FindFirst(System.Security.Claims.ClaimTypes.Hash)?.Value;
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(tokenHash) || string.IsNullOrEmpty(userEmail))
            return BadRequest("Missing token information");

        var user = await _userManager.FindByEmailAsync(userEmail);
        if (user == null)
            return NotFound("User not found");

        // Revoke the current session
        var userSession = await _dbContext.UserSessions
            .FirstOrDefaultAsync(s => s.UserId == user.Id && s.SessionHash == tokenHash);

        if (userSession != null)
        {
            userSession.IsRevoked = true;
            _dbContext.UserSessions.Update(userSession);
            await _dbContext.SaveChangesAsync();
        }

        // Clear the auth cookie
        ClearAuthCookie();

        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        // Logout from all devices/browsers
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(userEmail))
            return BadRequest("Missing user information");

        var user = await _userManager.FindByEmailAsync(userEmail);
        if (user == null)
            return NotFound("User not found");

        // Revoke all sessions for this user
        var userSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsRevoked = true;
        }

        _dbContext.UserSessions.UpdateRange(userSessions);
        await _dbContext.SaveChangesAsync();

        // Clear the auth cookie
        ClearAuthCookie();

        return Ok(new { message = "Logged out from all devices successfully" });
    }

    [HttpGet("verify")]
    public IActionResult Verify()
    {
        // Try Authorization header first, then fall back to cookie
        string? token = null;

        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            token = authHeader.Substring("Bearer ".Length);
        }
        else
        {
            token = Request.Cookies["rrsm_auth"];
        }

        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));

        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(10) // Allow 10 second clock skew for client-server time differences
            }, out SecurityToken validatedToken);

            return Ok();
        }
        catch
        {
            return Unauthorized("Invalid token");
        }
    }

    /// <summary>
    /// Returns the current user's claims from the HttpOnly cookie.
    /// Used by Blazor WASM to determine authentication state.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var claims = User.Claims.ToDictionary(c => c.Type, c => c.Value);
        return Ok(claims);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(Authorization_UserLoginDTO model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
            return Unauthorized(new Authorization_LoginResponseDTO
            {
                RequiresTwoFactor = false,
                Message = "Invalid credentials"
            });

        var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
        if (!result.Succeeded)
            return Unauthorized(new Authorization_LoginResponseDTO
            {
                RequiresTwoFactor = false,
                Message = "Invalid credentials"
            });

        // Check if 2FA is enabled
        var is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);

        if (is2faEnabled)
        {
            // No code provided yet - tell client 2FA is required
            if (string.IsNullOrEmpty(model.TwoFactorCode))
            {
                return Ok(new Authorization_LoginResponseDTO
                {
                    RequiresTwoFactor = true,
                    Message = "Two-factor authentication required"
                });
            }

            // Verify TOTP code
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                model.TwoFactorCode);

            if (!isValid)
            {
                return Unauthorized(new Authorization_LoginResponseDTO
                {
                    RequiresTwoFactor = true,
                    Message = "Invalid two-factor authentication code"
                });
            }
        }

        // Generate a unique session hash for this login
        string sessionHash = Generate256BitHash();

        // Get client device info
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers["User-Agent"].ToString() ?? "unknown";
        var deviceName = ExtractDeviceName(userAgent);

        // Create new user session record for multi-session support
        var userSession = new UserSession
        {
            UserId = user.Id,
            SessionHash = sessionHash,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceName = deviceName,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(3), // Match token expiration
            IsRevoked = false
        };

        _dbContext.UserSessions.Add(userSession);

        // Also update legacy SessionHash for backwards compatibility
        ApplicationUser appUser = await _dbContext.Users.SingleAsync(u => u.Id == user.Id);
        appUser.SessionHash = sessionHash;
        appUser.LastLoginAt = DateTime.UtcNow;

        // Auto-select a server if none is selected or current selection is invalid
        int? selectedServerId = appUser.SelectedServerId;
        var accessibleServers = await GetAccessibleServers(appUser);

        if (!selectedServerId.HasValue || !accessibleServers.Any(s => s.Id == selectedServerId.Value))
        {
            var firstServer = accessibleServers.FirstOrDefault();
            selectedServerId = firstServer?.Id;
            appUser.SelectedServerId = selectedServerId;
        }

        _dbContext.Users.Update(appUser);

        await _dbContext.SaveChangesAsync();

        var token = GenerateJwtToken(user, sessionHash);

        // Set HttpOnly cookie with JWT
        SetAuthCookie(token);

        return Ok(new Authorization_LoginResponseDTO
        {
            Token = null,
            RequiresTwoFactor = false,
            Message = "Login successful",
            SelectedServerId = selectedServerId
        });
    }

    /// <summary>
    /// Gets the list of servers a user has access to, respecting moderator permissions.
    /// </summary>
    private async Task<List<RconServer>> GetAccessibleServers(ApplicationUser user)
    {
        var sysProfile = await _dbContext.SystemProfiles.FirstOrDefaultAsync(sp => sp.Id == user.SystemProfileId);
        if (sysProfile == null)
            return new List<RconServer>();

        if (user.IsModerator)
        {
            return await _dbContext.RconServers
                .Where(s => s.SystemProfileId == sysProfile.Id &&
                            s.ModeratorPermissions.Any(mp => mp.UserId == user.Id))
                .OrderBy(s => s.Id)
                .ToListAsync();
        }

        return await _dbContext.RconServers
            .Where(s => s.SystemProfileId == sysProfile.Id)
            .OrderBy(s => s.Id)
            .ToListAsync();
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(Authorization_ForgotPasswordDTO model)
    {
        _logger.LogDebug("[FORGOT-PASSWORD] Request received");

        // Always return 200 to prevent email enumeration
        var genericMessage = "If an account with that email exists, a recovery code has been sent.";

        if (string.IsNullOrWhiteSpace(model.Email))
        {
            _logger.LogDebug("[FORGOT-PASSWORD] Email is empty, returning early");
            return Ok(new { message = genericMessage });
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            _logger.LogDebug("[FORGOT-PASSWORD] User not found, returning generic message");
            return Ok(new { message = genericMessage });
        }

        _logger.LogDebug("[FORGOT-PASSWORD] User found: {UserId}", user.Id);

        // Generate random 6-digit code (overwrites any existing code)
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        // Deliberately not logged, even at Debug - this is the plaintext password-reset
        // code and would otherwise sit in the log/console for anyone with log access.

        // Hash the code with SHA256 before storing
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var hashedBytes = sha256.ComputeHash(codeBytes);
        var hashedCode = Convert.ToBase64String(hashedBytes);

        // Store hashed code and expiry
        user.PasswordResetCode = hashedCode;
        user.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(15);
        await _userManager.UpdateAsync(user);

        // Send the recovery code via SMTP (configured under the "Smtp" section / SMTP_* env vars)
        var sent = await _emailService.SendPasswordRecoveryEmailAsync(model.Email, code);
        _logger.LogDebug("[FORGOT-PASSWORD] Email sent: {Sent}", sent);

        return Ok(new { message = genericMessage });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(Authorization_ResetPasswordDTO model)
    {
        if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Code) ||
            string.IsNullOrWhiteSpace(model.NewPassword))
            return BadRequest(new { message = "All fields are required." });

        if (model.NewPassword != model.ConfirmPassword)
            return BadRequest(new { message = "Passwords do not match." });

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
            return BadRequest(new { message = "Invalid reset request." });

        // Check expiry
        if (!user.PasswordResetCodeExpiry.HasValue || user.PasswordResetCodeExpiry.Value < DateTime.UtcNow)
        {
            // Clear expired code
            user.PasswordResetCode = null;
            user.PasswordResetCodeExpiry = null;
            await _userManager.UpdateAsync(user);
            return BadRequest(new { message = "Reset code has expired. Please request a new one." });
        }

        // Hash submitted code and compare
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var codeBytes = Encoding.UTF8.GetBytes(model.Code);
        var hashedBytes = sha256.ComputeHash(codeBytes);
        var hashedCode = Convert.ToBase64String(hashedBytes);

        if (user.PasswordResetCode != hashedCode)
            return BadRequest(new { message = "Invalid reset code." });

        // Reset the password
        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
            return BadRequest(new { message = "Failed to reset password." });

        var addResult = await _userManager.AddPasswordAsync(user, model.NewPassword);
        if (!addResult.Succeeded)
        {
            var errors = string.Join(", ", addResult.Errors.Select(e => e.Description));
            return BadRequest(new { message = $"Password does not meet requirements: {errors}" });
        }

        // Clear reset code
        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiry = null;
        await _userManager.UpdateAsync(user);

        // Invalidate all existing sessions for security
        var userSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsRevoked = true;
        }

        if (userSessions.Any())
        {
            _dbContext.UserSessions.UpdateRange(userSessions);
            await _dbContext.SaveChangesAsync();
        }

        return Ok(new { message = "Password has been reset successfully. You can now log in with your new password." });
    }

    private void SetAuthCookie(string token)
    {
        Response.Cookies.Append("rrsm_auth", token, new CookieOptions
        {
            HttpOnly = true,
            // Only mark the cookie Secure when the request actually came in over HTTPS.
            // Self-hosted instances are commonly accessed over plain HTTP (bare IP, no
            // reverse proxy yet) - a hardcoded Secure=true would make browsers silently
            // drop the cookie there, breaking login with no visible error.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddHours(3) // Match token expiration
        });
    }

    private void ClearAuthCookie()
    {
        Response.Cookies.Delete("rrsm_auth", new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    private string GenerateJwtToken(ApplicationUser user, string sessionHash = "")
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers["User-Agent"].ToString() ?? "unknown";

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim("ip", ipAddress),
            new Claim("ua", userAgent),
            new Claim(ClaimTypes.Version, "1.0"),
            new Claim(ClaimTypes.Hash, sessionHash)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(3),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    
    // Generate hash that will be used in the Users database table
    // and JWT token to validate session.
    private string Generate256BitHash()
    {
        using (var rng = RandomNumberGenerator.Create())
        {
            var bytes = new byte[32]; // 256 bits = 32 bytes
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }

    /// <summary>
    /// Extracts a user-friendly device name from the User-Agent string
    /// </summary>
    private string ExtractDeviceName(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "Unknown Device";

        // Extract basic browser/device info from User-Agent
        if (userAgent.Contains("Chrome"))
            return "Chrome Browser";
        if (userAgent.Contains("Firefox"))
            return "Firefox Browser";
        if (userAgent.Contains("Safari"))
            return "Safari Browser";
        if (userAgent.Contains("Edge"))
            return "Edge Browser";
        if (userAgent.Contains("Mobile") || userAgent.Contains("Android"))
            return "Mobile Device";
        if (userAgent.Contains("iPad"))
            return "iPad";

        return "Browser";
    }
}
