using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.Moderator;

namespace RustRconServerManager.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModeratorController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public ModeratorController(AppDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpGet("List")]
    public async Task<IActionResult> List()
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // Only admins can manage moderators
            if (!user.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            var moderators = await _dbContext.Users
                .Where(m => m.SystemProfileId == profile.Id && m.IsModerator)
                .Include(m => m.ServerPermissions)
                .ThenInclude(sp => sp.RconServer)
                .Select(m => new Moderator_ListItemDTO
                {
                    Id = m.Id,
                    Username = m.UserName ?? string.Empty,
                    DisplayName = m.DisplayName,
                    Email = m.Email,
                    IsActive = !m.isLoginBlocked,
                    CreatedAt = m.CreatedAt,
                    LastLoginAt = m.LastLoginAt,
                    ServerPermissions = m.ServerPermissions.Select(sp => new Moderator_ServerPermissionDTO
                    {
                        ServerId = sp.RconServerId,
                        ServerName = sp.RconServer.Name,
                        GrantedAt = sp.GrantedAt
                    }).ToList()
                })
                .ToListAsync();

            return Ok(moderators);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to retrieve moderators", ex));
        }
    }

    [HttpPost("Create")]
    public async Task<IActionResult> Create([FromBody] Moderator_CreateDTO dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);

            // Only admins can create moderators
            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            // Validate username is unique
            var existingUser = await _userManager.FindByNameAsync(dto.Username);
            if (existingUser != null)
            {
                return Conflict("A user with this username already exists.");
            }

            // Validate email is unique if provided
            if (!string.IsNullOrEmpty(dto.Email))
            {
                var existingEmail = await _userManager.FindByEmailAsync(dto.Email);
                if (existingEmail != null)
                {
                    return Conflict("A user with this email already exists.");
                }
            }

            // Validate server IDs belong to this profile
            var validServerIds = await _dbContext.RconServers
                .Where(s => s.SystemProfileId == profile.Id && dto.ServerIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync();

            if (validServerIds.Count != dto.ServerIds.Count)
            {
                return BadRequest("One or more server IDs are invalid.");
            }

            // Create moderator user
            var moderator = new ApplicationUser
            {
                UserName = dto.Username,
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
                EmailConfirmed = !string.IsNullOrWhiteSpace(dto.Email),
                DisplayName = dto.DisplayName,
                SystemProfileId = profile.Id,
                IsModerator = true,
                isLoginBlocked = false,
                HasChosenUsername = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(moderator, dto.Password);

            if (!result.Succeeded)
            {
                return BadRequest($"Failed to create moderator: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            // Add server permissions
            foreach (var serverId in validServerIds)
            {
                var permission = new ModeratorServerPermission
                {
                    UserId = moderator.Id,
                    RconServerId = serverId,
                    GrantedAt = DateTime.UtcNow
                };
                _dbContext.ModeratorServerPermissions.Add(permission);
            }

            await _dbContext.SaveChangesAsync();

            // Return the created moderator info
            var createdModeratorDto = new Moderator_ListItemDTO
            {
                Id = moderator.Id,
                Username = moderator.UserName ?? string.Empty,
                DisplayName = moderator.DisplayName,
                Email = moderator.Email,
                IsActive = !moderator.isLoginBlocked,
                LastLoginAt = moderator.LastLoginAt,
                ServerPermissions = validServerIds.Select(sid => new Moderator_ServerPermissionDTO
                {
                    ServerId = sid
                }).ToList()
            };

            return Ok(createdModeratorDto);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to create moderator", ex));
        }
    }

    [HttpPut("Update/{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] Moderator_UpdateDTO dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);

            // Only admins can update moderators
            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            var moderator = await _dbContext.Users
                .Include(m => m.ServerPermissions)
                .FirstOrDefaultAsync(m => m.Id == id && m.SystemProfileId == profile.Id && m.IsModerator);

            if (moderator == null)
            {
                return NotFound("Moderator not found.");
            }

            // Validate server IDs belong to this profile
            var validServerIds = await _dbContext.RconServers
                .Where(s => s.SystemProfileId == profile.Id && dto.ServerIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync();

            if (validServerIds.Count != dto.ServerIds.Count)
            {
                return BadRequest("One or more server IDs are invalid.");
            }

            // Update moderator properties
            moderator.DisplayName = dto.DisplayName;
            moderator.Email = dto.Email;
            moderator.isLoginBlocked = !dto.IsActive;

            await _userManager.UpdateAsync(moderator);

            // Remove old permissions
            _dbContext.ModeratorServerPermissions.RemoveRange(moderator.ServerPermissions);

            // Add new permissions
            foreach (var serverId in validServerIds)
            {
                var permission = new ModeratorServerPermission
                {
                    UserId = moderator.Id,
                    RconServerId = serverId,
                    GrantedAt = DateTime.UtcNow
                };
                _dbContext.ModeratorServerPermissions.Add(permission);
            }

            await _dbContext.SaveChangesAsync();

            return Ok("Moderator updated successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to update moderator", ex));
        }
    }

    [HttpPut("ChangePassword/{id}")]
    public async Task<IActionResult> ChangePassword(string id, [FromBody] Moderator_ChangePasswordDTO dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);

            // Only admins can change moderator passwords
            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            var moderator = await _dbContext.Users
                .FirstOrDefaultAsync(m => m.Id == id && m.SystemProfileId == profile.Id && m.IsModerator);

            if (moderator == null)
            {
                return NotFound("Moderator not found.");
            }

            // Remove old password and set new one
            await _userManager.RemovePasswordAsync(moderator);
            var result = await _userManager.AddPasswordAsync(moderator, dto.NewPassword);

            if (!result.Succeeded)
            {
                return BadRequest($"Failed to change password: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            return Ok("Password changed successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to change password", ex));
        }
    }

    [HttpDelete("Delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);

            // Only admins can delete moderators
            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            var moderator = await _dbContext.Users
                .FirstOrDefaultAsync(m => m.Id == id && m.SystemProfileId == profile.Id && m.IsModerator);

            if (moderator == null)
            {
                return NotFound("Moderator not found.");
            }

            await _userManager.DeleteAsync(moderator);

            return Ok("Moderator deleted successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to delete moderator", ex));
        }
    }

    /// <summary>
    /// Get list of all available pages that can be assigned to moderators
    /// </summary>
    [HttpGet("AvailablePages")]
    public async Task<IActionResult> GetAvailablePages()
    {
        var currentUser = await User.GetUser(_dbContext);

        if (!currentUser.isAdmin)
        {
            return Forbid();
        }

        var availablePages = new List<Moderator_AvailablePageDTO>
        {
            new() { Route = "dashboard-console", DisplayName = "Dashboard Console", Description = "Live console widget on dashboard" },
            new() { Route = "dashboard-chat", DisplayName = "Dashboard Chat", Description = "Live chat widget on dashboard" },
            new() { Route = "dashboard-map", DisplayName = "Dashboard Map", Description = "Server map on dashboard" },
            new() { Route = "dashboard-stats", DisplayName = "Dashboard Stats", Description = "Performance statistics charts" },
            new() { Route = "players", DisplayName = "Players", Description = "Player management and information" },
            new() { Route = "banlist", DisplayName = "Ban List", Description = "Manage player bans" },
            new() { Route = "scheduler", DisplayName = "Scheduler", Description = "Scheduled commands and tasks" },
            new() { Route = "triggers", DisplayName = "Triggers", Description = "Event-based automation" },
            new() { Route = "server-actions", DisplayName = "Server Actions", Description = "Quick server actions and commands" },
            new() { Route = "server-management", DisplayName = "Server Management", Description = "Server configuration" },
            new() { Route = "console", DisplayName = "Console", Description = "Server console access" }
        };

        return Ok(availablePages);
    }

    /// <summary>
    /// Get page permissions for a specific moderator
    /// </summary>
    [HttpGet("PagePermissions/{userId}")]
    public async Task<IActionResult> GetPagePermissions(string userId)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);

            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            var moderator = await _dbContext.Users
                .Include(u => u.PagePermissions)
                .FirstOrDefaultAsync(m => m.Id == userId && m.SystemProfileId == profile.Id && m.IsModerator);

            if (moderator == null)
            {
                return NotFound("Moderator not found.");
            }

            var response = new Moderator_PagePermissionsResponseDTO
            {
                UserId = moderator.Id,
                Username = moderator.UserName ?? string.Empty,
                AllowedPages = moderator.PagePermissions.Select(p => p.PageRoute).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to get page permissions", ex));
        }
    }

    /// <summary>
    /// Set page permissions for a moderator
    /// </summary>
    [HttpPost("PagePermissions")]
    public async Task<IActionResult> SetPagePermissions([FromBody] Moderator_SetPagePermissionsDTO dto)
    {
        try
        {
            var currentUser = await User.GetUser(_dbContext);

            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            var profile = await User.GetUserSystemProfile(_dbContext);

            var moderator = await _dbContext.Users
                .Include(u => u.PagePermissions)
                .FirstOrDefaultAsync(m => m.Id == dto.UserId && m.SystemProfileId == profile.Id && m.IsModerator);

            if (moderator == null)
            {
                return NotFound("Moderator not found.");
            }

            // Remove all existing page permissions
            _dbContext.ModeratorPagePermissions.RemoveRange(moderator.PagePermissions);

            // Add new permissions
            foreach (var pageRoute in dto.PageRoutes)
            {
                var permission = new ModeratorPagePermission
                {
                    UserId = moderator.Id,
                    PageRoute = pageRoute,
                    GrantedAt = DateTime.UtcNow
                };
                _dbContext.ModeratorPagePermissions.Add(permission);
            }

            await _dbContext.SaveChangesAsync();

            return Ok("Page permissions updated successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to update page permissions", ex));
        }
    }

    /// <summary>
    /// Check if the current user (moderator) has access to a specific page
    /// This endpoint is used by the frontend to check page access
    /// </summary>
    [HttpGet("CheckPageAccess/{pageRoute}")]
    public async Task<IActionResult> CheckPageAccess(string pageRoute)
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // Admins have access to all pages
            if (user.isAdmin)
            {
                return Ok(new { hasAccess = true });
            }

            // Non-moderators (regular users) have access to all pages
            if (!user.IsModerator)
            {
                return Ok(new { hasAccess = true });
            }

            // Dashboard is always accessible for moderators
            if (pageRoute == "dashboard")
            {
                return Ok(new { hasAccess = true });
            }

            // Check if moderator has permission for this page
            var hasPermission = await _dbContext.ModeratorPagePermissions
                .AnyAsync(p => p.UserId == user.Id && p.PageRoute == pageRoute);

            return Ok(new { hasAccess = hasPermission });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to check page access", ex));
        }
    }

    /// <summary>
    /// Get the current user's page permissions (for navigation menu filtering)
    /// </summary>
    [HttpGet("MyPagePermissions")]
    public async Task<IActionResult> GetMyPagePermissions()
    {
        try
        {
            var user = await User.GetUser(_dbContext);

            // If not a moderator, return empty list (they have access to all pages)
            if (!user.IsModerator)
            {
                return Ok(new { allowedPages = new List<string>() });
            }

            // Get moderator's page permissions
            var allowedPages = await _dbContext.ModeratorPagePermissions
                .Where(p => p.UserId == user.Id)
                .Select(p => p.PageRoute)
                .ToListAsync();

            // Dashboard is always accessible for moderators
            if (!allowedPages.Contains("dashboard"))
            {
                allowedPages.Add("dashboard");
            }

            return Ok(new { allowedPages });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorHelper.FormatError("Failed to get page permissions", ex));
        }
    }
}
