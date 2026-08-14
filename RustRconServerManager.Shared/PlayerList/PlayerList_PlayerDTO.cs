namespace RustRconServerManager.Shared.PlayerList;

public class PlayerList_PlayerDTO
{
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string? Country { get; set; }
    public bool IsOnline { get; set; }
    public int? CurrentPing { get; set; }
    public double? Health { get; set; }
    public long? TeamId { get; set; }
    public int? ViolationLevel { get; set; }

    // Ban information (from PlayerBans table)
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }

    // Report information (from PlayerReports table)
    public int ReportCount { get; set; }

    // VAC ban information (from Steam API)
    public bool VACBanned { get; set; }
    public int? NumberOfVACBans { get; set; }
    public int? DaysSinceLastVACBan { get; set; }

    // Connection information
    public string? LastIp { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public double ConnectedSeconds { get; set; }

    // Steam account info
    public DateTime? SteamAccountCreated { get; set; }
    public int? RustPlaytimeMinutes { get; set; }
    public int? ProfileVisibility { get; set; } // 1 = Private, 3 = Public

    // VPN/Proxy detection result (from proxycheck.io cache)
    public bool IsVpn { get; set; }

    // Position data (only for online players)
    public Position? Position { get; set; }
}

public class Position
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class PlayerList_PlayerListDTO
{
    public List<PlayerList_PlayerDTO> OnlinePlayers { get; set; } = new();
    public List<PlayerList_PlayerDTO> OfflinePlayers { get; set; } = new();
    public int TotalOnline { get; set; }
    public int TotalOffline { get; set; }

    // Offline pagination metadata
    public int OfflinePage { get; set; } = 1;
    public int OfflinePageSize { get; set; } = 100;
    public int OfflineFilteredTotal { get; set; }

    // Total banned across the full (unpaginated) set
    public int TotalBanned { get; set; }
}
