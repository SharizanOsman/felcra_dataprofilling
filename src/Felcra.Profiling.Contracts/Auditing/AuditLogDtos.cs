namespace Felcra.Profiling.Contracts.Auditing;

public sealed record AuditLogDto(
    long Id,
    DateTimeOffset Timestamp,
    string? UserName,
    string Action,
    string EntityType,
    Guid EntityId,
    string? Changes);
