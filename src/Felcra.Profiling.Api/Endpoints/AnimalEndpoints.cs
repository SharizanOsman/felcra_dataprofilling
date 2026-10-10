using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>Animal registry. Farm visibility is enforced by the farm-scope query filter.</summary>
internal static class AnimalEndpoints
{
    public static IEndpointRouteBuilder MapAnimalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/animals").WithTags("Animals");

        group.MapGet("/", ListAnimals);
        group.MapGet("/duplicate-tags", ListDuplicateTags);
        group.MapGet("/{id:guid}", GetAnimal);
        // Field officers may register animals (e.g. new calves); edits and status changes need a manager.
        group.MapPost("/", CreateAnimal).Validate<AnimalRequest>();
        group.MapPut("/{id:guid}", UpdateAnimal).RequireAuthorization(Policies.Manager).Validate<AnimalRequest>();
        group.MapPost("/{id:guid}/status", ChangeStatus).RequireAuthorization(Policies.Manager).Validate<ChangeStatusRequest>();
        group.MapPost("/{id:guid}/retag", Retag).Validate<RetagRequest>();
        group.MapDelete("/{id:guid}", DeleteAnimal).RequireAuthorization(Policies.HqAdmin);

        return app;
    }

    private sealed record AnimalRow(
        Guid Id, int AnimalNo, string? TagNumber, bool HasDuplicateTag, Sex Sex, AnimalCategory Category, string? BreedName,
        DateOnly? BirthDate, AgeEstimationMethod? BirthDateMethod, Guid FarmId, string FarmCode, string? ProjectCode,
        string? HerdGroupName, AnimalStatus Status, decimal? WeightKg, DateOnly? WeighedOn,
        decimal? CensusWeightKg, DateOnly? CensusWeighedOn);

    private static async Task<Ok<PagedResult<AnimalListItemDto>>> ListAnimals(
        AppDbContext db,
        CancellationToken ct,
        Guid? farmId = null,
        string? search = null,
        string? status = nameof(AnimalStatus.Active),
        AnimalCategory? category = null,
        Guid? projectId = null,
        Guid? herdGroupId = null,
        bool duplicatesOnly = false,
        bool untaggedOnly = false,
        string sort = "animalNo",
        int page = 1,
        int pageSize = 50,
        string? ids = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var query = db.Animals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
            query = query.Where(a => idList.Contains(a.Id));
        }
        if (farmId is not null) query = query.Where(a => a.FarmId == farmId);
        // Active animals by default; "all" (or empty) lists every status.
        if (!string.IsNullOrEmpty(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var wanted = Enum.TryParse<AnimalStatus>(status, ignoreCase: true, out var s) ? s : AnimalStatus.Active;
            query = query.Where(a => a.Status == wanted);
        }
        if (category is not null) query = query.Where(a => a.Category == category);
        if (projectId is not null) query = query.Where(a => a.ProjectId == projectId);
        if (herdGroupId is not null) query = query.Where(a => a.HerdGroupId == herdGroupId);
        if (untaggedOnly) query = query.Where(a => a.TagNumber == null);
        if (duplicatesOnly)
            query = query.Where(a => a.TagNumber != null && db.Animals.Any(o =>
                o.Id != a.Id && o.FarmId == a.FarmId && o.TagNumber == a.TagNumber && o.Status == AnimalStatus.Active));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var number = int.TryParse(term.TrimStart('A', 'a').TrimStart('0'), out var n) ? n : -1;
            query = query.Where(a => (a.TagNumber != null && EF.Functions.ILike(a.TagNumber, $"%{term}%")) || a.AnimalNo == number);
        }

        query = sort switch
        {
            "tag" => query.OrderBy(a => a.TagNumber == null).ThenBy(a => a.TagNumber).ThenBy(a => a.AnimalNo),
            "category" => query.OrderBy(a => a.Category).ThenBy(a => a.AnimalNo),
            "age" => query.OrderBy(a => a.BirthDate == null).ThenBy(a => a.BirthDate).ThenBy(a => a.AnimalNo),
            _ => query.OrderBy(a => a.AnimalNo),
        };

        var total = await query.CountAsync(ct);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AnimalRow(
                a.Id,
                a.AnimalNo,
                a.TagNumber,
                a.TagNumber != null && db.Animals.Any(o =>
                    o.Id != a.Id && o.FarmId == a.FarmId && o.TagNumber == a.TagNumber && o.Status == AnimalStatus.Active),
                a.Sex,
                a.Category,
                db.Breeds.Where(b => b.Id == a.BreedId).Select(b => b.Name).FirstOrDefault(),
                a.BirthDate,
                a.BirthDateMethod,
                a.FarmId,
                db.Farms.Where(f => f.Id == a.FarmId).Select(f => f.Code).First(),
                db.Projects.Where(p => p.Id == a.ProjectId).Select(p => p.Code).FirstOrDefault(),
                db.HerdGroups.Where(g => g.Id == a.HerdGroupId).Select(g => g.Name).FirstOrDefault(),
                a.Status,
                db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.Weight)
                    .OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.CreatedAt).Select(m => (decimal?)m.Value).FirstOrDefault(),
                db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.Weight)
                    .OrderByDescending(m => m.MeasuredOn).Select(m => (DateOnly?)m.MeasuredOn).FirstOrDefault(),
                // Weight recorded during a census.
                (from e in db.CensusEntries
                 join s in db.CensusSessions on e.CensusSessionId equals s.Id
                 where e.AnimalId == a.Id && e.WeightKg != null
                 orderby s.StartDate descending
                 select e.WeightKg).FirstOrDefault(),
                (from e in db.CensusEntries
                 join s in db.CensusSessions on e.CensusSessionId equals s.Id
                 where e.AnimalId == a.Id && e.WeightKg != null
                 orderby s.StartDate descending
                 select (DateOnly?)s.StartDate).FirstOrDefault()))
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            // Newest of a weighing at the pen and a census weight.
            var (weight, weighedOn) = r.WeighedOn >= r.CensusWeighedOn || r.CensusWeighedOn is null
                ? (r.WeightKg, r.WeighedOn)
                : (r.CensusWeightKg, r.CensusWeighedOn);
            return new AnimalListItemDto(
                r.Id, Animal.FormatAnimalNo(r.AnimalNo), r.TagNumber, r.HasDuplicateTag, r.Sex, r.Category, r.BreedName,
                r.BirthDate, r.BirthDateMethod, r.FarmId, r.FarmCode, r.ProjectCode, r.HerdGroupName, r.Status,
                weight, weighedOn);
        }).ToList();

        return TypedResults.Ok(new PagedResult<AnimalListItemDto>(items, total));
    }

    private static async Task<Ok<List<DuplicateTagDto>>> ListDuplicateTags(AppDbContext db, CancellationToken ct, Guid? farmId = null)
    {
        var animals = await db.Animals.AsNoTracking()
            .Where(a => a.TagNumber != null && a.Status == AnimalStatus.Active && (farmId == null || a.FarmId == farmId))
            .Select(a => new { a.Id, a.AnimalNo, a.TagNumber, a.FarmId })
            .ToListAsync(ct);
        var farmCodes = await db.Farms.ToDictionaryAsync(f => f.Id, f => f.Code, ct);

        var duplicates = animals
            .GroupBy(a => (a.FarmId, Tag: a.TagNumber!.Replace(" ", "").ToUpperInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateTagDto(
                g.First().TagNumber!, g.Key.FarmId, farmCodes.GetValueOrDefault(g.Key.FarmId, "?"),
                g.Select(a => a.Id).ToList(), g.Select(a => Animal.FormatAnimalNo(a.AnimalNo)).ToList()))
            .OrderBy(d => d.FarmCode).ThenBy(d => d.TagNumber)
            .ToList();
        return TypedResults.Ok(duplicates);
    }

    private static async Task<Results<Ok<AnimalDto>, NotFound>> GetAnimal(Guid id, AppDbContext db, CancellationToken ct)
    {
        var animal = await db.Animals.AsNoTracking().Include(a => a.TagHistory).SingleOrDefaultAsync(a => a.Id == id, ct);
        return animal is null ? TypedResults.NotFound() : TypedResults.Ok(await ToDtoAsync(db, animal, ct));
    }

    private static async Task<Results<Created<AnimalDto>, NotFound, ValidationProblem>> CreateAnimal(
        AnimalRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Farms.AnyAsync(f => f.Id == request.FarmId, ct)) return TypedResults.NotFound();
        if (await ValidateReferencesAsync(db, request, request.FarmId, null, ct) is { } problem) return problem;

        var animal = new Animal { FarmId = request.FarmId };
        Apply(request, animal);
        animal.Retag(request.TagNumber, LocalDate.Today(clock), "Registered");
        db.Animals.Add(animal);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/animals/{animal.Id}", await ToDtoAsync(db, animal, ct));
    }

    private static async Task<Results<Ok<AnimalDto>, NotFound, ValidationProblem>> UpdateAnimal(
        Guid id, AnimalRequest request, AppDbContext db, CancellationToken ct)
    {
        var animal = await db.Animals.Include(a => a.TagHistory).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (animal is null) return TypedResults.NotFound();
        if (await ValidateReferencesAsync(db, request, animal.FarmId, animal.Id, ct) is { } problem) return problem;

        // The farm is fixed after registration (moves are recorded as a transfer) and tags change via /retag.
        db.Entry(animal).Property(a => a.Version).OriginalValue = request.Version;
        Apply(request, animal);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(db, animal, ct));
    }

    private static async Task<Results<Ok<AnimalDto>, NotFound>> ChangeStatus(
        Guid id, ChangeStatusRequest request, AppDbContext db, CancellationToken ct)
    {
        var animal = await db.Animals.Include(a => a.TagHistory).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (animal is null) return TypedResults.NotFound();

        db.Entry(animal).Property(a => a.Version).OriginalValue = request.Version;
        animal.Status = request.Status;
        animal.StatusDate = request.Status == AnimalStatus.Active ? null : request.Date;
        animal.StatusReason = request.Status == AnimalStatus.Active ? null : request.Reason;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(db, animal, ct));
    }

    private static async Task<Results<Ok<AnimalDto>, NotFound>> Retag(Guid id, RetagRequest request, AppDbContext db, CancellationToken ct)
    {
        var animal = await db.Animals.Include(a => a.TagHistory).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (animal is null) return TypedResults.NotFound();

        db.Entry(animal).Property(a => a.Version).OriginalValue = request.Version;
        animal.Retag(request.TagNumber, request.Date, request.Reason);
        // Tag history rows are separate; touch the animal so its version changes too.
        db.Entry(animal).Property(a => a.TagNumber).IsModified = true;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(db, animal, ct));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAnimal(Guid id, AppDbContext db, CancellationToken ct)
    {
        var animal = await db.Animals.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (animal is null) return TypedResults.NotFound();

        // Remove its census entries too so session head counts stay consistent.
        db.CensusEntries.RemoveRange(await db.CensusEntries.Where(e => e.AnimalId == id).ToListAsync(ct));
        db.Animals.Remove(animal);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void Apply(AnimalRequest request, Animal animal)
    {
        animal.Category = request.Category;
        animal.Sex = request.Sex;
        animal.BreedId = request.BreedId;
        animal.ProjectId = request.ProjectId;
        animal.HerdGroupId = request.HerdGroupId;
        animal.BirthDate = request.BirthDate;
        animal.BirthDateMethod = request.BirthDate is null ? null : request.BirthDateMethod;
        animal.DamId = request.DamId;
        animal.SireId = request.SireId;
        animal.Remarks = request.Remarks;
    }

    private static async Task<ValidationProblem?> ValidateReferencesAsync(
        AppDbContext db, AnimalRequest request, Guid farmId, Guid? selfId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ProjectId is { } projectId && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            errors["ProjectId"] = ["Project does not exist."];
        if (request.BreedId is { } breedId && !await db.Breeds.AnyAsync(b => b.Id == breedId, ct))
            errors["BreedId"] = ["Breed does not exist."];
        if (request.HerdGroupId is { } groupId && !await db.HerdGroups.AnyAsync(g => g.Id == groupId && g.FarmId == farmId, ct))
            errors["HerdGroupId"] = ["Group does not belong to this farm."];
        if (request.DamId is { } damId && (damId == selfId || !await db.Animals.AnyAsync(a => a.Id == damId && a.Sex == Sex.Female, ct)))
            errors["DamId"] = ["Dam must be another female animal."];
        if (request.SireId is { } sireId && (sireId == selfId || !await db.Animals.AnyAsync(a => a.Id == sireId && a.Sex == Sex.Male, ct)))
            errors["SireId"] = ["Sire must be another male animal."];
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static async Task<AnimalDto> ToDtoAsync(AppDbContext db, Animal animal, CancellationToken ct)
    {
        var farmCode = await db.Farms.Where(f => f.Id == animal.FarmId).Select(f => f.Code).FirstAsync(ct);
        var parents = await db.Animals.AsNoTracking()
            .Where(a => a.Id == animal.DamId || a.Id == animal.SireId)
            .Select(a => new { a.Id, a.AnimalNo, a.TagNumber })
            .ToListAsync(ct);
        string? Label(Guid? parentId) => parents.FirstOrDefault(p => p.Id == parentId) is { } p
            ? $"{Animal.FormatAnimalNo(p.AnimalNo)}{(p.TagNumber is null ? "" : $" (tag {p.TagNumber})")}"
            : null;

        var duplicateIds = animal.TagNumber is null
            ? []
            : await db.Animals.AsNoTracking()
                .Where(o => o.Id != animal.Id && o.FarmId == animal.FarmId && o.TagNumber == animal.TagNumber && o.Status == AnimalStatus.Active)
                .Select(o => o.Id)
                .ToListAsync(ct);

        var census = await (
                from e in db.CensusEntries.AsNoTracking()
                join s in db.CensusSessions on e.CensusSessionId equals s.Id
                where e.AnimalId == animal.Id
                orderby s.StartDate descending
                select new AnimalCensusHistoryDto(s.Id, s.Title, s.StartDate, e.Category, e.EstimatedAgeMonths, e.WeightKg,
                    e.BodyConditionScore, e.Selected, e.Remarks))
            .ToListAsync(ct);

        return new AnimalDto(
            animal.Id, Animal.FormatAnimalNo(animal.AnimalNo), animal.TagNumber, animal.Sex, animal.Category,
            animal.FarmId, farmCode, animal.ProjectId, animal.HerdGroupId, animal.BreedId,
            animal.BirthDate, animal.BirthDateMethod,
            animal.DamId, Label(animal.DamId), animal.SireId, Label(animal.SireId),
            animal.Status, animal.StatusDate, animal.StatusReason, animal.Remarks,
            duplicateIds,
            animal.TagHistory.OrderByDescending(t => t.AssignedOn)
                .Select(t => new AnimalTagDto(t.TagNumber, t.AssignedOn, t.RemovedOn, t.Reason)).ToList(),
            census,
            animal.Version);
    }
}
