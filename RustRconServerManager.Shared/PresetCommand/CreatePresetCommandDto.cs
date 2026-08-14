namespace RustRconServerManager.Shared.PresetCommand;

/// <summary>
/// DTO for creating or updating a preset command.
/// </summary>
public class CreatePresetCommandDto
{
    public int? RconServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsGlobal { get; set; }
    public int SortOrder { get; set; }
}
