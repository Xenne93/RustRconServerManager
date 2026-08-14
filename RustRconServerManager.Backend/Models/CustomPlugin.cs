using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RustRconServerManager.Shared.PluginVersionCheck;

namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Server-specific plugin source configuration
/// Defines where each plugin should be checked for updates (Umod, Codefling, or Custom)
/// Only plugins with a configured source will show update information
/// </summary>
[Table("ServerPluginSources")]
public class ServerPluginSource
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int RustServerId { get; set; }

    [Required]
    [MaxLength(255)]
    public string PluginName { get; set; } = string.Empty;

    /// <summary>
    /// Plugin source: Umod, Codefling, or Custom
    /// Determines which API to check for version updates
    /// </summary>
    [Required]
    public PluginSource Source { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(RustServerId))]
    public RconServer? RconServer { get; set; }
}
