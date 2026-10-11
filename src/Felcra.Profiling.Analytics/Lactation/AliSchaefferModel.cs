using Felcra.Profiling.Analytics.Numerics;

namespace Felcra.Profiling.Analytics.Lactation;

/// <summary>Daily milk yield (litres) on a given day in milk.</summary>
public readonly record struct MilkPoint(double DaysInMilk, double YieldLitres);

/// <summary>
/// Ali &amp; Schaeffer (1987) lactation curve:
/// y(t) = a + b·(t/305) + c·(t/305)² + d·ln(305/t) + e·ln²(305/t), t = days in milk.
/// </summary>
public sealed record AliSchaefferCurve(double A, double B, double C, double D, double E)
{
    public const int StandardLength = 305;

    public double YieldAt(double daysInMilk)
    {
        var t = Math.Max(daysInMilk, 1);
        var x = t / StandardLength;
        var l = Math.Log(StandardLength / t);
        return A + B * x + C * x * x + D * l + E * l * l;
    }

    /// <summary>Total over the standard 305-day lactation (negative daily values count as zero).</summary>
    public double Total305 => Enumerable.Range(1, StandardLength).Sum(t => Math.Max(0, YieldAt(t)));

    public (int Day, double Yield) Peak =>
        Enumerable.Range(1, StandardLength).Select(t => (t, YieldAt(t))).MaxBy(p => p.Item2);
}

public sealed record AliSchaefferFit(AliSchaefferCurve Curve, FitQuality Quality);

public static class AliSchaefferModel
{
    /// <summary>Five coefficients need more than five test days to say anything.</summary>
    public const int MinimumPoints = 6;

    public static double[] Basis(double daysInMilk)
    {
        var t = Math.Max(daysInMilk, 1);
        var x = t / AliSchaefferCurve.StandardLength;
        var l = Math.Log(AliSchaefferCurve.StandardLength / t);
        return [1, x, x * x, l, l * l];
    }

    /// <summary>Linear least-squares fit. Null with too few points or test days that cannot separate the terms.</summary>
    public static AliSchaefferFit? Fit(IReadOnlyList<MilkPoint> points)
    {
        var valid = points.Where(p => p.DaysInMilk is >= 1 and <= 400 && p.YieldLitres >= 0).ToList();
        if (valid.Count < MinimumPoints || valid.Select(p => p.DaysInMilk).Distinct().Count() < MinimumPoints) return null;

        var beta = LinearSolver.LeastSquares(valid.Select(p => Basis(p.DaysInMilk)).ToList(), valid.Select(p => p.YieldLitres).ToList());
        if (beta is null || beta.Any(double.IsNaN)) return null;

        var curve = new AliSchaefferCurve(beta[0], beta[1], beta[2], beta[3], beta[4]);
        return new AliSchaefferFit(curve, FitQuality.From(valid.Select(p => p.YieldLitres).ToList(), valid.Select(p => curve.YieldAt(p.DaysInMilk)).ToList()));
    }
}
