namespace Felcra.Profiling.Analytics.Numerics;

/// <summary>Small dense linear algebra for the models (a handful of unknowns, so no library is needed).</summary>
public static class LinearSolver
{
    /// <summary>Solves A·x = b by Gaussian elimination with partial pivoting. Returns null if A is singular.</summary>
    public static double[]? Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;

            if (pivot != col)
                for (var j = col; j <= n; j++) (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);

            for (var row = col + 1; row < n; row++)
            {
                var factor = m[row, col] / m[col, col];
                for (var j = col; j <= n; j++) m[row, j] -= factor * m[col, j];
            }
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = m[i, n];
            for (var j = i + 1; j < n; j++) sum -= m[i, j] * x[j];
            x[i] = sum / m[i, i];
        }
        return x;
    }

    /// <summary>
    /// Ordinary least squares: the coefficients minimising Σ (y − X·β)². Columns are scaled before
    /// forming the normal equations to keep them well conditioned. Returns null if the columns are dependent.
    /// </summary>
    public static double[]? LeastSquares(IReadOnlyList<double[]> rows, IReadOnlyList<double> y)
    {
        var p = rows[0].Length;
        var scale = new double[p];
        for (var j = 0; j < p; j++)
            scale[j] = Math.Max(1e-12, Math.Sqrt(rows.Sum(r => r[j] * r[j])));

        var xtx = new double[p, p];
        var xty = new double[p];
        for (var i = 0; i < rows.Count; i++)
        {
            for (var j = 0; j < p; j++)
            {
                var xj = rows[i][j] / scale[j];
                xty[j] += xj * y[i];
                for (var k = 0; k < p; k++) xtx[j, k] += xj * rows[i][k] / scale[k];
            }
        }

        var beta = Solve(xtx, xty);
        if (beta is null) return null;
        for (var j = 0; j < p; j++) beta[j] /= scale[j];
        return beta;
    }
}

/// <summary>Goodness of fit for a fitted model.</summary>
public sealed record FitQuality(int Points, double RSquared, double Rmse)
{
    public static FitQuality From(IReadOnlyList<double> observed, IReadOnlyList<double> predicted)
    {
        var mean = observed.Average();
        var ssRes = observed.Zip(predicted, (o, p) => (o - p) * (o - p)).Sum();
        var ssTot = observed.Sum(o => (o - mean) * (o - mean));
        return new FitQuality(observed.Count, ssTot <= 0 ? 0 : 1 - ssRes / ssTot, Math.Sqrt(ssRes / observed.Count));
    }
}
