using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RustRconServerManager.Shared.PluginVersionCheck;

namespace RustRconServerManager.Backend.Models
{
    /// <summary>
    /// Cache table for plugin version information
    /// Stores results for 30 minutes to reduce API calls to Umod/Codefling
    /// Server-independent: only caches latest version info from APIs
    /// </summary>
    [Table("PluginVersionCache")]
    public class PluginVersionCache
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string PluginName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? LatestVersion { get; set; }

        [MaxLength(500)]
        public string? PluginUrl { get; set; }

        public PluginSource? Source { get; set; }

        // Timestamp when this entry was cached
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;

        // When this cache entry expires (30 minutes after CachedAt)
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);

        // Umod rate limit info (for monitoring)
        public int? UmodRateLimitRemaining { get; set; }
        public int? UmodRateLimitTotal { get; set; }
    }
}
