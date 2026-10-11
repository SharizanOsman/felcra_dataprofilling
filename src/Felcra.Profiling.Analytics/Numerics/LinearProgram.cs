namespace Felcra.Profiling.Analytics.Numerics;

public enum ConstraintKind
{
    LessOrEqual,
    GreaterOrEqual,
    Equal,
}

/// <summary>One linear constraint: Σ coefficients[j]·x[j] (≤ | ≥ | =) rhs.</summary>
public sealed record LinearConstraint(double[] Coefficients, ConstraintKind Kind, double Rhs, string Name);

public enum LpStatus
{
    Optimal,
    Infeasible,
    Unbounded,
}

public sealed record LpResult(LpStatus Status, double[] X, double Objective);

/// <summary>
/// Minimises cᵀx subject to linear constraints and x ≥ 0, using the two-phase simplex method with
/// Bland's rule (no cycling). Sized for ration problems: tens of variables and constraints.
/// </summary>
public static class LinearProgram
{
    private const double Eps = 1e-9;

    public static LpResult Minimize(double[] cost, IReadOnlyList<LinearConstraint> constraints)
    {
        var n = cost.Length;
        var m = constraints.Count;

        // Normalise to non-negative right-hand sides.
        var rows = constraints.Select(c => c.Rhs < 0
            ? (Coef: c.Coefficients.Select(v => -v).ToArray(), Kind: Flip(c.Kind), Rhs: -c.Rhs)
            : (Coef: c.Coefficients, c.Kind, c.Rhs)).ToList();

        var slackCount = rows.Count(r => r.Kind != ConstraintKind.Equal);
        var artificialCount = rows.Count(r => r.Kind != ConstraintKind.LessOrEqual);
        var cols = n + slackCount + artificialCount;
        var t = new double[m, cols + 1];      // tableau; last column is the right-hand side
        var basis = new int[m];
        var isArtificial = new bool[cols];

        int slack = n, artificial = n + slackCount;
        for (var i = 0; i < m; i++)
        {
            for (var j = 0; j < n; j++) t[i, j] = rows[i].Coef[j];
            t[i, cols] = rows[i].Rhs;
            switch (rows[i].Kind)
            {
                case ConstraintKind.LessOrEqual:
                    t[i, slack] = 1;
                    basis[i] = slack++;
                    break;
                case ConstraintKind.GreaterOrEqual:
                    t[i, slack++] = -1;
                    t[i, artificial] = 1;
                    isArtificial[artificial] = true;
                    basis[i] = artificial++;
                    break;
                default:
                    t[i, artificial] = 1;
                    isArtificial[artificial] = true;
                    basis[i] = artificial++;
                    break;
            }
        }

        // Phase 1: minimise the sum of artificial variables to find a feasible basis.
        var phase1 = new double[cols];
        for (var j = 0; j < cols; j++) phase1[j] = isArtificial[j] ? 1 : 0;
        var allowed = Enumerable.Repeat(true, cols).ToArray();
        if (!Optimise(t, basis, phase1, allowed, m, cols)) return new LpResult(LpStatus.Unbounded, new double[n], double.NaN);
        if (ObjectiveValue(t, basis, phase1, m, cols) > 1e-7) return new LpResult(LpStatus.Infeasible, new double[n], double.NaN);

        // Pivot remaining (zero-valued) artificial variables out of the basis where possible.
        for (var i = 0; i < m; i++)
        {
            if (!isArtificial[basis[i]]) continue;
            for (var j = 0; j < cols; j++)
            {
                if (isArtificial[j] || Math.Abs(t[i, j]) < Eps) continue;
                Pivot(t, basis, i, j, m, cols);
                break;
            }
        }

        // Phase 2: the real objective, artificial columns excluded.
        var phase2 = new double[cols];
        Array.Copy(cost, phase2, n);
        for (var j = 0; j < cols; j++) allowed[j] = !isArtificial[j];
        if (!Optimise(t, basis, phase2, allowed, m, cols)) return new LpResult(LpStatus.Unbounded, new double[n], double.NaN);

        var x = new double[n];
        for (var i = 0; i < m; i++)
            if (basis[i] < n) x[basis[i]] = Math.Max(0, t[i, cols]);
        return new LpResult(LpStatus.Optimal, x, cost.Zip(x, (c, v) => c * v).Sum());
    }

    /// <summary>Runs simplex iterations. Returns false if the problem is unbounded.</summary>
    private static bool Optimise(double[,] t, int[] basis, double[] cost, bool[] allowed, int m, int cols)
    {
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            // Entering column: the lowest-index column with a negative reduced cost (Bland's rule).
            var entering = -1;
            for (var j = 0; j < cols && entering < 0; j++)
            {
                if (!allowed[j] || basis.Contains(j)) continue;
                var reduced = cost[j];
                for (var i = 0; i < m; i++) reduced -= cost[basis[i]] * t[i, j];
                if (reduced < -Eps) entering = j;
            }
            if (entering < 0) return true;

            // Leaving row: minimum ratio; ties broken by the lowest basic index.
            var leaving = -1;
            var best = double.PositiveInfinity;
            for (var i = 0; i < m; i++)
            {
                if (t[i, entering] <= Eps) continue;
                var ratio = t[i, cols] / t[i, entering];
                if (ratio < best - Eps || (Math.Abs(ratio - best) <= Eps && leaving >= 0 && basis[i] < basis[leaving]))
                    (best, leaving) = (ratio, i);
            }
            if (leaving < 0) return false;

            Pivot(t, basis, leaving, entering, m, cols);
        }
        throw new InvalidOperationException("Simplex did not converge.");
    }

    private static void Pivot(double[,] t, int[] basis, int row, int col, int m, int cols)
    {
        var p = t[row, col];
        for (var j = 0; j <= cols; j++) t[row, j] /= p;
        for (var i = 0; i < m; i++)
        {
            if (i == row || Math.Abs(t[i, col]) < 1e-15) continue;
            var f = t[i, col];
            for (var j = 0; j <= cols; j++) t[i, j] -= f * t[row, j];
        }
        basis[row] = col;
    }

    private static double ObjectiveValue(double[,] t, int[] basis, double[] cost, int m, int cols)
    {
        var value = 0.0;
        for (var i = 0; i < m; i++) value += cost[basis[i]] * t[i, cols];
        return value;
    }

    private static ConstraintKind Flip(ConstraintKind kind) => kind switch
    {
        ConstraintKind.LessOrEqual => ConstraintKind.GreaterOrEqual,
        ConstraintKind.GreaterOrEqual => ConstraintKind.LessOrEqual,
        _ => ConstraintKind.Equal,
    };
}
