using System.Linq.Expressions;
using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Farms;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>
/// Farms and their herd groups. Visibility is enforced by the farm-scope query filter: managers and
/// officers only ever see (and therefore can only reach) their assigned farms.
/// </summary>
internal static class FarmEndpoints
{
    private static readonly Expression<Func<Farm, FarmDto>> ToDto = f => new FarmDto(
        f.Id, f.Code, f.Name, f.Address, f.State, f.AreaHectares, f.Capacity, f.IsActive,
        f.Projects.Select(p => p.ProjectId).ToList(), f.Version, f.Latitude, f.Longitude);

    private static readonly Expression<Func<HerdGroup, HerdGroupDto>> ToGroupDto = g => new HerdGroupDto(
        g.Id, g.FarmId, g.Name, g.Description, g.IsActive, g.Version);

    private static readonly Func<Farm, FarmDto> MapFarm = ToDto.Compile();
    private static readonly Func<HerdGroup, HerdGroupDto> MapGroup = ToGroupDto.Compile();

    public static IEndpointRouteBuilder MapFarmEndpoints(this IEndpointRouteBuilder app)
    {
        var farms = app.MapGroup("/api/farms").WithTags("Farms");

        farms.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Farms.AsNoTracking().OrderBy(f => f.Name).Select(ToDto).ToListAsync(ct)));

        // Farms with their location and herd size, for the map on the Farms page.
        farms.MapGet("/map", async (AppDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Farms.AsNoTracking().OrderBy(f => f.Name)
                .Select(f => new FarmMapPointDto(f.Id, f.Code, f.Name, f.State, f.Latitude, f.Longitude,
                    db.Animals.Count(a => a.FarmId == f.Id && a.Status == AnimalStatus.Active), f.Capacity, f.IsActive))
                .ToListAsync(ct)));
        farms.MapGet("/{id:guid}", async Task<Results<Ok<FarmDto>, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Farms.AsNoTracking().Where(f => f.Id == id).Select(ToDto).SingleOrDefaultAsync(ct) is { } farm
                ? TypedResults.Ok(farm)
                : TypedResults.NotFound());

        farms.MapPost("/", CreateFarm).RequireAuthorization(Policies.HqAdmin).Validate<FarmRequest>();
        farms.MapPut("/{id:guid}", UpdateFarm).RequireAuthorization(Policies.HqAdmin).Validate<FarmRequest>();
        farms.MapDelete("/{id:guid}", DeleteFarm).RequireAuthorization(Policies.HqAdmin);

        var groups = farms.MapGroup("/{farmId:guid}/groups").WithTags("Herd groups");

        groups.MapGet("/", async Task<Results<Ok<List<HerdGroupDto>>, NotFound>> (Guid farmId, AppDbContext db, CancellationToken ct) =>
            await CanAccessFarmAsync(db, farmId, ct)
                ? TypedResults.Ok(await db.HerdGroups.AsNoTracking().Where(g => g.FarmId == farmId).OrderBy(g => g.Name).Select(ToGroupDto).ToListAsync(ct))
                : TypedResults.NotFound());

        groups.MapPost("/", CreateGroup).RequireAuthorization(Policies.Manager).Validate<HerdGroupRequest>();
        groups.MapPut("/{id:guid}", UpdateGroup).RequireAuthorization(Policies.Manager).Validate<HerdGroupRequest>();
        groups.MapDelete("/{id:guid}", DeleteGroup).RequireAuthorization(Policies.Manager);

        return app;
    }

    private static async Task<Results<Created<FarmDto>, ValidationProblem>> CreateFarm(
        FarmRequest request, AppDbContext db, CancellationToken ct)
    {
        if (await ValidateProjectsAsync(db, request.ProjectIds, ct) is { } problem) return problem;

        var farm = new Farm { Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim() };
        Apply(request, farm);
        farm.Projects = request.ProjectIds.Distinct().Select(p => new FarmProject { FarmId = farm.Id, ProjectId = p }).ToList();
        db.Farms.Add(farm);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/farms/{farm.Id}", MapFarm(farm));
    }

    private static async Task<Results<Ok<FarmDto>, NotFound, ValidationProblem>> UpdateFarm(
        Guid id, FarmRequest request, AppDbContext db, CancellationToken ct)
    {
        var farm = await db.Farms.Include(f => f.Projects).SingleOrDefaultAsync(f => f.Id == id, ct);
        if (farm is null) return TypedResults.NotFound();
        if (await ValidateProjectsAsync(db, request.ProjectIds, ct) is { } problem) return problem;

        db.Entry(farm).Property(f => f.Version).OriginalValue = request.Version;
        Apply(request, farm);

        var wanted = request.ProjectIds.Distinct().ToList();
        db.FarmProjects.RemoveRange(farm.Projects.Where(p => !wanted.Contains(p.ProjectId)).ToList());
        db.FarmProjects.AddRange(wanted
            .Where(p => farm.Projects.All(fp => fp.ProjectId != p))
            .Select(p => new FarmProject { FarmId = farm.Id, ProjectId = p }));

        // Project links are a separate table, so touch the farm row to bump its version as well.
        db.Entry(farm).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MapFarm(farm));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteFarm(Guid id, AppDbContext db, CancellationToken ct)
    {
        var farm = await db.Farms.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (farm is null) return TypedResults.NotFound();

        db.Farms.Remove(farm);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<HerdGroupDto>, NotFound>> CreateGroup(
        Guid farmId, HerdGroupRequest request, AppDbContext db, CancellationToken ct)
    {
        if (!await CanAccessFarmAsync(db, farmId, ct)) return TypedResults.NotFound();

        var group = new HerdGroup { FarmId = farmId, Name = request.Name.Trim() };
        ApplyGroup(request, group);
        db.HerdGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/farms/{farmId}/groups/{group.Id}", MapGroup(group));
    }

    private static async Task<Results<Ok<HerdGroupDto>, NotFound>> UpdateGroup(
        Guid farmId, Guid id, HerdGroupRequest request, AppDbContext db, CancellationToken ct)
    {
        var group = await db.HerdGroups.SingleOrDefaultAsync(g => g.Id == id && g.FarmId == farmId, ct);
        if (group is null) return TypedResults.NotFound();

        db.Entry(group).Property(g => g.Version).OriginalValue = request.Version;
        ApplyGroup(request, group);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MapGroup(group));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGroup(Guid farmId, Guid id, AppDbContext db, CancellationToken ct)
    {
        var group = await db.HerdGroups.SingleOrDefaultAsync(g => g.Id == id && g.FarmId == farmId, ct);
        if (group is null) return TypedResults.NotFound();

        db.HerdGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void Apply(FarmRequest request, Farm farm)
    {
        farm.Code = request.Code.Trim().ToUpperInvariant();
        farm.Name = request.Name.Trim();
        farm.Address = request.Address;
        farm.State = request.State;
        farm.AreaHectares = request.AreaHectares;
        farm.Capacity = request.Capacity;
        farm.Latitude = request.Latitude;
        farm.Longitude = request.Longitude;
        farm.IsActive = request.IsActive;
    }

    private static void ApplyGroup(HerdGroupRequest request, HerdGroup group)
    {
        group.Name = request.Name.Trim();
        group.Description = request.Description;
        group.IsActive = request.IsActive;
    }

    /// <summary>True when the farm exists and is visible to the current user (farm-scope filter).</summary>
    private static Task<bool> CanAccessFarmAsync(AppDbContext db, Guid farmId, CancellationToken ct) =>
        db.Farms.AnyAsync(f => f.Id == farmId, ct);

    private static async Task<ValidationProblem?> ValidateProjectsAsync(AppDbContext db, List<Guid> projectIds, CancellationToken ct)
    {
        var ids = projectIds.Distinct().ToList();
        var known = await db.Projects.CountAsync(p => ids.Contains(p.Id), ct);
        return known == ids.Count
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["ProjectIds"] = ["One or more projects do not exist."] });
    }
}
