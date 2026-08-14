namespace RustRconServerManager.Shared.Moderator;

/// <summary>
/// DTO for creating a new moderator account
/// </summary>
public class Moderator_CreateDTO
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public List<int> ServerIds { get; set; } = new();
}
