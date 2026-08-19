using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.Cli;

// Invoked via `--reset-password` for admins who are locked out and have no working SMTP
// configuration for the email-code "forgot password" flow. Mirrors AuthController's
// ResetPassword action (RemovePasswordAsync/AddPasswordAsync + session revocation) but
// skips the email-code verification step entirely, since running this requires terminal
// access to the host/container the app itself runs on.
public static class ResetPasswordCli
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Console.WriteLine("RustRconServerManager - Password Reset");
        Console.WriteLine();

        Console.Write("Account username: ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            Console.WriteLine("No username entered. Aborting.");
            return 1;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user == null)
        {
            Console.WriteLine($"No account found for '{username}'.");
            return 1;
        }

        string newPassword;
        while (true)
        {
            Console.Write("New password: ");
            var password = ReadMaskedLine();
            Console.Write("Confirm new password: ");
            var confirm = ReadMaskedLine();

            if (password != confirm)
            {
                Console.WriteLine("Passwords do not match. Try again.");
                Console.WriteLine();
                continue;
            }

            if (string.IsNullOrEmpty(password))
            {
                Console.WriteLine("Password cannot be empty. Try again.");
                Console.WriteLine();
                continue;
            }

            newPassword = password;
            break;
        }

        var removeResult = await userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
        {
            var errors = string.Join(", ", removeResult.Errors.Select(e => e.Description));
            Console.WriteLine($"Failed to reset password: {errors}");
            return 1;
        }

        var addResult = await userManager.AddPasswordAsync(user, newPassword);
        if (!addResult.Succeeded)
        {
            var errors = string.Join(", ", addResult.Errors.Select(e => e.Description));
            Console.WriteLine($"Password does not meet requirements: {errors}");
            return 1;
        }

        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiry = null;
        await userManager.UpdateAsync(user);

        var userSessions = await dbContext.UserSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsRevoked = true;
        }

        if (userSessions.Any())
        {
            dbContext.UserSessions.UpdateRange(userSessions);
            await dbContext.SaveChangesAsync();
        }

        Console.WriteLine();
        Console.WriteLine($"Password for '{username}' has been reset. Any existing sessions have been signed out.");
        return 0;
    }

    private static string ReadMaskedLine()
    {
        // Console.ReadKey requires a real console - falls back to plain (unmasked)
        // input when stdin isn't a TTY (e.g. piped input, `docker exec` without -it).
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var input = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                input.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return input.ToString();
    }
}
