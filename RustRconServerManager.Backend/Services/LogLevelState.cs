using Microsoft.Extensions.Logging;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Holds the currently active minimum log level. Adjustable at runtime from the Panel
/// Settings page (see PanelSettingsController.SetLogLevel) without an app restart - the
/// logging filter registered in Program.cs reads this on every log call.
/// </summary>
public static class LogLevelState
{
    private static volatile LogLevel _minimum = LogLevel.Error;

    public static LogLevel Minimum
    {
        get => _minimum;
        set => _minimum = value;
    }

    public static bool TryParse(string? value, out LogLevel level) =>
        Enum.TryParse(value, ignoreCase: true, out level);
}
