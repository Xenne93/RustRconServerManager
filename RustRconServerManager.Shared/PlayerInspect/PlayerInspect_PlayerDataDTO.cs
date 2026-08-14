namespace RustRconServerManager.Shared.PlayerInspect;

public class PlayerInspect_PlayerDataDTO
{
    // Basic player info
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string? Country { get; set; }
    public bool IsOnline { get; set; }

    // Connection info
    public string? LastIp { get; set; }
    public bool IsVpn { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public double TotalPlaytimeHours { get; set; }

    // Current stats (if online)
    public int? CurrentPing { get; set; }
    public float? CurrentHealth { get; set; }
    public long? CurrentTeamId { get; set; }
    public Position? CurrentPosition { get; set; }

    // VAC ban info
    public bool VACBanned { get; set; }
    public int? NumberOfVACBans { get; set; }
    public int? DaysSinceLastVACBan { get; set; }

    // Steam account info
    public DateTime? SteamAccountCreated { get; set; }
    public int? RustPlaytimeMinutes { get; set; }
    public int? ProfileVisibility { get; set; } // 1 = Private, 3 = Public

    // Ban info
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
    public DateTime? BanExpiry { get; set; }
    public bool IsGlobalBan { get; set; }

    // Statistics
    public int TotalChatMessages { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
}

public class Position
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public class PlayerInspect_ChatMessageDTO
{
    public int Id { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PlayerInspect_KillLogDTO
{
    public int Id { get; set; }
    public string KillerName { get; set; } = string.Empty;
    public string VictimName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsPVP { get; set; }
}

public class PlayerInspect_BanHistoryDTO
{
    public int Id { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public long? DurationHours { get; set; }
    public bool IsActive { get; set; }
    public bool IsGlobalBan { get; set; }
    public string? BannedBy { get; set; }
    public string? LiftedBy { get; set; }
    public DateTime? LiftedAt { get; set; }
    public string? LiftReason { get; set; }
}

public class PlayerInspect_ResponseDTO
{
    public PlayerInspect_PlayerDataDTO PlayerData { get; set; } = new();
    public List<PlayerInspect_ChatMessageDTO> RecentChatMessages { get; set; } = new();
    public List<PlayerInspect_KillLogDTO> RecentKills { get; set; } = new();
    public List<PlayerInspect_KillLogDTO> RecentDeaths { get; set; } = new();
    public List<PlayerInspect_BanHistoryDTO> BanHistory { get; set; } = new();

    // IP history
    public List<PlayerInspect_IpHistoryDTO> IpHistory { get; set; } = new();
}

public class PlayerInspect_IpHistoryDTO
{
    public string IpAddress { get; set; } = string.Empty;
    public bool IsVpn { get; set; }
    public string? Country { get; set; }
    public DateTime FirstUsed { get; set; }
    public DateTime LastUsed { get; set; }
    public int TimesUsed { get; set; }
}
