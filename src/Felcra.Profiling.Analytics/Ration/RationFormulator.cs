using Felcra.Profiling.Analytics.Numerics;

namespace Felcra.Profiling.Analytics.Ration;

/// <summary>A feed ingredient's composition (dry-matter basis) and price per kg as fed.</summary>
public sealed record RationIngredient(
    Guid Id,
    string Name,
    bool IsForage,
    double? DryMatterPercent,
    double? CrudeProteinPercent,
    double? TdnPercent,
    double? MetabolisableEnergy,
    double? CalciumPercent,
    double? PhosphorusPercent,
    double? PricePerKg,
    double? MaxInclusionPercent = null);

/// <summary>Daily targets for one animal. Nutrient levels are percentages of the ration's dry matter.</summary>
public sealed record RationTargets(
    double DryMatterIntakeKg,
    double CrudeProteinMinPercent,
    double? CrudeProteinMaxPercent,
    double? TdnMinPercent,
    double? MetabolisableEnergyMin,
    double? CalciumMinPercent,
    double? CalciumMaxPercent,
    double? PhosphorusMinPercent,
    double? ForageMinPercent);

public sealed record RationLine(Guid IngredientId, string Name, double AsFedKg, double DryMatterKg, double PercentOfDryMatter, double Cost);

/// <summary>Nutrient level actually reached by the ration (% of DM, or MJ/kg DM for energy).</summary>
public sealed record NutrientLevel(string Nutrient, double Achieved, double? Minimum, double? Maximum);

public sealed record RationResult(
    bool Feasible,
    IReadOnlyList<RationLine> Lines,
    double CostPerDay,
    IReadOnlyList<NutrientLevel> Nutrients,
    IReadOnlyList<string> Warnings,
    string? Problem);

/// <summary>
/// Least-cost ration: chooses kg (as fed) of each ingredient per animal per day to meet the dry-matter
/// intake and nutrient targets at the lowest cost, by linear programming.
/// </summary>
public static class RationFormulator
{
    public static RationResult Formulate(IReadOnlyList<RationIngredient> candidates, RationTargets targets)
    {
        var warnings = new List<string>();
        var usable = new List<RationIngredient>();
        foreach (var i in candidates)
        {
            if (i.PricePerKg is null) warnings.Add(T("{0} left out: no price.", i.Name));
            else if (i.DryMatterPercent is null or <= 0) warnings.Add(T("{0} left out: no dry matter %.", i.Name));
            else usable.Add(i);
        }
        if (usable.Count == 0) return Fail(T("No ingredients with a price and dry matter %."), warnings);

        foreach (var (label, missing) in new[]
                 {
                     ("crude protein", usable.Where(i => i.CrudeProteinPercent is null)),
                     ("TDN", targets.TdnMinPercent is null ? [] : usable.Where(i => i.TdnPercent is null)),
                     ("ME", targets.MetabolisableEnergyMin is null ? [] : usable.Where(i => i.MetabolisableEnergy is null)),
                     ("calcium", usable.Where(i => i.CalciumPercent is null)),
                     ("phosphorus", usable.Where(i => i.PhosphorusPercent is null)),
                 })
        {
            var names = missing.Select(i => i.Name).ToList();
            if (names.Count > 0) warnings.Add(T("No {0} value for {1}; counted as 0.", T(label), string.Join(", ", names)));
        }

        var dmi = targets.DryMatterIntakeKg;
        // Dry matter supplied per kg as fed, and per-kg-DM nutrient values.
        var dm = usable.Select(i => i.DryMatterPercent!.Value / 100).ToArray();
        double[] Per(Func<RationIngredient, double?> nutrient) => usable.Select((i, k) => dm[k] * (nutrient(i) ?? 0)).ToArray();

        var constraints = new List<LinearConstraint>
        {
            new(dm, ConstraintKind.Equal, dmi, "dry matter intake"),
            new(Per(i => i.CrudeProteinPercent), ConstraintKind.GreaterOrEqual, dmi * targets.CrudeProteinMinPercent, "crude protein minimum"),
        };
        if (targets.CrudeProteinMaxPercent is { } cpMax)
            constraints.Add(new(Per(i => i.CrudeProteinPercent), ConstraintKind.LessOrEqual, dmi * cpMax, "crude protein maximum"));
        if (targets.TdnMinPercent is { } tdn)
            constraints.Add(new(Per(i => i.TdnPercent), ConstraintKind.GreaterOrEqual, dmi * tdn, "TDN (energy) minimum"));
        if (targets.MetabolisableEnergyMin is { } me)
            constraints.Add(new(Per(i => i.MetabolisableEnergy), ConstraintKind.GreaterOrEqual, dmi * me, "ME (energy) minimum"));
        if (targets.CalciumMinPercent is { } caMin)
            constraints.Add(new(Per(i => i.CalciumPercent), ConstraintKind.GreaterOrEqual, dmi * caMin, "calcium minimum"));
        if (targets.CalciumMaxPercent is { } caMax)
            constraints.Add(new(Per(i => i.CalciumPercent), ConstraintKind.LessOrEqual, dmi * caMax, "calcium maximum"));
        if (targets.PhosphorusMinPercent is { } pMin)
            constraints.Add(new(Per(i => i.PhosphorusPercent), ConstraintKind.GreaterOrEqual, dmi * pMin, "phosphorus minimum"));
        if (targets.ForageMinPercent is { } forage)
            constraints.Add(new(usable.Select((i, k) => i.IsForage ? dm[k] : 0).ToArray(), ConstraintKind.GreaterOrEqual,
                dmi * forage / 100, "forage minimum"));
        for (var k = 0; k < usable.Count; k++)
        {
            if (usable[k].MaxInclusionPercent is not { } max) continue;
            var row = new double[usable.Count];
            row[k] = dm[k];
            constraints.Add(new(row, ConstraintKind.LessOrEqual, dmi * max / 100, $"{usable[k].Name} maximum inclusion"));
        }

        var result = LinearProgram.Minimize(usable.Select(i => i.PricePerKg!.Value).ToArray(), constraints);
        if (result.Status != LpStatus.Optimal)
            return Fail(DiagnoseInfeasible(usable, targets, dm), warnings);

        var lines = usable.Select((i, k) => (i, k))
            .Where(x => result.X[x.k] > 1e-6)
            .Select(x => new RationLine(x.i.Id, x.i.Name, result.X[x.k], result.X[x.k] * dm[x.k], 100 * result.X[x.k] * dm[x.k] / dmi,
                result.X[x.k] * x.i.PricePerKg!.Value))
            .OrderByDescending(l => l.DryMatterKg)
            .ToList();

        double Level(Func<RationIngredient, double?> nutrient) =>
            usable.Select((i, k) => result.X[k] * dm[k] * (nutrient(i) ?? 0)).Sum() / dmi;

        var nutrients = new List<NutrientLevel>
        {
            new(T("Crude protein %"), Level(i => i.CrudeProteinPercent), targets.CrudeProteinMinPercent, targets.CrudeProteinMaxPercent),
        };
        if (targets.TdnMinPercent is not null || usable.All(i => i.TdnPercent is not null))
            nutrients.Add(new(T("TDN %"), Level(i => i.TdnPercent), targets.TdnMinPercent, null));
        if (targets.MetabolisableEnergyMin is not null || usable.All(i => i.MetabolisableEnergy is not null))
            nutrients.Add(new(T("ME MJ/kg DM"), Level(i => i.MetabolisableEnergy), targets.MetabolisableEnergyMin, null));
        nutrients.Add(new(T("Calcium %"), Level(i => i.CalciumPercent), targets.CalciumMinPercent, targets.CalciumMaxPercent));
        nutrients.Add(new(T("Phosphorus %"), Level(i => i.PhosphorusPercent), targets.PhosphorusMinPercent, null));

        return new RationResult(true, lines, result.Objective, nutrients, warnings, null);
    }

    /// <summary>Points at the target that the available ingredients cannot reach, to help the user fix it.</summary>
    private static string DiagnoseInfeasible(List<RationIngredient> usable, RationTargets targets, double[] dm)
    {
        var reasons = new List<string>();
        void Check(string nutrient, double? minimum, Func<RationIngredient, double?> value)
        {
            if (minimum is { } min && usable.Max(i => value(i) ?? 0) < min)
                reasons.Add(T("no ingredient reaches the {0} target of {1:0.##}", T(nutrient), min));
        }
        Check("crude protein", targets.CrudeProteinMinPercent, i => i.CrudeProteinPercent);
        Check("TDN", targets.TdnMinPercent, i => i.TdnPercent);
        Check("ME", targets.MetabolisableEnergyMin, i => i.MetabolisableEnergy);
        Check("calcium", targets.CalciumMinPercent, i => i.CalciumPercent);
        Check("phosphorus", targets.PhosphorusMinPercent, i => i.PhosphorusPercent);
        if (targets.ForageMinPercent is > 0 && !usable.Any(i => i.IsForage)) reasons.Add(T("no forage ingredient is selected"));

        return reasons.Count > 0
            ? T("No ration meets all targets: {0}. Add suitable ingredients or relax the targets.", string.Join("; ", reasons))
            : T("No ration meets all targets at once with these ingredients. Add ingredients (e.g. a protein feed or mineral) or relax a target or inclusion limit.");
    }

    private static RationResult Fail(string problem, List<string> warnings) => new(false, [], 0, [], warnings, problem);
}

/// <summary>
/// Starting targets for typical tropical beef cattle classes. They are guidance only, to be confirmed by
/// a FELCRA / MARDI nutritionist for local feeds and breeds.
/// </summary>
public static class RationPresets
{
    public sealed record Preset(string Key, string Name, double DmiPercentOfBodyWeight, RationTargets Targets);

    public static IReadOnlyList<Preset> All(double bodyWeightKg) =>
    [
        new("maintenance", T("Maintenance (dry dam / mature bull)"), 2.0, Targets(bodyWeightKg, 2.0, cp: 7, tdn: 50, caMin: 0.20, pMin: 0.15, forage: 60)),
        new("lactating", T("Late pregnancy / lactating dam"), 2.5, Targets(bodyWeightKg, 2.5, cp: 10.5, tdn: 58, caMin: 0.30, pMin: 0.20, forage: 50)),
        new("growing", T("Growing (≈0.5 kg/day)"), 2.5, Targets(bodyWeightKg, 2.5, cp: 12, tdn: 62, caMin: 0.40, pMin: 0.22, forage: 40)),
        new("finishing", T("Finishing (≈1 kg/day)"), 2.4,
 Targets(bodyWeightKg, 2.4, cp: 12, tdn: 70, caMin: 0.40, pMin: 0.25, forage: 15)),
    ];

    private static RationTargets Targets(double bodyWeight, double dmiPercent, double cp, double tdn, double caMin, double pMin, double forage) =>
        new(Math.Round(bodyWeight * dmiPercent / 100, 2), cp, null, tdn, null, caMin, 1.0, pMin, forage);
}
