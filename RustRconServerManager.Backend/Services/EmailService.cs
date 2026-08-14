using System.Net;
using System.Net.Mail;

namespace RustRconServerManager.Backend.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendPasswordRecoveryEmailAsync(string toEmail, string code)
    {
        var host = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("SMTP is not configured (Smtp:Host is empty) - password recovery email was not sent to {Email}", toEmail);
            return false;
        }

        var port = _configuration.GetValue<int>("Smtp:Port", 587);
        var user = _configuration["Smtp:User"];
        var password = _configuration["Smtp:Password"];
        var from = _configuration["Smtp:From"] ?? user ?? "no-reply@localhost";
        var enableSsl = _configuration.GetValue<bool>("Smtp:EnableSsl", true);

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl
            };

            if (!string.IsNullOrWhiteSpace(user))
            {
                client.Credentials = new NetworkCredential(user, password);
            }

            using var message = new MailMessage(from, toEmail)
            {
                Subject = "Password recovery code",
                Body = $"Your password recovery code is: {code}\n\nThis code expires in 15 minutes.",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send password recovery email to {Email}", toEmail);
            return false;
        }
    }
}
