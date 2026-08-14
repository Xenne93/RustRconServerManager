namespace RustRconServerManager.Shared.PresetCommand;

/// <summary>
/// DTO for retrieving preset command information.
/// </summary>
public class PresetCommandDto
{
    public int Id { get; set; }
    public int? RconServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsGlobal { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
