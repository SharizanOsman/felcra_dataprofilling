using Felcra.Profiling.Domain.Breeding;

namespace Felcra.Profiling.Application.Breeding;

/// <summary>
/// Thresholds for reproduction status, reminders and culling. Defaults follow common beef-cattle practice
/// and the Pagoh report; FELCRA can tune them in configuration (section "Breeding").
/// </summary>
public sealed class BreedingOptions
{
    public const string SectionName = "Breeding";

    /// <summary>Average gestation length. Brahman crosses run slightly longer (≈ 285–292 days).</summary>
    public int GestationDays { get; set; } = 285;

    /// <summary>Days after service when a pregnancy check is due.</summary>
    public int PregnancyCheckAfterDays { get; set; } = 45;

    /// <summary>A dam not pregnant this long after calving is a culling candidate (Pagoh report: 12–18 months).</summary>
    public int CullOpenDays { get; set; } = 365;

    /// <summary>Dams this old are culling candidates (Pagoh report: 8 years and above).</summary>
    public int CullAgeYears { get; set; } = 8;

    /// <summary>Consecutive "open" pregnancy checks that make a dam a culling candidate.</summary>
    public int CullConsecutiveOpenChecks { get; set; } = 2;

    /// <summary>
    /// BCS below this is flagged. FELCRA has not confirmed its BCS scale (URS open question 1):
    /// 2.0 suits a 1–5 scale; use 4 for a 1–9 scale.
    /// </summary>
    public decimal LowBcs { get; set; } = 2.0m;
}

/// <summary>The minimum the calculator needs to know about one event.</summary>
public sealed record EventFact(DateOnly Date, BreedingEventType Type, PregnancyResult? Result = null, int? GestationDays = null);

public sealed record ReproductionStatus(
    ReproductiveState State,
    DateOnly? LastCalving,
    int? CalvingIntervalDays,
    DateOnly? LastService,
    DateOnly? ExpectedCalving,
    DateOnly? PregnancyCheckDue,
    int? DaysOpen,
    int ConsecutiveOpenChecks,
    int CalvingCount);

public static class ReproductionCalculator
{
    public static ReproductionStatus Evaluate(IEnumerable<EventFact> events, DateOnly today, BreedingOptions options)
    {
        var all = events.Where(e => e.Type != BreedingEventType.BullSoundnessExam).OrderBy(e => e.Date).ToList();
        var calvings = all.Where(e => e.Type == BreedingEventType.Calving).Select(e => e.Date).ToList();
        DateOnly? lastCalving = calvings.Count > 0 ? calvings[^1] : null;
        int? interval = calvings.Count > 1 ? calvings[^1].DayNumber - calvings[^2].DayNumber : null;

        // The current reproductive cycle starts at the last calving or abortion.
        var cycleStart = all.LastOrDefault(e => e.Type is BreedingEventType.Calving or BreedingEventType.Abortion)?.Date;
        var cycle = cycleStart is { } start ? all.Where(e => e.Date > start).ToList() : all;

        var lastService = cycle.LastOrDefault(e => e.Type is BreedingEventType.Insemination or BreedingEventType.NaturalService);
        var checks = cycle.Where(e => e.Type == BreedingEventType.PregnancyCheck).ToList();
        var lastCheck = checks.LastOrDefault();
        var openStreak = checks.AsEnumerable().Reverse().TakeWhile(c => c.Result == PregnancyResult.Open).Count();

        var state = ReproductiveState.Unknown;
        DateOnly? expected = null;
        DateOnly? checkDue = null;
        DateOnly? conception = null;

        if (lastCheck is not null && (lastService is null || lastCheck.Date >= lastService.Date) && lastCheck.Result != PregnancyResult.Uncertain)
        {
            if (lastCheck.Result == PregnancyResult.Pregnant)
            {
                state = ReproductiveState.Pregnant;
                conception = lastCheck.GestationDays is { } days
                    ? lastCheck.Date.AddDays(-days)
                    : lastService?.Date;
                expected = conception?.AddDays(options.GestationDays);
            }
            else
            {
                state = ReproductiveState.Open;
            }
        }
        else if (lastService is not null)
        {
            state = ReproductiveState.Served;
            expected = lastService.Date.AddDays(options.GestationDays);
            checkDue = lastService.Date.AddDays(options.PregnancyCheckAfterDays);
        }
        else if (cycleStart is not null)
        {
            state = ReproductiveState.Open;
        }

        int? daysOpen = lastCalving is { } calved
            ? (conception ?? today).DayNumber - calved.DayNumber
            : null;

        return new ReproductionStatus(state, lastCalving, interval, lastService?.Date, expected, checkDue, daysOpen, openStreak, calvings.Count);
    }

    /// <summary>Reasons a dam should be considered for culling (empty when none).</summary>
    public static List<string> CullingReasons(ReproductionStatus status, int? ageMonths, decimal? latestBcs, DateOnly today, BreedingOptions options)
    {
        var reasons = new List<string>();
        if (ageMonths is { } months && months >= options.CullAgeYears * 12)
            reasons.Add($"Aged {months / 12} years (≥ {options.CullAgeYears})");
        if (status.State != ReproductiveState.Pregnant && status.LastCalving is { } calved
            && today.DayNumber - calved.DayNumber > options.CullOpenDays)
            reasons.Add($"Not pregnant {WholeMonths(calved, today)} months after last calving");
        if (status.ConsecutiveOpenChecks >= options.CullConsecutiveOpenChecks)
            reasons.Add($"Empty at {status.ConsecutiveOpenChecks} pregnancy checks in a row");
        if (latestBcs is { } bcs && bcs < options.LowBcs)
            reasons.Add($"Low body condition (BCS {bcs:0.#})");
        return reasons;
    }

    /// <summary>Complete calendar months between two dates.</summary>
    public static int WholeMonths(DateOnly from, DateOnly to) =>
        Math.Max(0, (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0));
}
