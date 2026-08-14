namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Mirrors the DB-backed PanelSettings.AutoUpdateEnabled setting to a plain flag file
/// (ContentRoot/data/autoupdate.flag) so external, pre-process-start scripts - which have
/// no access to the app's own database driver - can read the current setting cheaply.
/// Docker's check-update.sh and the standalone start.sh/start.ps1 update checks both read
/// this file. ContentRoot/data resolves to the already-persisted /app/data Docker volume,
/// and to app/data next to the standalone launcher scripts.
/// </summary>
public class AutoUpdateFlagFileService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AutoUpdateFlagFileService> _logger;

    public AutoUpdateFlagFileService(IWebHostEnvironment environment, ILogger<AutoUpdateFlagFileService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public void Write(bool enabled)
    {
        try
        {
            var dataDir = Path.Combine(_environment.ContentRootPath, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "autoupdate.flag"), enabled ? "true" : "false");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write auto-update flag file");
        }
    }
}
