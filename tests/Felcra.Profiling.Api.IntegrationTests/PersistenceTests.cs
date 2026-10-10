using Felcra.Profiling.Domain.Auditing;
using Felcra.Profiling.Domain.Farms;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Farm_scope_limits_non_hq_users_to_assigned_farms()
    {
        using var scope = factory.Services.CreateScope();
        var other = new Farm { Code = $"T{Guid.NewGuid():N}"[..12], Name = "Other farm" };
        await using (var system = factory.CreateDbContext(scope))
        {
            system.Farms.Add(other);
            await system.SaveChangesAsync();
        }

        Guid pagohId;
        await using (var system = factory.CreateDbContext(scope))
            pagohId = (await system.Farms.SingleAsync(f => f.Code == "PAGOH")).Id;

        await using var manager = factory.CreateDbContext(scope, new TestUser(Guid.NewGuid(), isHqAdmin: false, pagohId));
        var visible = await manager.Farms.Select(f => f.Id).ToListAsync();
        Assert.Equal(new[] { pagohId }, visible);

        await using var hq = factory.CreateDbContext(scope, new TestUser(Guid.NewGuid(), isHqAdmin: true));
        var all = await hq.Farms.Select(f => f.Id).ToListAsync();
        Assert.Contains(pagohId, all);
        Assert.Contains(other.Id, all);
    }

    [Fact]
    public async Task Changes_are_audited_and_deletes_are_soft()
    {
        using var scope = factory.Services.CreateScope();
        var farm = new Farm { Code = $"A{Guid.NewGuid():N}"[..12], Name = "Audit farm" };

        await using (var db = factory.CreateDbContext(scope))
        {
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
        }
        await using (var db = factory.CreateDbContext(scope))
        {
            var loaded = await db.Farms.SingleAsync(f => f.Id == farm.Id);
            loaded.Name = "Audit farm (renamed)";
            await db.SaveChangesAsync();
        }
        await using (var db = factory.CreateDbContext(scope))
        {
            db.Farms.Remove(await db.Farms.SingleAsync(f => f.Id == farm.Id));
            await db.SaveChangesAsync();
        }

        await using var check = factory.CreateDbContext(scope);
        Assert.False(await check.Farms.AnyAsync(f => f.Id == farm.Id));

        var deleted = await check.Farms.IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleAsync(f => f.Id == farm.Id);
        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);

        var actions = await check.AuditLogs
            .Where(a => a.EntityId == farm.Id)
            .OrderBy(a => a.Id)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Equal(new[] { AuditAction.Created, AuditAction.Updated, AuditAction.Deleted }, actions);

        var update = await check.AuditLogs.SingleAsync(a => a.EntityId == farm.Id && a.Action == AuditAction.Updated);
        Assert.Contains("renamed", update.Changes);
    }
}
