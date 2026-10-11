using System.Globalization;

namespace Felcra.Profiling.UI.Shared.Charts;

public enum SeriesKind
{
    Line,
    Points,
}

/// <summary>A point on an X/Y chart. <see cref="Label"/> is shown as a tooltip.</summary>
public sealed record ChartPoint(double X, double Y, string? Label = null);

public sealed record ChartSeries(string Name, string Color, SeriesKind Kind, IReadOnlyList<ChartPoint> Points);

/// <summary>One stack segment per series in a bar chart; values line up with the chart's labels.</summary>
public sealed record BarSeries(string Name, string Color, IReadOnlyList<double> Values);

/// <summary>Round axis ticks ("nice numbers") covering a data range.</summary>
public sealed record AxisScale(double Min, double Max, double Step)
{
    public static AxisScale For(double min, double max, int targetTicks = 5)
    {
        if (double.IsNaN(min) || double.IsNaN(max)) (min, max) = (0, 1);
        if (max - min < 1e-9) (min, max) = (min - 1, max + 1);
        var rough = (max - min) / targetTicks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = (rough / magnitude) switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10,
        } * magnitude;
        return new AxisScale(Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }

    public IEnumerable<double> Ticks()
    {
        for (var v = Min; v <= Max + Step / 1000; v += Step) yield return Math.Round(v, 10);
    }

    public static string Format(double value) => value.ToString(Math.Abs(value) >= 100 || value == Math.Round(value) ? "0" : "0.#", CultureInfo.InvariantCulture);
}

/// <summary>Colours that read well on the light theme and in print.</summary>
public static class ChartColors
{
    public const string Primary = "#2e7d32";
    public const string Secondary = "#1565c0";
    public const string Warning = "#ef6c00";
    public const string Error = "#c62828";
    public const string Muted = "#90a4ae";
    public const string Purple = "#6a1b9a";

    public static string Svg(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
