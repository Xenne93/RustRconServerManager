namespace RustRconServerManager.Backend.Models;

/// <summary>
/// A fake VAC-ban profile assigned to a SteamID for testing the VAC-ban protection
/// rules. Only consulted by SteamApiService.GetPlayerVACBanInfoAsync while
/// PanelSettings.DeveloperModeEnabled is true.
/// </summary>
public class DeveloperVacBanOverride
{
    public int Id { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public bool VACBanned { get; set; } = true;

    public int NumberOfVACBans { get; set; } = 1;

    public int DaysSinceLastBan { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
