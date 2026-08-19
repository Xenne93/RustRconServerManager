using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Shared.Account;

namespace RustRconServerManager.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public AccountController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
            
        }
        
        
        /// <summary>
        /// Get basic user info - accessible by all users including moderators
        /// </summary>
        [HttpGet("BasicInfo")]
        public IActionResult GetBasicInfo()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("No email claim found");

            var user = _dbContext.Users.Where(x => x.Email == userEmail).FirstOrDefault();

            if (user == null)
                return NotFound();

            return Ok(new
            {
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsModerator = user.IsModerator,
                isAdmin = user.isAdmin
            });
        }

        /// <summary>
        /// Get user's selected server ID from database - accessible by all users
        /// </summary>
        [HttpGet("SelectedServerId")]
        public IActionResult GetSelectedServerId()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("No email claim found");

            var user = _dbContext.Users.Where(x => x.Email == userEmail).FirstOrDefault();

            if (user == null)
                return NotFound();

            return Ok(new
            {
                SelectedServerId = user.SelectedServerId
            });
        }

        /// <summary>
        /// Get full account information - NOT accessible by moderators
        /// </summary>
        [HttpGet("AccountInformation")]
        public IActionResult AccountInformation()
        {

            var userEmail = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("No email claim found");

            var accountInformation = _dbContext.Users.Where(x => x.Email == userEmail).FirstOrDefault();

            if (accountInformation == null)
                return NotFound();

            // Moderators cannot access the full account page
            if (accountInformation.IsModerator)
                return Forbid("Moderators cannot access account settings");

            Account_AccountInformationDTO dto = new Account_AccountInformationDTO();

            dto.Email = accountInformation.Email;
            dto.DisplayName = accountInformation.DisplayName;
            dto.CreatedAt = accountInformation.CreatedAt;
            dto.isAdmin = accountInformation.isAdmin;
            dto.IsModerator = accountInformation.IsModerator;
            dto.Theme = accountInformation.Theme;
            dto.Website = accountInformation.Website;

            return Ok(dto);
        }

        /// <summary>
        /// Sets the current user's display name - shown across the panel (navbar, moderator
        /// lists, audit log) instead of their email address. Required for every account.
        /// </summary>
        [HttpPut("DisplayName")]
        public async Task<IActionResult> SetDisplayName([FromBody] Account_SetDisplayNameDTO dto)
        {
            var trimmed = dto.DisplayName?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(trimmed))
                return BadRequest("Display name is required.");

            if (trimmed.Length > 50)
                return BadRequest("Display name must be 50 characters or fewer.");

            var userEmail = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("No email claim found");

            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email == userEmail);

            if (user == null)
                return NotFound();

            user.DisplayName = trimmed;
            await _dbContext.SaveChangesAsync();

            return Ok(new { DisplayName = user.DisplayName });
        }
    }
}