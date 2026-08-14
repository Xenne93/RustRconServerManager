namespace RustRconServerManager.Shared.Security;

public class Security_Enable2FADTO
{
    public string VerificationCode { get; set; } = string.Empty;
}

public class Security_2FASetupResponseDTO
{
    public string QrCodeUri { get; set; } = string.Empty;
    public string ManualEntryKey { get; set; } = string.Empty;
}

public class Security_Disable2FADTO
{
    public string TwoFactorCode { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class Security_2FAStatusDTO
{
    public bool IsEnabled { get; set; }
    public bool HasRecoveryCodes { get; set; }
}
