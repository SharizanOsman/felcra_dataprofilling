namespace Felcra.Profiling.Domain.Auditing;

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
}

/// <summary>An immutable record of a change to an auditable entity.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditAction Action { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>JSON object of changed properties: { "Name": { "old": ..., "new": ... } }.</summary>
    public string? Changes { get; set; }
}
