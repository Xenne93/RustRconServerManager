namespace RustRconServerManager.Shared.PluginVersionCheck
{
    /// <summary>
    /// Plugin version check result
    /// </summary>
    public class PluginVersionCheckResult
    {
        public string PluginName { get; set; } = string.Empty;
        public string InstalledVersion { get; set; } = string.Empty;
        public string? LatestVersion { get; set; }
        public bool IsUpToDate { get; set; }
        public string? PluginUrl { get; set; }
        public PluginSource? Source { get; set; }

        /// <summary>
        /// True if plugin was not found on any source (404 from all sources)
        /// False if there was a temporary error (rate limit, timeout, etc.)
        /// </summary>
        public bool NotFound { get; set; }
    }

    /// <summary>
    /// Plugin source enum
    /// </summary>
    public enum PluginSource
    {
        Unknown = 0,
        Codefling = 1,
        Umod = 2,
        Custom = 3  // Custom/private plugin - no version checking
    }

    /// <summary>
    /// Codefling API response
    /// </summary>
    public class CodeflingPluginResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Umod API response
    /// </summary>
    public class UmodPluginResponse
    {
        public string Name { get; set; } = string.Empty;
        public string Latest_Release_Version { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Batch plugin version check request
    /// </summary>
    public class PluginVersionCheckBatchRequest
    {
        public List<PluginVersionCheckItem> Plugins { get; set; } = new();
    }

    /// <summary>
    /// Single plugin item for batch request
    /// </summary>
    public class PluginVersionCheckItem
    {
        public string PluginName { get; set; } = string.Empty;
        public string InstalledVersion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Batch plugin version check response
    /// </summary>
    public class PluginVersionCheckBatchResponse
    {
        public Dictionary<string, PluginVersionCheckResult?> Results { get; set; } = new();
        public int TotalCount { get; set; }
        public int CheckedCount { get; set; }
        public int CacheHitCount { get; set; }
    }
}
