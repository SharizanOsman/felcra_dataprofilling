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

/// <summary>Colours from the FFISB logo (navy, red and their tints) that read well on the light theme and in print.</summary>
public static class ChartColors
{
    public const string Primary = "#2A347F";
    public const string Secondary = "#E41E26";
    public const string NavyLight = "#6B74B8";
    public const string RedLight = "#F08A8E";
    public const string Gold = "#C9A227";
    public const string Warning = "#ef6c00";
    public const string Error = "#b71c1c";
    public const string Muted = "#9AA0B4";
    public const string Purple = NavyLight;

    /// <summary>Series colours in order, for charts with several categories.</summary>
    public static readonly string[] Categorical = [Primary, Secondary, NavyLight, RedLight, Muted, Gold];

    public static string Pick(int index) => Categorical[index % Categorical.Length];

    public static string Svg(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>One slice of a donut chart.</summary>
public sealed record DonutSegment(string Label, double Value, string Color);

/// <summary>
/// One row of a horizontal bar chart. <see cref="Text"/> is the value label shown at the end of the bar
/// (defaults to the value); <see cref="Color"/> overrides the chart colour for this row.
/// </summary>
public sealed record HBarItem(string Label, double Value, string? Text = null, string? Color = null, string? Href = null);
