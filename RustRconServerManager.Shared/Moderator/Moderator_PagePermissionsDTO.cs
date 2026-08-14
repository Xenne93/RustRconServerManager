namespace RustRconServerManager.Shared.Moderator;

/// <summary>
/// DTO for setting a moderator's page permissions
/// </summary>
public class Moderator_SetPagePermissionsDTO
{
    public string UserId { get; set; } = string.Empty;
    public List<string> PageRoutes { get; set; } = new();
}

/// <summary>
/// DTO for getting a moderator's page permissions
/// </summary>
public class Moderator_PagePermissionsResponseDTO
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<string> AllowedPages { get; set; } = new();
}

/// <summary>
/// Available pages that can be assigned to moderators
/// </summary>
public class Moderator_AvailablePageDTO
{
    public string Route { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
