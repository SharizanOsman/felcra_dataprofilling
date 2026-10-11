using Felcra.Profiling.Analytics.Growth;
using Felcra.Profiling.Analytics.Lactation;
using Felcra.Profiling.Analytics.Numerics;
using Felcra.Profiling.Analytics.Ration;

namespace Felcra.Profiling.Analytics.Tests;

public class GompertzTests
{
    private static readonly GompertzCurve Truth = new(A: 480, B: Math.Log(480.0 / 30), K: 0.0025);

    [Fact]
    public void Recovers_a_known_curve_from_noisy_weights()
    {
        var random = new Random(42);
        var points = Enumerable.Range(0, 60)
            .Select(i => 30.0 + i * 40)
            .Select(age => new GrowthPoint(age, Truth.WeightAt(age) * (1 + (random.NextDouble() - 0.5) * 0.06)))
            .ToList();

        var fit = GompertzModel.Fit(points)!;

        Assert.InRange(fit.Curve.A, 455, 505);
        Assert.InRange(fit.Curve.K, 0.0022, 0.0028);
        Assert.True(fit.Quality.RSquared > 0.98, $"R² {fit.Quality.RSquared}");
    }

    [Fact]
    public void Daily_gain_matches_the_slope_of_the_curve()
    {
        var numeric = (Truth.WeightAt(301) - Truth.WeightAt(299)) / 2;

        Assert.Equal(numeric, Truth.DailyGainAt(300), 4);
    }

    [Fact]
    public void Too_few_weighings_give_no_curve() =>
        Assert.Null(GompertzModel.Fit([new(100, 90), new(200, 140), new(300, 180)]));
}

public class AliSchaefferTests
{
    private static readonly AliSchaefferCurve Truth = new(A: 18, B: -12, C: 2, D: -1.5, E: -0.4);

    [Fact]
    public void Recovers_a_known_lactation_curve()
    {
        var points = Enumerable.Range(1, 30).Select(i => i * 10.0).Select(t => new MilkPoint(t, Truth.YieldAt(t))).ToList();

        var fit = AliSchaefferModel.Fit(points)!;

        Assert.Equal(Truth.A, fit.Curve.A, 3);
        Assert.Equal(Truth.E, fit.Curve.E, 3);
        Assert.Equal(Truth.Total305, fit.Curve.Total305, 0);
        Assert.True(fit.Quality.RSquared > 0.9999);
    }

    [Fact]
    public void Peak_comes_early_in_lactation()
    {
        var (day, yield) = Truth.Peak;

        Assert.InRange(day, 20, 90);
        Assert.True(yield > Truth.YieldAt(250));
    }

    [Fact]
    public void Needs_at_least_six_test_days() =>
        Assert.Null(AliSchaefferModel.Fit([new(10, 8), new(30, 9), new(60, 8.5), new(90, 8), new(120, 7)]));
}

public class LinearProgramTests
{
    [Fact]
    public void Solves_a_textbook_minimisation()
    {
        // min 2x + 3y  s.t.  x + y ≥ 4,  x + 3y ≥ 6,  x ≤ 3  →  x = 3, y = 1, cost 9
        var result = LinearProgram.Minimize([2, 3],
        [
            new([1, 1], ConstraintKind.GreaterOrEqual, 4, "a"),
            new([1, 3], ConstraintKind.GreaterOrEqual, 6, "b"),
            new([1, 0], ConstraintKind.LessOrEqual, 3, "c"),
        ]);

        Assert.Equal(LpStatus.Optimal, result.Status);
        Assert.Equal(3, result.X[0], 6);
        Assert.Equal(1, result.X[1], 6);
        Assert.Equal(9, result.Objective, 6);
    }

    [Fact]
    public void Handles_equalities_and_negative_right_hand_sides()
    {
        // min x + y  s.t.  x + y = 10,  -x ≤ -6 (x ≥ 6)  →  cost 10 with x ≥ 6
        var result = LinearProgram.Minimize([1, 1],
        [
            new([1, 1], ConstraintKind.Equal, 10, "sum"),
            new([-1, 0], ConstraintKind.LessOrEqual, -6, "x min"),
        ]);

        Assert.Equal(LpStatus.Optimal, result.Status);
        Assert.Equal(10, result.Objective, 6);
        Assert.True(result.X[0] >= 6 - 1e-9);
    }

    [Fact]
    public void Reports_infeasible_problems() =>
        Assert.Equal(LpStatus.Infeasible, LinearProgram.Minimize([1],
        [
            new([1], ConstraintKind.GreaterOrEqual, 5, "a"),
            new([1], ConstraintKind.LessOrEqual, 3, "b"),
        ]).Status);

    [Fact]
    public void Reports_unbounded_problems() =>
        Assert.Equal(LpStatus.Unbounded, LinearProgram.Minimize([-1], [new([1], ConstraintKind.GreaterOrEqual, 1, "a")]).Status);
}

public class RationTests
{
    private static RationIngredient Feed(string name, bool forage, double dm, double cp, double tdn, double ca, double p, double price, double? max = null) =>
        new(Guid.NewGuid(), name, forage, dm, cp, tdn, null, ca, p, price, max);

    private static readonly RationIngredient Napier = Feed("Napier grass", true, 20, 9, 55, 0.45, 0.30, 0.10);
    private static readonly RationIngredient Pkc = Feed("Palm kernel cake", false, 90, 16, 72, 0.25, 0.55, 0.70);
    private static readonly RationIngredient Soybean = Feed("Soybean meal", false, 89, 48, 82, 0.30, 0.70, 2.80);
    private static readonly RationIngredient Limestone = Feed("Limestone", false, 99, 0, 0, 38, 0, 0.50);

    [Fact]
    public void Two_ingredient_ration_matches_the_hand_calculation()
    {
        // 8 kg DM at ≥ 12% CP from Napier (9%) and PKC (16%):
        // napier share s with 9s + 16(1−s) = 12 → s = 4/7. PKC is dearer per kg DM, so CP is exactly 12%.
        var targets = new RationTargets(8, 12, null, null, null, null, null, null, null);

        var result = RationFormulator.Formulate([Napier, Pkc], targets);

        Assert.True(result.Feasible);
        var napierDm = result.Lines.Single(l => l.Name == "Napier grass").DryMatterKg;
        Assert.Equal(8 * 4.0 / 7, napierDm, 3);
        Assert.Equal(12, result.Nutrients.Single(n => n.Nutrient == "Crude protein %").Achieved, 3);
        Assert.Equal(napierDm / 0.2 * 0.10 + (8 - napierDm) / 0.9 * 0.70, result.CostPerDay, 3);
    }

    [Fact]
    public void Growing_preset_is_met_at_least_cost_with_minerals()
    {
        var preset = RationPresets.All(250).Single(p => p.Key == "growing");

        var result = RationFormulator.Formulate([Napier, Pkc, Soybean, Limestone], preset.Targets);

        Assert.True(result.Feasible, result.Problem);
        Assert.Equal(preset.Targets.DryMatterIntakeKg, result.Lines.Sum(l => l.DryMatterKg), 4);
        Assert.All(result.Nutrients.Where(n => n.Minimum is not null), n => Assert.True(n.Achieved >= n.Minimum - 1e-6, n.Nutrient));
        Assert.True(result.Lines.Where(l => l.Name == "Napier grass").Sum(l => l.PercentOfDryMatter) >= 40 - 1e-6);
    }

    [Fact]
    public void Inclusion_limits_are_respected()
    {
        var cappedPkc = Pkc with { MaxInclusionPercent = 30 };
        var targets = new RationTargets(8, 11, null, null, null, null, null, null, null);

        var result = RationFormulator.Formulate([Napier, cappedPkc, Soybean], targets);

        Assert.True(result.Feasible);
        Assert.True(result.Lines.Single(l => l.Name == "Palm kernel cake").PercentOfDryMatter <= 30 + 1e-6);
    }

    [Fact]
    public void Impossible_targets_explain_what_is_missing()
    {
        var targets = new RationTargets(8, 20, null, null, null, null, null, null, null);

        var result = RationFormulator.Formulate([Napier, Pkc], targets);

        Assert.False(result.Feasible);
        Assert.Contains("crude protein", result.Problem);
    }

    [Fact]
    public void Ingredients_without_price_are_left_out_with_a_warning()
    {
        var unpriced = Napier with { Name = "Free grass", PricePerKg = null };

        var result = RationFormulator.Formulate([unpriced, Napier, Pkc], new RationTargets(8, 10, null, null, null, null, null, null, null));

        Assert.Contains(result.Warnings, w => w.Contains("Free grass"));
        Assert.DoesNotContain(result.Lines, l => l.Name == "Free grass");
    }
}
