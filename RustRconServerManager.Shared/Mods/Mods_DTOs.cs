using RustRconServerManager.Shared.PluginVersionCheck;

namespace RustRconServerManager.Shared.Mods
{
    /// <summary>
    /// Response DTO for mods/plugins list
    /// </summary>
    public class Mods_ResponseDTO
    {
        public string Message { get; set; } = string.Empty;
        public List<PluginDTO> Plugins { get; set; } = new();
        public int TotalCount { get; set; }
        public int LoadedCount { get; set; }
        public int FailedCount { get; set; }
        public int UnloadedCount { get; set; }
    }

    /// <summary>
    /// Represents a plugin/mod
    /// </summary>
    public class PluginDTO
    {
        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string LoadTime { get; set; } = string.Empty;
        public string MemoryUsage { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public PluginStatus Status { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// True if this is a custom/private plugin (skips version checking)
        /// </summary>
        public bool IsCustom { get; set; }

        /// <summary>
        /// Plugin source: Umod, Codefling, Custom, or null if not configured
        /// </summary>
        public PluginSource? Source { get; set; }
    }

    /// <summary>
    /// Plugin status enum
    /// </summary>
    public enum PluginStatus
    {
        Loaded,
        Failed,
        Unloaded
    }

    /// <summary>
    /// Request DTO for plugin reload
    /// </summary>
    public class Mods_ReloadPluginDTO
    {
        public string PluginName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for plugin unload
    /// </summary>
    public class Mods_UnloadPluginDTO
    {
        public string PluginName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for setting plugin source (Umod, Codefling, Custom, or null to remove)
    /// </summary>
    public class Mods_SetPluginSourceDTO
    {
        public string PluginName { get; set; } = string.Empty;

        /// <summary>
        /// Plugin source: Umod, Codefling, Custom, or null to remove source configuration
        /// </summary>
        public PluginSource? Source { get; set; }
    }
}
