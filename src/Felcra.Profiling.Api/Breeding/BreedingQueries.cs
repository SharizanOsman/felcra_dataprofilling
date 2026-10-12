using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Application.Breeding;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Felcra.Profiling.Api.Breeding;

/// <summary>
/// Reproduction status, culling candidates, herd figures and reminders, computed from breeding events
/// with <see cref="ReproductionCalculator"/>. Farm scope comes from the DbContext's query filters.
/// </summary>
internal sealed class BreedingQueries(AppDbContext db, IOptions<BreedingOptions> options, TimeProvider clock)
{
    private readonly BreedingOptions _options = options.Value;

    public DateOnly Today => LocalDate.Today(clock);

    /// <summary>An active female in the breeding herd: a dam, or a heifer with breeding records.</summary>
    public sealed record Dam(Guid Id, int AnimalNo, string? TagNumber, Guid FarmId, string FarmCode, DateOnly? BirthDate,
        decimal? LatestBcs, ReproductionStatus Status);

    public ReproductionStatus Evaluate(IEnumerable<EventFact> events) => ReproductionCalculator.Evaluate(events, Today, _options);

    public List<string> CullingReasons(ReproductionStatus status, DateOnly? birthDate, decimal? latestBcs) =>
        ReproductionCalculator.CullingReasons(status, birthDate is { } b ? ReproductionCalculator.WholeMonths(b, Today) : null, latestBcs, Today, _options);

    public async Task<Dictionary<Guid, List<EventFact>>> EventsByAnimalAsync(ICollection<Guid> animalIds, CancellationToken ct) =>
        (await db.BreedingEvents.AsNoTracking()
            .Where(e => animalIds.Contains(e.AnimalId))
            .Select(e => new { e.AnimalId, e.EventDate, e.Type, e.PregnancyResult, e.GestationDays })
            .ToListAsync(ct))
        .GroupBy(e => e.AnimalId)
        .ToDictionary(g => g.Key, g => g.Select(e => new EventFact(e.EventDate, e.Type, e.PregnancyResult, e.GestationDays)).ToList());

    public async Task<List<Dam>> DamsAsync(Guid? farmId, CancellationToken ct)
    {
        var rows = await db.Animals.AsNoTracking()
            .Where(a => a.Status == AnimalStatus.Active && a.Sex == Sex.Female && (farmId == null || a.FarmId == farmId))
            .Where(a => a.Category == AnimalCategory.Dam || db.BreedingEvents.Any(e => e.AnimalId == a.Id))
            .Select(a => new
            {
                a.Id, a.AnimalNo, a.TagNumber, a.FarmId, a.BirthDate,
                FarmCode = db.Farms.Where(f => f.Id == a.FarmId).Select(f => f.Code).First(),
                Bcs = db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.BodyCondition)
                    .OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.CreatedAt).Select(m => (decimal?)m.Value).FirstOrDefault(),
                BcsOn = db.Measurements.Where(m => m.AnimalId == a.Id && m.Type == MeasurementType.BodyCondition)
                    .OrderByDescending(m => m.MeasuredOn).Select(m => (DateOnly?)m.MeasuredOn).FirstOrDefault(),
                CensusBcs = (from e in db.CensusEntries
                             join s in db.CensusSessions on e.CensusSessionId equals s.Id
                             where e.AnimalId == a.Id && e.BodyConditionScore != null
                             orderby s.StartDate descending
                             select new { e.BodyConditionScore, s.StartDate }).FirstOrDefault(),
            })
            .OrderBy(a => a.AnimalNo)
            .ToListAsync(ct);

        var events = await EventsByAnimalAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r =>
        {
            var bcs = r.CensusBcs is { } c && (r.BcsOn is null || c.StartDate > r.BcsOn) ? c.BodyConditionScore : r.Bcs;
            return new Dam(r.Id, r.AnimalNo, r.TagNumber, r.FarmId, r.FarmCode, r.BirthDate, bcs, Evaluate(events.GetValueOrDefault(r.Id) ?? []));
        }).ToList();
    }

    public async Task<List<CullingCandidateDto>> CullingCandidatesAsync(Guid? farmId, CancellationToken ct) =>
        (await DamsAsync(farmId, ct))
            .Select(d => (Dam: d, Reasons: CullingReasons(d.Status, d.BirthDate, d.LatestBcs)))
            .Where(x => x.Reasons.Count > 0)
            .Select(x => new CullingCandidateDto(
                x.Dam.Id, Animal.FormatAnimalNo(x.Dam.AnimalNo), x.Dam.TagNumber, x.Dam.FarmCode,
                x.Dam.BirthDate is { } b ? ReproductionCalculator.WholeMonths(b, Today) : null,
                x.Dam.Status.State, x.Dam.Status.LastCalving, x.Dam.LatestBcs, x.Reasons))
            .OrderByDescending(c => c.Reasons.Count).ThenBy(c => c.AnimalNo)
            .ToList();

    public async Task<List<HerdBreedingSummaryDto>> SummaryAsync(Guid? farmId, CancellationToken ct)
    {
        var dams = await DamsAsync(farmId, ct);
        var yearAgo = Today.AddYears(-1);
        var recent = await db.BreedingEvents.AsNoTracking()
            .Where(e => e.EventDate > yearAgo && (e.Type == BreedingEventType.Calving || e.Type == BreedingEventType.Abortion)
                        && (farmId == null || e.FarmId == farmId))
            .Select(e => new { e.FarmId, e.Type, e.Id })
            .ToListAsync(ct);
        var calvingIds = recent.Where(e => e.Type == BreedingEventType.Calving).Select(e => e.Id).ToList();
        var calvesByFarm = await db.Animals.AsNoTracking()
            .Where(a => a.BirthEventId != null && calvingIds.Contains(a.BirthEventId.Value))
            .GroupBy(a => a.FarmId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var farms = await db.Farms.AsNoTracking().Where(f => farmId == null || f.Id == farmId).OrderBy(f => f.Code).ToListAsync(ct);

        return farms.Select(f =>
        {
            var herd = dams.Where(d => d.FarmId == f.Id).ToList();
            int Count(ReproductiveState s) => herd.Count(d => d.Status.State == s);
            var intervals = herd.Where(d => d.Status.CalvingIntervalDays is not null).Select(d => d.Status.CalvingIntervalDays!.Value).ToList();
            var calves = calvesByFarm.GetValueOrDefault(f.Id);
            return new HerdBreedingSummaryDto(
                f.Id, f.Code, herd.Count, Count(ReproductiveState.Pregnant), Count(ReproductiveState.Served), Count(ReproductiveState.Open),
                Count(ReproductiveState.Unknown),
                herd.Count == 0 ? null : Math.Round(100m * Count(ReproductiveState.Pregnant) / herd.Count, 1),
                intervals.Count == 0 ? null : (int)Math.Round(intervals.Average()),
                calves,
                herd.Count == 0 ? null : Math.Round(100m * calves / herd.Count, 1),
                recent.Count(e => e.FarmId == f.Id && e.Type == BreedingEventType.Abortion));
        }).ToList();
    }

    /// <summary>Tasks due up to <paramref name="days"/> ahead, plus overdue ones from the last 60 days.</summary>
    public async Task<List<ReminderDto>> RemindersAsync(Guid? farmId, int days, CancellationToken ct)
    {
        var today = Today;
        var horizon = today.AddDays(days);
        var oldest = today.AddDays(-60);
        var reminders = new List<ReminderDto>();

        foreach (var dam in await DamsAsync(farmId, ct))
        {
            var no = Animal.FormatAnimalNo(dam.AnimalNo);
            var s = dam.Status;
            if (s.State == ReproductiveState.Served && s.PregnancyCheckDue is { } check && check <= horizon && check >= oldest)
                reminders.Add(new(ReminderType.PregnancyCheckDue, dam.Id, no, dam.TagNumber, dam.FarmId, dam.FarmCode, check,
                    T("Pregnancy check due"), T("Served {0:dd MMM yyyy}", s.LastService)));
            if (s.State == ReproductiveState.Pregnant && s.ExpectedCalving is { } calving && calving <= horizon && calving >= oldest)
                reminders.Add(new(ReminderType.CalvingDue, dam.Id, no, dam.TagNumber, dam.FarmId, dam.FarmCode, calving,
                    T("Calving due"), T("Watch for signs of calving; record the calf when born.")));
            if (dam.LatestBcs is { } bcs && bcs < _options.LowBcs)
                reminders.Add(new(ReminderType.LowBodyCondition, dam.Id, no, dam.TagNumber, dam.FarmId, dam.FarmCode, today,
                    T("Low body condition (BCS {0:0.#})", bcs), T("Check feed and health; consider supplements.")));
        }

        // Boosters / next doses that have not been given yet (no later record of the same product).
        var products = db.TreatmentProducts.IgnoreQueryFilters([QueryFilters.SoftDelete]);
        var due = await (
                from t in db.TreatmentRecords.AsNoTracking()
                join a in db.Animals on t.AnimalId equals a.Id
                join p in products on t.TreatmentProductId equals p.Id
                where t.NextDueOn != null && t.NextDueOn <= horizon && t.NextDueOn >= oldest
                      && a.Status == AnimalStatus.Active && (farmId == null || a.FarmId == farmId)
                      && !db.TreatmentRecords.Any(l => l.AnimalId == t.AnimalId && l.TreatmentProductId == t.TreatmentProductId && l.GivenOn > t.GivenOn)
                select new
                {
                    a.Id, a.AnimalNo, a.TagNumber, a.FarmId, t.NextDueOn, Product = p.Name, t.GivenOn,
                    FarmCode = db.Farms.Where(f => f.Id == a.FarmId).Select(f => f.Code).First(),
                })
            .ToListAsync(ct);
        reminders.AddRange(due.Select(d => new ReminderDto(ReminderType.TreatmentDue, d.Id, Animal.FormatAnimalNo(d.AnimalNo), d.TagNumber,
            d.FarmId, d.FarmCode, d.NextDueOn!.Value, T("{0} due", d.Product), T("Last given {0:dd MMM yyyy}", d.GivenOn))));

        return reminders.OrderBy(r => r.DueDate).ThenBy(r => r.AnimalNo).ToList();
    }
}
