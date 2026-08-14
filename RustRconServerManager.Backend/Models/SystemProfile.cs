namespace RustRconServerManager.Backend.Models;

public class SystemProfile
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Secret { get; set; }
    public string Hash { get; set; }
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Navigation property to PanelSettings (one-to-one)
    /// </summary>
    public PanelSettings PanelSettings { get; set; }
}