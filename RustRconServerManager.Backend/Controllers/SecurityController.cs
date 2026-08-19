using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.Security;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SecurityController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UrlEncoder _urlEncoder;

    public SecurityController(
        AppDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        UrlEncoder urlEncoder)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _urlEncoder = urlEncoder;
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] Security_ChangePasswordDTO dto)
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // Validate passwords match
            if (dto.NewPassword != dto.ConfirmPassword)
            {
                return BadRequest("New password and confirmation do not match.");
            }

            // If 2FA is enabled, verify the code
            if (await _userManager.GetTwoFactorEnabledAsync(user))
            {
                if (string.IsNullOrEmpty(dto.TwoFactorCode))
                {
                    return BadRequest("Two-factor authentication code is required.");
                }

                var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    dto.TwoFactorCode);

                if (!isValid)
                {
                    return BadRequest("Invalid two-factor authentication code.");
                }
            }

            // Change the password
            var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

            if (!result.Succeeded)
            {
                return BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            return Ok(new { message = "Password changed successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to change password", ex));
        }
    }

    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail([FromBody] Security_ChangeEmailDTO dto)
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            if (string.IsNullOrWhiteSpace(dto.NewEmail) || !MailAddress.TryCreate(dto.NewEmail, out _))
            {
                return BadRequest("A valid email address is required.");
            }

            var passwordValid = await _userManager.CheckPasswordAsync(user, dto.CurrentPassword);
            if (!passwordValid)
            {
                return BadRequest("Current password is incorrect.");
            }

            var existingUser = await _userManager.FindByEmailAsync(dto.NewEmail);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return BadRequest("This email address is already in use.");
            }

            var setEmailResult = await _userManager.SetEmailAsync(user, dto.NewEmail);
            if (!setEmailResult.Succeeded)
            {
                return BadRequest(string.Join(", ", setEmailResult.Errors.Select(e => e.Description)));
            }

            // Username is a separate, independent identifier now (login is by username,
            // not email) - it must NOT be overwritten here.
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);

            // Identity/session resolution is keyed on the user's Id (see GetUser(),
            // SecurityBindingMiddleware), not email, so changing it doesn't invalidate the
            // current session - no need to log the user out.
            return Ok(new { message = "Email changed successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to change email", ex));
        }
    }

    [HttpGet("2fa/status")]
    public async Task<IActionResult> Get2FAStatus()
    {
        try
        {
            var user = await User.GetUser(_dbContext);
            var is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);

            return Ok(new Security_2FAStatusDTO
            {
                IsEnabled = is2faEnabled,
                HasRecoveryCodes = false
            });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to get 2FA status", ex));
        }
    }

    [HttpPost("2fa/setup")]
    public async Task<IActionResult> Setup2FA()
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // Reset the authenticator key
            await _userManager.ResetAuthenticatorKeyAsync(user);
            var key = await _userManager.GetAuthenticatorKeyAsync(user);

            if (string.IsNullOrEmpty(key))
            {
                return BadRequest("Failed to generate authenticator key.");
            }

            // Generate QR code URI
            var email = user.Email ?? user.UserName ?? "user";
            var qrCodeUri = GenerateQrCodeUri(email, key);

            return Ok(new Security_2FASetupResponseDTO
            {
                QrCodeUri = qrCodeUri,
                ManualEntryKey = FormatKey(key)
            });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to setup 2FA", ex));
        }
    }

    [HttpPost("2fa/enable")]
    public async Task<IActionResult> Enable2FA([FromBody] Security_Enable2FADTO dto)
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // Verify the code
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                dto.VerificationCode);

            if (!isValid)
            {
                return BadRequest("Invalid verification code. Please try again.");
            }

            // Enable 2FA
            await _userManager.SetTwoFactorEnabledAsync(user, true);

            return Ok(new { message = "Two-factor authentication has been enabled successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to enable 2FA", ex));
        }
    }

    [HttpPost("2fa/disable")]
    public async Task<IActionResult> Disable2FA([FromBody] Security_Disable2FADTO dto)
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // Verify password
            var passwordValid = await _userManager.CheckPasswordAsync(user, dto.Password);
            if (!passwordValid)
            {
                return BadRequest("Invalid password.");
            }

            // Verify 2FA code
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                dto.TwoFactorCode);

            if (!isValid)
            {
                return BadRequest("Invalid two-factor authentication code.");
            }

            // Disable 2FA
            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);

            return Ok(new { message = "Two-factor authentication has been disabled." });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to disable 2FA", ex));
        }
    }

    private string GenerateQrCodeUri(string email, string key)
    {
        const string issuer = "RustRconServerManager";
        return $"otpauth://totp/{_urlEncoder.Encode(issuer)}:{_urlEncoder.Encode(email)}?secret={key}&issuer={_urlEncoder.Encode(issuer)}&digits=6";
    }

    private string FormatKey(string key)
    {
        // Format key as XXXX XXXX XXXX XXXX for easier manual entry
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < key.Length)
        {
            result.Append(key.Substring(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < key.Length)
        {
            result.Append(key.Substring(currentPosition));
        }

        return result.ToString().ToUpperInvariant();
    }
}
