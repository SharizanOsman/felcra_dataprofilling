using System.Linq.Expressions;
using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Domain.Common;
using Felcra.Profiling.Domain.MasterData;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>
/// Master data shared by all farms: readable by every signed-in user, maintained by HQ admins.
/// </summary>
internal static class ReferenceDataEndpoints
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapReferenceData<Project, ProjectDto, ProjectRequest>(
            "/api/projects", "Projects",
            p => new ProjectDto(p.Id, p.Code, p.Name, p.PlanReference, p.Description, p.IsActive, p.Version),
            r => new Project { Code = r.Code.Trim(), Name = r.Name.Trim() },
            (r, p) =>
            {
                p.Code = r.Code.Trim();
                p.Name = r.Name.Trim();
                p.PlanReference = r.PlanReference;
                p.Description = r.Description;
                p.IsActive = r.IsActive;
            },
            p => p.Code);

        app.MapReferenceData<Breed, BreedDto, BreedRequest>(
            "/api/breeds", "Breeds",
            b => new BreedDto(b.Id, b.Code, b.Name, b.Description, b.IsActive, b.Version),
            r => new Breed { Code = r.Code.Trim(), Name = r.Name.Trim() },
            (r, b) =>
            {
                b.Code = r.Code.Trim();
                b.Name = r.Name.Trim();
                b.Description = r.Description;
                b.IsActive = r.IsActive;
            },
            b => b.Name);

        app.MapReferenceData<TreatmentProduct, TreatmentProductDto, TreatmentProductRequest>(
            "/api/treatment-products", "Treatment products",
            t => new TreatmentProductDto(t.Id, t.Name, t.Category, t.Unit, t.DefaultDose, t.WithdrawalDays, t.Notes, t.IsActive, t.Version),
            r => new TreatmentProduct { Name = r.Name.Trim() },
            (r, t) =>
            {
                t.Name = r.Name.Trim();
                t.Category = r.Category;
                t.Unit = r.Unit;
                t.DefaultDose = r.DefaultDose;
                t.WithdrawalDays = r.WithdrawalDays;
                t.Notes = r.Notes;
                t.IsActive = r.IsActive;
            },
            t => t.Name);

        app.MapReferenceData<FeedIngredient, FeedIngredientDto, FeedIngredientRequest>(
            "/api/feed-ingredients", "Feed ingredients",
            f => new FeedIngredientDto(f.Id, f.Name, f.Category, f.DryMatterPercent, f.CrudeProteinPercent, f.TdnPercent,
                f.MetabolisableEnergy, f.CalciumPercent, f.PhosphorusPercent, f.PricePerKg, f.IsActive, f.Version),
            r => new FeedIngredient { Name = r.Name.Trim() },
            (r, f) =>
            {
                f.Name = r.Name.Trim();
                f.Category = r.Category;
                f.DryMatterPercent = r.DryMatterPercent;
                f.CrudeProteinPercent = r.CrudeProteinPercent;
                f.TdnPercent = r.TdnPercent;
                f.MetabolisableEnergy = r.MetabolisableEnergy;
                f.CalciumPercent = r.CalciumPercent;
                f.PhosphorusPercent = r.PhosphorusPercent;
                f.PricePerKg = r.PricePerKg;
                f.IsActive = r.IsActive;
            },
            f => f.Name);

        return app;
    }

    /// <summary>
    /// Maps list / get / create / update / delete for a reference-data entity.
    /// <paramref name="create"/> builds the entity with its required members; <paramref name="apply"/> then copies every field.
    /// </summary>
    private static void MapReferenceData<TEntity, TDto, TRequest>(
        this IEndpointRouteBuilder app,
        string path,
        string tag,
        Expression<Func<TEntity, TDto>> toDto,
        Func<TRequest, TEntity> create,
        Action<TRequest, TEntity> apply,
        Expression<Func<TEntity, string>> orderBy)
        where TEntity : AuditableEntity
        where TDto : IHasId
        where TRequest : class, IVersionedRequest
    {
        var group = app.MapGroup(path).WithTags(tag);
        var map = toDto.Compile();

        // Handlers are passed as Delegate variables: the ASP.NET Core route-handler analyzer crashes
        // (AD0001) on inline lambdas whose signatures use generic type parameters.
        Delegate list = async (AppDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Set<TEntity>().AsNoTracking().OrderBy(orderBy).Select(toDto).ToListAsync(ct));

        Delegate get = async Task<Results<Ok<TDto>, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Set<TEntity>().AsNoTracking().Where(e => e.Id == id).Select(toDto).SingleOrDefaultAsync(ct) is { } dto
                ? TypedResults.Ok(dto)
                : TypedResults.NotFound();

        Delegate post = async (TRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var entity = create(request);
            apply(request, entity);
            db.Add(entity);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"{path}/{entity.Id}", map(entity));
        };

        Delegate put = async Task<Results<Ok<TDto>, NotFound>> (Guid id, TRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Set<TEntity>().SingleOrDefaultAsync(e => e.Id == id, ct);
            if (entity is null) return TypedResults.NotFound();

            db.Entry(entity).Property(e => e.Version).OriginalValue = request.Version;
            apply(request, entity);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(map(entity));
        };

        Delegate delete = async Task<Results<NoContent, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Set<TEntity>().SingleOrDefaultAsync(e => e.Id == id, ct);
            if (entity is null) return TypedResults.NotFound();

            db.Remove(entity);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        };

        group.MapGet("/", list);
        group.MapGet("/{id:guid}", get);
        group.MapPost("/", post).RequireAuthorization(Policies.HqAdmin).Validate<TRequest>();
        group.MapPut("/{id:guid}", put).RequireAuthorization(Policies.HqAdmin).Validate<TRequest>();
        group.MapDelete("/{id:guid}", delete).RequireAuthorization(Policies.HqAdmin);
    }
}
