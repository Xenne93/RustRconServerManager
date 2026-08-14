namespace RustRconServerManager.Shared.Moderator;

/// <summary>
/// DTO for listing moderator accounts
/// </summary>
public class Moderator_ListItemDTO
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<Moderator_ServerPermissionDTO> ServerPermissions { get; set; } = new();
}

/// <summary>
/// DTO for server permission information
/// </summary>
public class Moderator_ServerPermissionDTO
{
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
}
