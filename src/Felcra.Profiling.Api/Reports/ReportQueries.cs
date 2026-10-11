using Felcra.Profiling.Api.Analytics;
using Felcra.Profiling.Api.Breeding;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Reports;

/// <summary>
/// Gathers the data for each report. Farm scoping comes from the database filters for signed-in users;
/// the monthly e-mail job runs without a user and passes the farm explicitly.
/// </summary>
internal sealed class ReportQueries(AppDbContext db, BreedingQueries breeding, AnalyticsQueries analytics, TimeProvider clock)
{
    public DateOnly Today => breeding.Today;

    private ReportScope Scope(string title, string farmLabel, string? period) => new(title, farmLabel, period, clock.GetUtcNow());

    /// <summary>"All farms" or "PAGOH · FELCRA Berhad Kawasan Pagoh"; null when the farm is not visible to the user.</summary>
    public async Task<string?> FarmLabelAsync(Guid? farmId, CancellationToken ct)
    {
        if (farmId is null) return "All farms";
        var farm = await db.Farms.AsNoTracking().Where(f => f.Id == farmId).Select(f => new { f.Code, f.Name }).SingleOrDefaultAsync(ct);
        return farm is null ? null : $"{farm.Code} · {farm.Name}";
    }

    public async Task<CensusFormData?> CensusFormAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.CensusSessions.AsNoTracking().Include(s => s.Treatments).SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;
        var farm = await db.Farms.AsNoTracking().Include(f => f.Projects).ThenInclude(p => p.Project).SingleAsync(f => f.Id == session.FarmId, ct);

        var entries = await (
                from e in db.CensusEntries.AsNoTracking()
                join a in db.Animals.IgnoreQueryFilters([QueryFilters.SoftDelete]) on e.AnimalId equals a.Id
                where e.CensusSessionId == sessionId
                select new
                {
                    e.LineNo, a.AnimalNo, a.TagNumber, a.FarmId, e.Category, e.EstimatedAgeMonths, e.WeightKg, e.BodyConditionScore,
                    ProjectCode = db.Projects.Where(p => p.Id == a.ProjectId).Select(p => p.Code).FirstOrDefault(),
                    e.Remarks, e.Selected, a.BreedId,
                })
            .ToListAsync(ct);

        // Tags used by more than one animal on the farm (the report's known duplicates).
        var duplicateTags = (await db.Animals.AsNoTracking()
                .Where(a => a.FarmId == session.FarmId && a.TagNumber != null && a.Status == AnimalStatus.Active)
                .Select(a => a.TagNumber!).ToListAsync(ct))
            .GroupBy(Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var rows = entries
            .OrderBy(e => e.LineNo ?? int.MaxValue).ThenBy(e => e.AnimalNo)
            .Select((e, i) => new CensusFormRow(
                e.LineNo ?? i + 1, Animal.FormatAnimalNo(e.AnimalNo), e.TagNumber, e.TagNumber is not null && duplicateTags.Contains(Key(e.TagNumber)),
                e.Category, e.EstimatedAgeMonths, e.WeightKg, e.BodyConditionScore, e.ProjectCode, e.Remarks, e.Selected))
            .ToList();

        var breedIds = entries.Where(e => e.BreedId is not null).Select(e => e.BreedId!.Value).Distinct().ToList();
        var breeds = await db.Breeds.AsNoTracking().Where(b => breedIds.Contains(b.Id)).OrderBy(b => b.Name).Select(b => b.Name).ToListAsync(ct);
        var treatmentIds = session.Treatments.Select(t => t.TreatmentProductId).ToList();
        var treatments = await db.TreatmentProducts.AsNoTracking().Where(t => treatmentIds.Contains(t.Id)).Select(t => new { t.Id, t.Name }).ToListAsync(ct);

        int Count(AnimalCategory c) => entries.Count(e => e.Category == c);
        var recorded = session.RecordedBulls is null && session.RecordedDams is null && session.RecordedMaleCalves is null && session.RecordedFemaleCalves is null
            ? null
            : new CategoryCounts(session.RecordedBulls ?? 0, session.RecordedDams ?? 0, session.RecordedMaleCalves ?? 0, session.RecordedFemaleCalves ?? 0);

        return new CensusFormData(
            Scope("Census and profiling form", $"{farm.Code} · {farm.Name}", ReportText.DateRange(session.StartDate, session.EndDate)),
            farm.Code, farm.Name, farm.Address,
            string.Join(" & ", farm.Projects.Select(p => p.Project?.Code).Where(c => c is not null).Order()) is { Length: > 0 } projects ? projects : "–",
            breeds.Count > 0 ? string.Join(" & ", breeds) : "Not recorded",
            session.Title, session.StartDate, session.EndDate, session.PicName, session.PicPosition, session.PicPhone, session.Notes,
            recorded,
            new CategoryCounts(Count(AnimalCategory.Bull), Count(AnimalCategory.Dam), Count(AnimalCategory.MaleCalf), Count(AnimalCategory.FemaleCalf)),
            treatments.Select(t => t.Name).Order().ToList(),
            rows);
    }

    private static string Key(string tag) => tag.Replace(" ", "").ToUpperInvariant();

    public async Task<TreatmentReportData> TreatmentsAsync(Guid? farmId, DateOnly from, DateOnly to, string farmLabel, CancellationToken ct)
    {
        var rows = await (
                from t in db.TreatmentRecords.AsNoTracking()
                join a in db.Animals.IgnoreQueryFilters([QueryFilters.SoftDelete]) on t.AnimalId equals a.Id
                join p in db.TreatmentProducts.IgnoreQueryFilters() on t.TreatmentProductId equals p.Id
                join f in db.Farms.IgnoreQueryFilters() on t.FarmId equals f.Id
                where t.GivenOn >= @from && t.GivenOn <= to && (farmId == null || t.FarmId == farmId)
                orderby t.GivenOn, f.Code, a.AnimalNo
                select new
                {
                    t.GivenOn, f.Code, a.AnimalNo, a.TagNumber, AnimalCategory = a.Category, Product = p.Name, ProductCategory = p.Category, t.Dose, t.DoseUnit, t.Route,
                    t.BatchNumber, t.WithdrawalUntil, t.NextDueOn,
                    GivenBy = db.Users.Where(u => u.Id == t.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
                })
            .ToListAsync(ct);

        return new TreatmentReportData(
            Scope("Health and treatment report", farmLabel, ReportText.DateRange(from, to)),
            rows.Select(r => new TreatmentRow(
                r.GivenOn, r.Code, Animal.FormatAnimalNo(r.AnimalNo), r.TagNumber, r.AnimalCategory, r.Product, ReportText.Humanize(r.ProductCategory),
                r.Dose is { } d ? $"{d:0.##} {r.DoseUnit}".Trim() : null, r.Route is { } route ? ReportText.Humanize(route) : null,
                r.BatchNumber, r.WithdrawalUntil, r.NextDueOn, r.GivenBy)).ToList());
    }

    public async Task<PerformanceData> PerformanceAsync(Guid? farmId, DateOnly from, DateOnly to, string farmLabel, CancellationToken ct)
    {
        var herd = await analytics.DashboardAsync(farmId, ct);
        var summary = await breeding.SummaryAsync(farmId, ct);
        var growth = await analytics.GrowthAsync(farmId, ct);
        var candidates = await breeding.CullingCandidatesAsync(farmId, ct);

        var measurements = await db.Measurements.AsNoTracking()
            .Where(m => m.MeasuredOn >= from && m.MeasuredOn <= to && (farmId == null || m.FarmId == farmId))
            .GroupBy(m => m.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var activity = new PeriodActivity(
            measurements.FirstOrDefault(m => m.Key == MeasurementType.Weight)?.Count ?? 0,
            measurements.FirstOrDefault(m => m.Key == MeasurementType.BodyCondition)?.Count ?? 0,
            await db.TreatmentRecords.CountAsync(t => t.GivenOn >= from && t.GivenOn <= to && (farmId == null || t.FarmId == farmId), ct),
            await db.BreedingEvents.CountAsync(e => e.EventDate >= from && e.EventDate <= to && (farmId == null || e.FarmId == farmId), ct),
            await db.Observations.CountAsync(o => o.ObservedOn >= from && o.ObservedOn <= to && (farmId == null || o.FarmId == farmId), ct),
            await db.Animals.CountAsync(a => a.BirthEventId != null && a.BirthDate >= from && a.BirthDate <= to && (farmId == null || a.FarmId == farmId), ct));

        var exits = await (
                from a in db.Animals.AsNoTracking()
                join f in db.Farms.IgnoreQueryFilters() on a.FarmId equals f.Id
                where a.Status != AnimalStatus.Active && a.StatusDate >= @from && a.StatusDate <= to && (farmId == null || a.FarmId == farmId)
                orderby a.StatusDate, a.AnimalNo
                select new { a.StatusDate, f.Code, a.AnimalNo, a.TagNumber, a.Category, a.Status, a.StatusReason })
            .ToListAsync(ct);

        return new PerformanceData(
            Scope("Farm performance report", farmLabel, ReportText.DateRange(from, to)),
            herd, summary, growth, activity,
            exits.Select(e => new ExitRow(e.StatusDate, e.Code, Animal.FormatAnimalNo(e.AnimalNo), e.TagNumber, e.Category, e.Status, e.StatusReason)).ToList(),
            candidates.Count);
    }

    public async Task<CullingReportData> CullingAsync(Guid? farmId, string farmLabel, CancellationToken ct) =>
        new(Scope("Culling candidates", farmLabel, $"As of {ReportText.Date(Today)}"), await breeding.CullingCandidatesAsync(farmId, ct));
}
