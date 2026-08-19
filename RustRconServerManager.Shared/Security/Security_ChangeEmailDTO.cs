namespace RustRconServerManager.Shared.Security;

public class Security_ChangeEmailDTO
{
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
}
