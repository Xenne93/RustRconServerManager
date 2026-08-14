namespace RustRconServerManager.Backend.Models;

/// <summary>
/// Represents a Rust game item that can be given to players
/// </summary>
public class RustItem
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int StackSize { get; set; }
}
