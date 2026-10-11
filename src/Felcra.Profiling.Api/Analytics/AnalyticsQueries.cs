using Felcra.Profiling.Analytics.Growth;
using Felcra.Profiling.Analytics.Lactation;
using Felcra.Profiling.Api.Breeding;
using Felcra.Profiling.Contracts.Analytics;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Analytics;

/// <summary>
/// Growth (Gompertz) and lactation (Ali-Schaeffer) analysis and the herd dashboard figures, used by the
/// analytics endpoints, the reports and the monthly report e-mail.
/// </summary>
internal sealed class AnalyticsQueries(AppDbContext db, BreedingQueries breeding)
{
    /// <summary>Animals this far below the curve for their age are flagged.</summary>
    private const double BelowCurvePercent = -15;

    private sealed record Weighing(Guid AnimalId, DateOnly Date, decimal WeightKg);

    /// <summary>All weights per animal: weighings at the pen plus census weights.</summary>
    private async Task<List<Weighing>> WeighingsAsync(Guid? farmId, CancellationToken ct)
    {
        var measured = await db.Measurements.AsNoTracking()
            .Where(m => m.Type == MeasurementType.Weight && (farmId == null || m.FarmId == farmId))
            .Select(m => new Weighing(m.AnimalId, m.MeasuredOn, m.Value))
            .ToListAsync(ct);
        var census = await (
                from e in db.CensusEntries.AsNoTracking()
                join s in db.CensusSessions on e.CensusSessionId equals s.Id
                where e.WeightKg != null && (farmId == null || e.FarmId == farmId)
                select new Weighing(e.AnimalId, s.StartDate, e.WeightKg!.Value))
            .ToListAsync(ct);
        return [.. measured, .. census];
    }

    public async Task<GrowthAnalysisDto> GrowthAsync(Guid? farmId, CancellationToken ct)
    {
        var animals = await db.Animals.AsNoTracking()
            .Where(a => a.Status == AnimalStatus.Active && (farmId == null || a.FarmId == farmId))
            .Select(a => new { a.Id, a.AnimalNo, a.TagNumber, a.Sex, a.Category, a.BirthDate, a.BirthDateMethod })
            .ToDictionaryAsync(a => a.Id, ct);
        var weighings = (await WeighingsAsync(farmId, ct)).Where(w => animals.ContainsKey(w.AnimalId)).ToList();

        var withAge = weighings
            .Where(w => animals[w.AnimalId].BirthDate is not null)
            .Select(w => (w, Age: w.Date.DayNumber - animals[w.AnimalId].BirthDate!.Value.DayNumber))
            .Where(x => x.Age >= 0)
            .ToList();

        var curves = new List<GrowthCurveDto>();
        var below = new List<GrowthAnimalDto>();
        foreach (var sex in new[] { Sex.Female, Sex.Male })
        {
            var data = withAge.Where(x => animals[x.w.AnimalId].Sex == sex).ToList();
            var fit = GompertzModel.Fit(data.Select(x => new GrowthPoint(x.Age, (double)x.w.WeightKg)).ToList());

            // Each animal's latest weighing against the curve.
            var latest = data.GroupBy(x => x.w.AnimalId).Select(g => g.MaxBy(x => x.w.Date)).ToList();
            var rows = latest.Select(x =>
            {
                var a = animals[x.w.AnimalId];
                double? expected = fit?.Curve.WeightAt(x.Age);
                double? deviation = expected is > 0 ? Math.Round(100 * ((double)x.w.WeightKg - expected.Value) / expected.Value, 1) : null;
                return new GrowthAnimalDto(a.Id, Animal.FormatAnimalNo(a.AnimalNo), a.TagNumber, a.Category,
                    a.BirthDateMethod != AgeEstimationMethod.Recorded, x.Age, x.w.WeightKg, x.w.Date,
                    expected is null ? null : Math.Round(expected.Value, 1), deviation);
            }).OrderBy(r => r.AgeDays).ToList();

            // Very young calves are excluded from flagging: the birth-weight anchor dominates there.
            below.AddRange(rows.Where(r => r.DeviationPercent <= BelowCurvePercent && r.AgeDays >= 90));

            var curve = fit is null
                ? []
                : Enumerable.Range(0, 97).Select(m => m * 30.44)
                    .Select(t => new CurvePointDto(Math.Round(t / 30.44, 1), Math.Round(fit.Curve.WeightAt(t), 1), Math.Round(fit.Curve.DailyGainAt(t), 3)))
                    .ToList();

            curves.Add(new GrowthCurveDto(sex,
                fit is null ? null : new GompertzDto(Math.Round(fit.Curve.A, 1), fit.Curve.B, fit.Curve.K, Math.Round(fit.Curve.BirthWeight, 1),
                    Math.Round(fit.Curve.InflectionAgeDays), Math.Round(fit.Quality.RSquared, 3), Math.Round(fit.Quality.Rmse, 1), fit.Quality.Points),
                fit is null ? $"Not enough weighings with a known age to fit a curve (need at least {GompertzModel.MinimumPoints})." : null,
                curve, rows));
        }

        return new GrowthAnalysisDto(
            curves, below.OrderBy(b => b.DeviationPercent).ToList(), withAge.Count,
            animals.Values.Count(a => a.BirthDate is null), BelowCurvePercent);
    }

    public async Task<LactationAnalysisDto> LactationAsync(Guid? farmId, CancellationToken ct)
    {
        var milk = await db.Measurements.AsNoTracking()
            .Where(m => m.Type == MeasurementType.MilkYield && (farmId == null || m.FarmId == farmId))
            .Select(m => new { m.AnimalId, m.MeasuredOn, m.Value })
            .ToListAsync(ct);
        if (milk.Count == 0)
            return new LactationAnalysisDto(0, 0, null,
                "No milk yields recorded yet. Record daily milk yield for dams in the field app to see lactation curves.", [], []);

        var animalIds = milk.Select(m => m.AnimalId).Distinct().ToList();
        var calvings = (await db.BreedingEvents.AsNoTracking()
                .Where(e => e.Type == BreedingEventType.Calving && animalIds.Contains(e.AnimalId))
                .Select(e => new { e.AnimalId, e.EventDate }).ToListAsync(ct))
            .GroupBy(c => c.AnimalId).ToDictionary(g => g.Key, g => g.Select(c => c.EventDate).OrderBy(d => d).ToList());
        var animals = await db.Animals.AsNoTracking().Where(a => animalIds.Contains(a.Id))
            .Select(a => new { a.Id, a.AnimalNo, a.TagNumber }).ToDictionaryAsync(a => a.Id, ct);

        // Days in milk: from the most recent calving on or before the test day.
        var points = milk.Select(m =>
        {
            var calved = calvings.GetValueOrDefault(m.AnimalId)?.LastOrDefault(d => d <= m.MeasuredOn);
            return (m.AnimalId, Dim: calved is { } c ? m.MeasuredOn.DayNumber - c.DayNumber : (int?)null, Yield: (double)m.Value);
        }).ToList();
        var usable = points.Where(p => p.Dim is >= 1 and <= 400).ToList();

        static AliSchaefferDto? ToDto(AliSchaefferFit? fit) => fit is null ? null : new AliSchaefferDto(
            fit.Curve.A, fit.Curve.B, fit.Curve.C, fit.Curve.D, fit.Curve.E, Math.Round(fit.Quality.RSquared, 3), fit.Quality.Points,
            Math.Round(fit.Curve.Total305), fit.Curve.Peak.Day, Math.Round(fit.Curve.Peak.Yield, 1));

        var herd = AliSchaefferModel.Fit(usable.Select(p => new MilkPoint(p.Dim!.Value, p.Yield)).ToList());
        var perAnimal = usable.GroupBy(p => p.AnimalId).Select(g =>
        {
            var a = animals[g.Key];
            return new LactationAnimalDto(a.Id, Animal.FormatAnimalNo(a.AnimalNo), a.TagNumber, g.Count(),
                ToDto(AliSchaefferModel.Fit(g.Select(p => new MilkPoint(p.Dim!.Value, p.Yield)).ToList())));
        }).OrderBy(a => a.AnimalNo).ToList();

        var curve = herd is null
            ? []
            : Enumerable.Range(1, 305).Where(d => d == 1 || d % 5 == 0)
                .Select(d => new CurvePointDto(d, Math.Round(Math.Max(0, herd.Curve.YieldAt(d)), 2), 0)).ToList();

        return new LactationAnalysisDto(milk.Count, points.Count - usable.Count, ToDto(herd),
            herd is null ? $"Not enough test days linked to a calving to fit a curve (need at least {AliSchaefferModel.MinimumPoints})." : null,
            curve, perAnimal);
    }

    public async Task<DashboardDto> DashboardAsync(Guid? farmId, CancellationToken ct)
    {
        var today = breeding.Today;
        var animals = await db.Animals.AsNoTracking()
            .Where(a => a.Status == AnimalStatus.Active && (farmId == null || a.FarmId == farmId))
            .Select(a => new { a.Id, a.Category, a.TagNumber, a.FarmId })
            .ToListAsync(ct);
        var capacity = await db.Farms.AsNoTracking().Where(f => farmId == null || f.Id == farmId).SumAsync(f => f.Capacity, ct);

        // Latest weight and BCS per animal (pen weighings and census).
        var weighings = await WeighingsAsync(farmId, ct);
        var latestWeight = weighings.GroupBy(w => w.AnimalId).ToDictionary(g => g.Key, g => g.MaxBy(w => w.Date)!);
        var bcsMeasured = await db.Measurements.AsNoTracking()
            .Where(m => m.Type == MeasurementType.BodyCondition && (farmId == null || m.FarmId == farmId))
            .Select(m => new { m.AnimalId, Date = m.MeasuredOn, m.Value }).ToListAsync(ct);
        var bcsCensus = await (from e in db.CensusEntries.AsNoTracking()
                               join s in db.CensusSessions on e.CensusSessionId equals s.Id
                               where e.BodyConditionScore != null && (farmId == null || e.FarmId == farmId)
                               select new { e.AnimalId, Date = s.StartDate, Value = e.BodyConditionScore!.Value }).ToListAsync(ct);
        var latestBcs = bcsMeasured.Concat(bcsCensus).GroupBy(b => b.AnimalId).ToDictionary(g => g.Key, g => g.MaxBy(b => b.Date)!.Value);

        var categories = Enum.GetValues<AnimalCategory>().Select(c =>
        {
            var ids = animals.Where(a => a.Category == c).Select(a => a.Id).ToList();
            var weights = ids.Where(latestWeight.ContainsKey).Select(id => latestWeight[id].WeightKg).ToList();
            var scores = ids.Where(latestBcs.ContainsKey).Select(id => latestBcs[id]).ToList();
            return new CategoryStatDto(c, ids.Count,
                weights.Count == 0 ? null : Math.Round(weights.Average(), 1),
                scores.Count == 0 ? null : Math.Round(scores.Average(), 1));
        }).ToList();
        int Count(AnimalCategory c) => animals.Count(a => a.Category == c);

        var activeIds = animals.Select(a => a.Id).ToHashSet();
        var tagGroups = animals.Where(a => a.TagNumber != null)
            .GroupBy(a => (a.FarmId, Tag: a.TagNumber!.Replace(" ", "").ToUpperInvariant()));

        var summary = await breeding.SummaryAsync(farmId, ct);
        var dams = summary.Sum(s => s.Dams);
        var reminders = await breeding.RemindersAsync(farmId, 14, ct);
        var candidates = await breeding.CullingCandidatesAsync(farmId, ct);

        var deaths = await db.Animals.AsNoTracking().CountAsync(a =>
            a.Status == AnimalStatus.Dead && a.StatusDate >= today.AddDays(-365) && (farmId == null || a.FarmId == farmId), ct);

        // Recording activity per week (Monday start) over the last 12 weeks.
        var from = today.AddDays(-((int)today.DayOfWeek + 6) % 7).AddDays(-7 * 11);
        var measuredDates = await db.Measurements.AsNoTracking()
            .Where(m => m.Type == MeasurementType.Weight && m.MeasuredOn >= from && (farmId == null || m.FarmId == farmId))
            .Select(m => m.MeasuredOn).ToListAsync(ct);
        var treatmentDates = await db.TreatmentRecords.AsNoTracking()
            .Where(t => t.GivenOn >= from && (farmId == null || t.FarmId == farmId)).Select(t => t.GivenOn).ToListAsync(ct);
        var breedingDates = await db.BreedingEvents.AsNoTracking()
            .Where(e => e.EventDate >= from && (farmId == null || e.FarmId == farmId)).Select(e => e.EventDate).ToListAsync(ct);
        var observationDates = await db.Observations.AsNoTracking()
            .Where(o => o.ObservedOn >= from && (farmId == null || o.FarmId == farmId)).Select(o => o.ObservedOn).ToListAsync(ct);
        int InWeek(IEnumerable<DateOnly> dates, DateOnly start) => dates.Count(d => d >= start && d < start.AddDays(7));
        var activity = Enumerable.Range(0, 12).Select(w => from.AddDays(7 * w))
            .Select(start => new WeeklyActivityDto(start, InWeek(measuredDates, start), InWeek(treatmentDates, start),
                InWeek(breedingDates, start), InWeek(observationDates, start)))
            .ToList();

        return new DashboardDto(
            animals.Count,
            capacity,
            new CategoryCounts(Count(AnimalCategory.Bull), Count(AnimalCategory.Dam), Count(AnimalCategory.MaleCalf), Count(AnimalCategory.FemaleCalf)),
            categories,
            animals.Count(a => a.TagNumber == null),
            tagGroups.Where(g => g.Count() > 1).Sum(g => g.Count()),
            latestWeight.Count(kv => activeIds.Contains(kv.Key) && kv.Value.Date >= today.AddDays(-90)),
            // Same basis as the Breeding page (all breeding females), but no rate until any female has a record.
            dams == summary.Sum(s => s.NoRecords) ? null : Math.Round(100m * summary.Sum(s => s.Pregnant) / dams, 1),
            summary.Sum(s => s.NoRecords),
            reminders.Count,
            reminders.Count(r => r.IsOverdueOn(today)),
            candidates.Count,
            treatmentDates.Count(d => d >= today.AddDays(-30)),
            summary.Sum(s => s.CalvesLast12Months),
            deaths,
            animals.Count + deaths == 0 ? null : Math.Round(100m * deaths / (animals.Count + deaths), 1),
            activity);
    }
}
