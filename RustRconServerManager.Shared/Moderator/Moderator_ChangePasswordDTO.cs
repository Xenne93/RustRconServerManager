namespace RustRconServerManager.Shared.Moderator;

/// <summary>
/// DTO for changing a moderator's password
/// </summary>
public class Moderator_ChangePasswordDTO
{
    public string Id { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
