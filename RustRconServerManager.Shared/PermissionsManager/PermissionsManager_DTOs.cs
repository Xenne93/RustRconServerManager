namespace RustRconServerManager.Shared.PermissionsManager
{
    /// <summary>
    /// Response DTO for permissions manager
    /// </summary>
    public class PermissionsManager_ResponseDTO
    {
        public string Message { get; set; } = string.Empty;
        public List<PermissionGroupDTO> Groups { get; set; } = new();
        public List<PlayerPermissionDTO> PlayerPermissions { get; set; } = new();
    }

    /// <summary>
    /// Represents a permission group (e.g., admin, moderator, vip)
    /// </summary>
    public class PermissionGroupDTO
    {
        public string GroupName { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public int PlayerCount { get; set; }
        public List<GroupPlayerDTO> Players { get; set; } = new();
    }

    /// <summary>
    /// Represents a player in a permission group
    /// </summary>
    public class GroupPlayerDTO
    {
        public string SteamId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a player's permissions
    /// </summary>
    public class PlayerPermissionDTO
    {
        public string SteamId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public List<string> Groups { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
    }

    /// <summary>
    /// Request DTO for adding a player to a group
    /// </summary>
    public class PermissionsManager_AddToGroupDTO
    {
        public string SteamId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for removing a player from a group
    /// </summary>
    public class PermissionsManager_RemoveFromGroupDTO
    {
        public string SteamId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for creating a new permission group
    /// </summary>
    public class PermissionsManager_CreateGroupDTO
    {
        public string GroupName { get; set; } = string.Empty;
        public string? ParentGroup { get; set; }
    }

    /// <summary>
    /// Request DTO for deleting a permission group
    /// </summary>
    public class PermissionsManager_DeleteGroupDTO
    {
        public string GroupName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Player search result DTO
    /// </summary>
    public class PlayerSearchResultDTO
    {
        public string SteamId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
    }
}
