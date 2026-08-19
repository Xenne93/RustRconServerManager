namespace RustRconServerManager.Shared.Account;

public class Account_AccountInformationDTO
{

    public string Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public bool isAdmin { get; set; }
    public bool IsModerator { get; set; }
    public string? Website { get; set; }
    public DateTime CreatedAt { get; set; }

}