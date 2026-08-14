namespace RustRconServerManager.Backend.Models;

public class SteamPlayer
{
    public int Id { get; set; }
    public string SteamId { get; set; }
    public string? Name { get; set; }
    public int ServerId { get; set; }
    public string? Avatar { get; set; }
    public DateTime? AvatarLastUpdated { get; set; }
    public string? Country { get; set; }
    public string? LastIp { get; set; }
    public int? LatestPing { get; set; }
    public int? LatestTeamId { get; set; }
    public bool IsOnline { get; set; }
    public float? LatestHealth { get; set; }
    public float? LatestPositionX { get; set; }
    public float? LatestPositionY { get; set; }
    public float? LatestPositionZ { get; set; }

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool VACBanned { get; set; }
    public int? NumberOfVACBans { get; set; }
    public int? DaysSinceLastVACBan { get; set; }

    // Steam account information
    public DateTime? SteamAccountCreated { get; set; }
    public int? RustPlaytimeMinutes { get; set; }
    public int? ProfileVisibility { get; set; } // 1 = Private, 3 = Public
}