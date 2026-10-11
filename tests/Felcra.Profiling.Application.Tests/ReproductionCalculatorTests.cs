using Felcra.Profiling.Application.Breeding;
using Felcra.Profiling.Domain.Breeding;

namespace Felcra.Profiling.Application.Tests;

public class ReproductionCalculatorTests
{
    private static readonly BreedingOptions Options = new();
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void No_records_is_unknown()
    {
        var status = ReproductionCalculator.Evaluate([], Today, Options);

        Assert.Equal(ReproductiveState.Unknown, status.State);
        Assert.Null(status.DaysOpen);
    }

    [Fact]
    public void Service_without_check_is_served_with_check_due_after_45_days()
    {
        var status = ReproductionCalculator.Evaluate([new EventFact(D("2026-09-01"), BreedingEventType.Insemination)], Today, Options);

        Assert.Equal(ReproductiveState.Served, status.State);
        Assert.Equal(D("2026-10-16"), status.PregnancyCheckDue);
        Assert.Equal(D("2026-09-01").AddDays(285), status.ExpectedCalving);
    }

    [Fact]
    public void Positive_check_makes_her_pregnant_with_calving_due_from_service()
    {
        var status = ReproductionCalculator.Evaluate(
        [
            new EventFact(D("2025-08-01"), BreedingEventType.Calving),
            new EventFact(D("2025-11-01"), BreedingEventType.NaturalService),
            new EventFact(D("2026-01-10"), BreedingEventType.PregnancyCheck, PregnancyResult.Pregnant),
        ], Today, Options);

        Assert.Equal(ReproductiveState.Pregnant, status.State);
        Assert.Equal(D("2025-11-01").AddDays(285), status.ExpectedCalving);
        Assert.Equal(92, status.DaysOpen); // calving to conception
        Assert.Null(status.PregnancyCheckDue);
    }

    [Fact]
    public void Gestation_age_at_check_overrides_the_service_date()
    {
        var status = ReproductionCalculator.Evaluate(
            [new EventFact(D("2026-08-01"), BreedingEventType.PregnancyCheck, PregnancyResult.Pregnant, GestationDays: 100)], Today, Options);

        Assert.Equal(D("2026-08-01").AddDays(-100 + 285), status.ExpectedCalving);
    }

    [Fact]
    public void Calving_starts_a_new_cycle_and_gives_the_interval()
    {
        var status = ReproductionCalculator.Evaluate(
        [
            new EventFact(D("2025-03-01"), BreedingEventType.Calving),
            new EventFact(D("2025-06-01"), BreedingEventType.Insemination),
            new EventFact(D("2025-07-20"), BreedingEventType.PregnancyCheck, PregnancyResult.Pregnant),
            new EventFact(D("2026-03-12"), BreedingEventType.Calving),
        ], Today, Options);

        Assert.Equal(ReproductiveState.Open, status.State);
        Assert.Equal(D("2026-03-12"), status.LastCalving);
        Assert.Equal(376, status.CalvingIntervalDays);
        Assert.Equal(Today.DayNumber - D("2026-03-12").DayNumber, status.DaysOpen);
        Assert.Equal(2, status.CalvingCount);
    }

    [Fact]
    public void Consecutive_empty_checks_are_counted()
    {
        var status = ReproductionCalculator.Evaluate(
        [
            new EventFact(D("2026-01-01"), BreedingEventType.Insemination),
            new EventFact(D("2026-02-20"), BreedingEventType.PregnancyCheck, PregnancyResult.Open),
            new EventFact(D("2026-03-10"), BreedingEventType.Insemination),
            new EventFact(D("2026-05-01"), BreedingEventType.PregnancyCheck, PregnancyResult.Open),
        ], Today, Options);

        Assert.Equal(ReproductiveState.Open, status.State);
        Assert.Equal(2, status.ConsecutiveOpenChecks);
    }

    [Fact]
    public void Culling_reasons_follow_the_pagoh_report_rules()
    {
        var status = ReproductionCalculator.Evaluate(
        [
            new EventFact(D("2025-01-15"), BreedingEventType.Calving),
            new EventFact(D("2025-04-01"), BreedingEventType.Insemination),
            new EventFact(D("2025-05-20"), BreedingEventType.PregnancyCheck, PregnancyResult.Open),
            new EventFact(D("2025-07-01"), BreedingEventType.NaturalService),
            new EventFact(D("2025-08-20"), BreedingEventType.PregnancyCheck, PregnancyResult.Open),
        ], Today, Options);

        var reasons = ReproductionCalculator.CullingReasons(status, ageMonths: 9 * 12 + 2, latestBcs: 1.5m, Today, Options);

        Assert.Equal(4, reasons.Count);
        Assert.Contains(reasons, r => r.StartsWith("Aged 9 years"));
        Assert.Contains(reasons, r => r.StartsWith("Not pregnant 20 months"));
        Assert.Contains(reasons, r => r.StartsWith("Empty at 2"));
        Assert.Contains(reasons, r => r.StartsWith("Low body condition"));
    }

    [Fact]
    public void A_young_pregnant_dam_is_not_a_culling_candidate()
    {
        var status = ReproductionCalculator.Evaluate(
        [
            new EventFact(D("2025-01-15"), BreedingEventType.Calving),
            new EventFact(D("2026-04-01"), BreedingEventType.Insemination),
            new EventFact(D("2026-05-20"), BreedingEventType.PregnancyCheck, PregnancyResult.Pregnant),
        ], Today, Options);

        Assert.Empty(ReproductionCalculator.CullingReasons(status, ageMonths: 48, latestBcs: 3m, Today, Options));
    }
}
