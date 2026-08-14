namespace RustRconServerManager.Shared.ServerSettings
{
    public class ServerSettings_DTO
    {
        public string ServerHostname { get; set; } = string.Empty;
        public string ServerDescription { get; set; } = string.Empty;
        public string ServerUrl { get; set; } = string.Empty;
        public string ServerHeaderImage { get; set; } = string.Empty;
        public string ServerLogoImage { get; set; } = string.Empty;
        public int MaxPlayers { get; set; } = 0;
    }

    public class ServerSettings_UpdateDTO
    {
        public string ServerHostname { get; set; } = string.Empty;
        public string ServerDescription { get; set; } = string.Empty;
        public string ServerUrl { get; set; } = string.Empty;
        public string ServerHeaderImage { get; set; } = string.Empty;
        public string ServerLogoImage { get; set; } = string.Empty;
        public int MaxPlayers { get; set; } = 0;
    }
}
