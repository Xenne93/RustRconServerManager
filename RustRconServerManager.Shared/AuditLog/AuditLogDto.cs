namespace RustRconServerManager.Shared.AuditLog;

public class AuditLogDto
{
    public int Id { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLogPagedResultDto
{
    public List<AuditLogDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
