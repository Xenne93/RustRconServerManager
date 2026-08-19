namespace RustRconServerManager.Shared.Authorization;

public class Authorization_UserLoginDTO
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string? TwoFactorCode { get; set; }
}

public class Authorization_LoginResponseDTO
{
    public string? Token { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public string? Message { get; set; }
    public int? SelectedServerId { get; set; }
}
