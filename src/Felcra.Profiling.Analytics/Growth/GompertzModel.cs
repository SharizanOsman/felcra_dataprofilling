using Felcra.Profiling.Analytics.Numerics;

namespace Felcra.Profiling.Analytics.Growth;

/// <summary>Age (days) and body weight (kg) of one weighing.</summary>
public readonly record struct GrowthPoint(double AgeDays, double WeightKg);

/// <summary>
/// Gompertz growth curve W(t) = A · exp(−b · exp(−k·t)), t = age in days.
/// A is mature weight (kg), b sets the starting weight (W(0) = A·e^−b), k is the maturing rate per day.
/// </summary>
public sealed record GompertzCurve(double A, double B, double K)
{
    public double WeightAt(double ageDays) => A * Math.Exp(-B * Math.Exp(-K * ageDays));

    /// <summary>Daily gain (kg/day) at an age: dW/dt = W · b · k · e^(−k·t).</summary>
    public double DailyGainAt(double ageDays) => WeightAt(ageDays) * B * K * Math.Exp(-K * ageDays);

    public double BirthWeight => WeightAt(0);

    /// <summary>Age of fastest growth (inflection point), ln(b)/k.</summary>
    public double InflectionAgeDays => B > 1 ? Math.Log(B) / K : 0;
}

public sealed record GompertzFit(GompertzCurve Curve, FitQuality Quality);

public static class GompertzModel
{
    /// <summary>Fewest weighings that can determine the three parameters with any reliability.</summary>
    public const int MinimumPoints = 5;

    /// <summary>
    /// Fits the curve by Levenberg–Marquardt least squares. A typical birth weight anchors the start of
    /// the curve, which matters for herds weighed only once (cross-sectional data, as in the Pagoh census).
    /// Returns null when there are too few points or the fit does not converge to a plausible curve.
    /// </summary>
    /// <param name="birthWeightKg">Anchor at age 0, weighted like one observation per 20 points. Null = no anchor.</param>
    public static GompertzFit? Fit(IReadOnlyList<GrowthPoint> points, double? birthWeightKg = 30, int maxIterations = 200)
    {
        if (points.Count < MinimumPoints) return null;

        var data = points.Select(p => (p.AgeDays, p.WeightKg, Weight: 1.0)).ToList();
        if (birthWeightKg is { } w0) data.Add((0, w0, Math.Max(1, points.Count / 20.0)));

        // Starting values: mature weight a little above the heaviest animal, maturing rate typical of cattle.
        var a = Math.Max(points.Max(p => p.WeightKg) * 1.2, 100);
        var start = birthWeightKg ?? Math.Max(5, points.Min(p => p.WeightKg) * 0.5);
        var p0 = new[] { a, Math.Log(a / start), 0.003 };
        var lambda = 1e-3;
        var cost = Cost(data, p0);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // Normal equations of the linearised problem: (JᵀWJ + λ·diag(JᵀWJ))·δ = JᵀW·r
            var jtj = new double[3, 3];
            var jtr = new double[3];
            foreach (var (t, y, weight) in data)
            {
                var e = Math.Exp(-p0[2] * t);
                var g = Math.Exp(-p0[1] * e);
                var model = p0[0] * g;
                double[] j = [g, -p0[0] * e * g, p0[0] * p0[1] * t * e * g];
                var r = y - model;
                for (var i = 0; i < 3; i++)
                {
                    jtr[i] += weight * j[i] * r;
                    for (var k = 0; k < 3; k++) jtj[i, k] += weight * j[i] * j[k];
                }
            }

            for (var i = 0; i < 3; i++) jtj[i, i] *= 1 + lambda;
            if (LinearSolver.Solve(jtj, jtr) is not { } step) break;

            var candidate = new[] { p0[0] + step[0], p0[1] + step[1], p0[2] + step[2] };
            var candidateCost = candidate.All(v => v > 0) ? Cost(data, candidate) : double.PositiveInfinity;
            if (candidateCost < cost)
            {
                var improvement = (cost - candidateCost) / Math.Max(cost, 1e-12);
                (p0, cost, lambda) = (candidate, candidateCost, Math.Max(lambda / 10, 1e-12));
                if (improvement < 1e-10) break;
            }
            else
            {
                lambda *= 10;
                if (lambda > 1e12) break;
            }
        }

        var curve = new GompertzCurve(p0[0], p0[1], p0[2]);
        // Reject curves that are mathematically fine but biologically absurd.
        if (curve.A is < 50 or > 2000 || curve.K is <= 0 or > 0.1 || double.IsNaN(curve.A)) return null;

        var observed = points.Select(p => p.WeightKg).ToList();
        var predicted = points.Select(p => curve.WeightAt(p.AgeDays)).ToList();
        return new GompertzFit(curve, FitQuality.From(observed, predicted));
    }

    private static double Cost(List<(double T, double Y, double W)> data, double[] p) =>
        data.Sum(d =>
        {
            var r = d.Y - p[0] * Math.Exp(-p[1] * Math.Exp(-p[2] * d.T));
            return d.W * r * r;
        });
}
