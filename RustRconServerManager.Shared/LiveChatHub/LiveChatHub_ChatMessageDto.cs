namespace RustRconServerManager.Shared.LiveChatHub;

public class LiveChatHub_ChatMessageDto
{
    /// <summary>
    /// Id van de server waar dit bericht bij hoort.
    /// </summary>
    public int ServerId { get; set; }

    /// <summary>
    /// Weergavenaam van de gebruiker (ingelogde user of ingame naam).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SteamId van de gebruiker (indien beschikbaar).
    /// </summary>
    public string? SteamId { get; set; }

    /// <summary>
    /// Eigenlijke tekst van het chatbericht.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp van het bericht.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Chat channel: "0" for global, "1" for team.
    /// </summary>
    public string Channel { get; set; } = "0";
}