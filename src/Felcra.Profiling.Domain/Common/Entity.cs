namespace Felcra.Profiling.Domain.Common;

/// <summary>
/// Base type for all persisted entities. IDs are UUIDv7 (time-ordered) and may be
/// generated on the client, which lets offline devices create records safely.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>
/// Entity whose creation/modification is stamped and whose changes are written to the audit log.
/// Deletes are converted to soft deletes by the persistence layer.
/// </summary>
public abstract class AuditableEntity : Entity, ISoftDeletable
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    /// <summary>
    /// Optimistic concurrency token (PostgreSQL <c>xmin</c>). Clients send back the version they
    /// edited; a stale version makes the update fail instead of silently overwriting someone else.
    /// </summary>
    public uint Version { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}

/// <summary>
/// Data that belongs to a single farm. Farm managers and field officers only see
/// rows for the farms they are assigned to.
/// </summary>
public interface IFarmScoped
{
    Guid FarmId { get; }
}
