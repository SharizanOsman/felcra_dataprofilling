using Felcra.Profiling.Api.Breeding;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>
/// Data for the field app's offline cache. Records made offline are sent through the normal record
/// endpoints, which already ignore repeated sends of the same record.
/// </summary>
internal static class SyncEndpoints
{
    /// <summary>
    /// Changes are re-sent from a little before the device's cursor: a transaction stamped before the
    /// previous pull may only have committed after it. Clients upsert, so overlap is harmless.
    /// </summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sync/pull", Pull).WithTags("Sync");
        return app;
    }

    private sealed record Row(
        Guid Id, int AnimalNo, string? TagNumber, Sex Sex, AnimalCategory Category, DateOnly? BirthDate,
        AgeEstimationMethod? BirthDateMethod, Guid FarmId, string FarmCode, string? ProjectCode, AnimalStatus Status, bool IsDeleted,
        decimal? WeightKg, DateOnly? WeighedOn, decimal? CensusWeightKg, DateOnly? CensusWeighedOn,
        decimal? Bcs, DateOnly? BcsOn, decimal? CensusBcs, DateOnly? CensusBcsOn);

    /// <summary>Tasks sent to the phone: due within this many days (plus overdue).</summary>
    private const int ReminderDays = 14;

    private static async Task<Ok<SyncPullDto>> Pull(
        AppDbContext db, BreedingQueries breedingQueries, TimeProvider clock, CancellationToken ct, DateTimeOffset? since = null)
    {
        var serverTime = clock.GetUtcNow();
        var changedSince = since - Overlap;

        // Farm scope still applies; soft-deleted animals are included so devices can drop them.
        var animals = db.Animals.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking();
        var allMeasurements = db.Measurements.IgnoreQueryFilters([QueryFilters.SoftDelete]);
        var allEntries = db.CensusEntries.IgnoreQueryFilters([QueryFilters.SoftDelete]);
        var allBreeding = db.BreedingEvents.IgnoreQueryFilters([QueryFilters.SoftDelete]);
        animals = changedSince is { } s
            ? animals.Where(a =>
                (a.UpdatedAt ?? a.CreatedAt) > s
                || a.DeletedAt > s
                // A new or removed weighing / census entry changes the animal's latest values.
                || allMeasurements.Any(m => m.AnimalId == a.Id && ((m.UpdatedAt ?? m.CreatedAt) > s || m.DeletedAt > s))
                || allEntries.Any(e => e.AnimalId == a.Id && ((e.UpdatedAt ?? e.CreatedAt) > s || e.DeletedAt > s))
                // A breeding event changes the reproduction status.
                || allBreeding.Any(e => e.AnimalId == a.Id && ((e.UpdatedAt ?? e.CreatedAt) > s || e.DeletedAt > s)))
            : animals.Where(a => !a.IsDeleted);

        var rows = await animals
            .OrderBy(a => a.AnimalNo)
            .Select(a => new Row(
                a.Id, a.AnimalNo, a.TagNumber, a.Sex, a.Category, a.BirthDate, a.BirthDateMethod, a.FarmId,
                db.Farms.Where(f => f.Id == a.FarmId).Select(f => f.Code).First(),
                db.Projects.Where(p => p.Id == a.ProjectId).Select(p => p.Code).FirstOrDefault(),
                a.Status,
                a.IsDeleted,
                db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.Weight)
                    .OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.CreatedAt).Select(m => (decimal?)m.Value).FirstOrDefault(),
                db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.Weight)
                    .OrderByDescending(m => m.MeasuredOn).Select(m => (DateOnly?)m.MeasuredOn).FirstOrDefault(),
                (from e in db.CensusEntries
                 join cs in db.CensusSessions on e.CensusSessionId equals cs.Id
                 where e.AnimalId == a.Id && e.WeightKg != null
                 orderby cs.StartDate descending
                 select e.WeightKg).FirstOrDefault(),
                (from e in db.CensusEntries
                 join cs in db.CensusSessions on e.CensusSessionId equals cs.Id
                 where e.AnimalId == a.Id && e.WeightKg != null
                 orderby cs.StartDate descending
                 select (DateOnly?)cs.StartDate).FirstOrDefault(),
                db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.BodyCondition)
                    .OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.CreatedAt).Select(m => (decimal?)m.Value).FirstOrDefault(),
                db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.BodyCondition)
                    .OrderByDescending(m => m.MeasuredOn).Select(m => (DateOnly?)m.MeasuredOn).FirstOrDefault(),
                (from e in db.CensusEntries
                 join cs in db.CensusSessions on e.CensusSessionId equals cs.Id
                 where e.AnimalId == a.Id && e.BodyConditionScore != null
                 orderby cs.StartDate descending
                 select e.BodyConditionScore).FirstOrDefault(),
                (from e in db.CensusEntries
                 join cs in db.CensusSessions on e.CensusSessionId equals cs.Id
                 where e.AnimalId == a.Id && e.BodyConditionScore != null
                 orderby cs.StartDate descending
                 select (DateOnly?)cs.StartDate).FirstOrDefault()))
            .ToListAsync(ct);

        var removed = rows.Where(r => r.IsDeleted).Select(r => r.Id).ToList();
        var females = rows.Where(r => !r.IsDeleted && r.Sex == Sex.Female).Select(r => r.Id).ToList();
        var breeding = await breedingQueries.EventsByAnimalAsync(females, ct);
        var changed = rows.Where(r => !r.IsDeleted).Select(r =>
        {
            var (weight, weighedOn) = Newest(r.WeightKg, r.WeighedOn, r.CensusWeightKg, r.CensusWeighedOn);
            var (bcs, bcsOn) = Newest(r.Bcs, r.BcsOn, r.CensusBcs, r.CensusBcsOn);
            var reproduction = r.Sex == Sex.Female ? breedingQueries.Evaluate(breeding.GetValueOrDefault(r.Id) ?? []) : null;
            return new SyncAnimalDto(
                r.Id, Animal.FormatAnimalNo(r.AnimalNo), r.TagNumber, r.Sex, r.Category, r.BirthDate, r.BirthDateMethod,
                r.FarmId, r.FarmCode, r.ProjectCode, r.Status, weight, weighedOn, bcs, bcsOn,
                reproduction?.State, reproduction?.State is ReproductiveState.Pregnant or ReproductiveState.Served ? reproduction.ExpectedCalving : null);
        }).ToList();

        var products = await db.TreatmentProducts.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name)
            .Select(t => new TreatmentProductDto(t.Id, t.Name, t.Category, t.Unit, t.DefaultDose, t.WithdrawalDays, t.Notes, t.IsActive, t.Version))
            .ToListAsync(ct);
        var sessions = await db.CensusSessions.AsNoTracking().Include(s => s.Treatments).Where(s => !s.IsClosed)
            .OrderByDescending(s => s.StartDate).ToListAsync(ct);

        return TypedResults.Ok(new SyncPullDto(
            serverTime, since is null, changed, removed, products, await CensusEndpoints.ToDtosAsync(db, sessions, ct),
            await breedingQueries.RemindersAsync(null, ReminderDays, ct),
            await db.FeedIngredients.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.Name)
                .Select(f => new FeedIngredientDto(f.Id, f.Name, f.Category, f.DryMatterPercent, f.CrudeProteinPercent, f.TdnPercent,
                    f.MetabolisableEnergy, f.CalciumPercent, f.PhosphorusPercent, f.PricePerKg, f.IsActive, f.Version))
                .ToListAsync(ct)));
    }

    /// <summary>The more recent of a value recorded at the pen and one recorded in a census.</summary>
    private static (decimal? Value, DateOnly? On) Newest(decimal? value, DateOnly? on, decimal? censusValue, DateOnly? censusOn) =>
        censusOn is null || (on is not null && on >= censusOn) ? (value, on) : (censusValue, censusOn);
}
