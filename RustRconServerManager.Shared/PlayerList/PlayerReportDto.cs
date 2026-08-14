namespace RustRconServerManager.Shared.PlayerList;

public class PlayerReportDto
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string ReporterId { get; set; }
    public string ReporterName { get; set; }
    public string ReportedId { get; set; }
    public string ReportedName { get; set; }
    public string Subject { get; set; }
    public string? Message { get; set; }
    public string Type { get; set; }
    public string Status { get; set; }
    public string? AdminNotes { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
