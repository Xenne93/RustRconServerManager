namespace RustRconServerManager.Backend.Services;

public interface IEmailService
{
    Task<bool> SendPasswordRecoveryEmailAsync(string toEmail, string code);
}
