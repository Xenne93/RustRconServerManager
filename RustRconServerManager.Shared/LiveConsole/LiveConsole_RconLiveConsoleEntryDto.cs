namespace RustRconServerManager.Shared.LiveConsole;

public class LiveConsole_RconLiveConsoleEntryDto
{
    /// <summary>
    /// Id van de server waar dit log bericht bij hoort.
    /// </summary>
    public int ServerId { get; set; }

    /// <summary>
    /// UTC timestamp van het log bericht.
    /// </summary>
    public DateTime TimeStamp { get; set; }

    /// <summary>
    /// Eigenlijke tekst van het log bericht.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}