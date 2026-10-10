using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Contracts.Auditing;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

internal static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit-logs", async (
                AppDbContext db,
                CancellationToken ct,
                int page = 1,
                int pageSize = 50,
                string? entityType = null,
                Guid? entityId = null,
                string? userName = null,
                DateTimeOffset? from = null,
                DateTimeOffset? to = null) =>
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 200);

                var query = db.AuditLogs.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
                if (entityId is not null) query = query.Where(a => a.EntityId == entityId);
                if (!string.IsNullOrWhiteSpace(userName)) query = query.Where(a => a.UserName != null && a.UserName.Contains(userName));
                if (from is not null) query = query.Where(a => a.Timestamp >= from);
                if (to is not null) query = query.Where(a => a.Timestamp < to);

                var total = await query.CountAsync(ct);
                var items = await query
                    .OrderByDescending(a => a.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new AuditLogDto(a.Id, a.Timestamp, a.UserName, a.Action.ToString(), a.EntityType, a.EntityId, a.Changes))
                    .ToListAsync(ct);

                return TypedResults.Ok(new PagedResult<AuditLogDto>(items, total));
            })
            .WithTags("Audit log")
            .RequireAuthorization(Policies.HqAdmin);

        return app;
    }
}
