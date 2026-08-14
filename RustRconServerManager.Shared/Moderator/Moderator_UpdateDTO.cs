namespace RustRconServerManager.Shared.Moderator;

/// <summary>
/// DTO for updating a moderator account
/// </summary>
public class Moderator_UpdateDTO
{
    public string Id { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public List<int> ServerIds { get; set; } = new();
}
