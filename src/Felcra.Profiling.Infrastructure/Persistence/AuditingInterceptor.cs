using System.Text.Json;
using System.Text.Json.Serialization;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Domain.Auditing;
using Felcra.Profiling.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Felcra.Profiling.Infrastructure.Persistence;

/// <summary>
/// Stamps created/updated/deleted metadata on <see cref="AuditableEntity"/>s, turns deletes into
/// soft deletes and writes an <see cref="AuditLog"/> row per change in the same transaction.
/// </summary>
public sealed class AuditingInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> StampProperties =
    [
        nameof(AuditableEntity.CreatedAt), nameof(AuditableEntity.CreatedBy),
        nameof(AuditableEntity.UpdatedAt), nameof(AuditableEntity.UpdatedBy),
        nameof(AuditableEntity.DeletedAt), nameof(AuditableEntity.DeletedBy),
        nameof(AuditableEntity.Version),
        // Deletion is recorded as its own audit action.
        nameof(AuditableEntity.IsDeleted),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>().ToList())
        {
            AuditAction action;
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    action = AuditAction.Created;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    var deleted = entry.Property(e => e.IsDeleted);
                    action = deleted.IsModified && deleted.OriginalValue && !entry.Entity.IsDeleted ? AuditAction.Restored : AuditAction.Updated;
                    break;
                case EntityState.Deleted:
                    // Soft delete: update only the delete columns. Marking the whole entity Modified would also
                    // write columns the database owns (e.g. identity columns), which PostgreSQL rejects.
                    entry.State = EntityState.Unchanged;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.DeletedBy = userId;
                    entry.Property(e => e.IsDeleted).IsModified = true;
                    entry.Property(e => e.DeletedAt).IsModified = true;
                    entry.Property(e => e.DeletedBy).IsModified = true;
                    action = AuditAction.Deleted;
                    break;
                default:
                    continue;
            }

            logs.Add(new AuditLog
            {
                Timestamp = now,
                UserId = userId,
                UserName = currentUser.UserName,
                Action = action,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                Changes = action is AuditAction.Deleted or AuditAction.Restored ? null : SerializeChanges(entry, action),
            });
        }

        context.Set<AuditLog>().AddRange(logs);
    }

    private static string? SerializeChanges(EntityEntry entry, AuditAction action)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (StampProperties.Contains(name) || property.Metadata.IsPrimaryKey()) continue;

            if (action == AuditAction.Created)
                changes[name] = new { @new = property.CurrentValue };
            else if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                changes[name] = new { old = property.OriginalValue, @new = property.CurrentValue };
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes, JsonOptions);
    }
}
