using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Exceptions;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.Extensions;

using System.Security.Claims;

public static class ClaimsPrincipalExtensions
{
    public static async Task<SystemProfile> GetUserSystemProfile(this ClaimsPrincipal user, AppDbContext context)
    {
        var applicationUser = await user.GetUser(context);

        return await context.SystemProfiles.FirstOrDefaultAsync(sp => sp.Id == applicationUser.SystemProfileId)
            ?? throw new UserNotAuthenticatedException("System profile not found for user.");
    }

    public static async Task<ApplicationUser> GetUser(this ClaimsPrincipal user, AppDbContext context)
    {
        var userEmail = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
        var sessionHash = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Hash)?.Value;

        if (string.IsNullOrWhiteSpace(userEmail) || string.IsNullOrWhiteSpace(sessionHash))
            throw new UserNotAuthenticatedException();

        return await context.Users.FirstOrDefaultAsync(u => u.Email == userEmail)
            ?? throw new UserNotAuthenticatedException();
    }

    public static string? GetEmail(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Email)?.Value;
    }

    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole("Admin");
    }
    
    
    // Check if the user has access to a specific server (based on ID).
    // Returns false if user has no access, true is the user does have access.
    public static async Task<bool> HasServerAccess(this ClaimsPrincipal user, AppDbContext context, int serverId)
    {
        var userProfile = await user.GetUserSystemProfile(context);
        if (userProfile == null)
            return false;

        var server = await context.RconServers.FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return false;

        return server.SystemProfileId == userProfile.Id;
    }

    
}
