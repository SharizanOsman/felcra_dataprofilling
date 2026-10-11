using Felcra.Profiling.Contracts.Analytics;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Api.Reports;

/// <summary>A generated report ready to download or attach to an e-mail.</summary>
internal sealed record ReportFile(string FileName, string ContentType, byte[] Content)
{
    public const string Pdf = "application/pdf";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

internal sealed record MonthlyPackage(PerformanceData Data, IReadOnlyList<ReportFile> Files);

internal enum ReportFormat
{
    Pdf,
    Xlsx,
}

/// <summary>Who / where a report covers, printed in its heading.</summary>
internal sealed record ReportScope(string Title, string FarmLabel, string? Period, DateTimeOffset GeneratedAt);

// ---- Census & profiling form (layout of the Pagoh report) ---------------------------------------

internal sealed record CensusFormRow(
    int No, string AnimalNo, string? TagNumber, bool DuplicateTag, AnimalCategory Category, int? AgeMonths,
    decimal? WeightKg, decimal? Bcs, string? ProjectCode, string? Remarks, bool Selected);

internal sealed record CensusFormData(
    ReportScope Scope,
    string FarmCode,
    string FarmName,
    string? FarmAddress,
    string Projects,
    string Breeds,
    string SessionTitle,
    DateOnly StartDate,
    DateOnly EndDate,
    string? PicName,
    string? PicPosition,
    string? PicPhone,
    string? Notes,
    CategoryCounts? Recorded,
    CategoryCounts Counted,
    IReadOnlyList<string> Treatments,
    IReadOnlyList<CensusFormRow> Rows)
{
    public IReadOnlyList<CensusFormRow> Selected => Rows.Where(r => r.Selected).ToList();
}

// ---- Treatments ----------------------------------------------------------------------------------

internal sealed record TreatmentRow(
    DateOnly GivenOn, string FarmCode, string AnimalNo, string? TagNumber, AnimalCategory Category, string Product,
    string ProductCategory, string? Dose, string? Route, string? BatchNumber, DateOnly? WithdrawalUntil, DateOnly? NextDueOn,
    string? GivenBy);

internal sealed record TreatmentReportData(ReportScope Scope, IReadOnlyList<TreatmentRow> Rows)
{
    public IReadOnlyList<(string Product, int Treatments, int Animals)> ByProduct =>
        Rows.GroupBy(r => r.Product)
            .Select(g => (g.Key, g.Count(), g.Select(r => r.AnimalNo).Distinct().Count()))
            .OrderByDescending(x => x.Item2).ToList();
}

// ---- Farm performance ----------------------------------------------------------------------------

internal sealed record PeriodActivity(int Weighings, int BcsScores, int Treatments, int BreedingEvents, int Observations, int CalvesBorn);

internal sealed record ExitRow(DateOnly? Date, string FarmCode, string AnimalNo, string? TagNumber, AnimalCategory Category, AnimalStatus Status, string? Reason);

internal sealed record PerformanceData(
    ReportScope Scope,
    DashboardDto Herd,
    IReadOnlyList<HerdBreedingSummaryDto> Breeding,
    GrowthAnalysisDto Growth,
    PeriodActivity Activity,
    IReadOnlyList<ExitRow> Exits,
    int CullingCandidates);

// ---- Culling candidates --------------------------------------------------------------------------

internal sealed record CullingReportData(ReportScope Scope, IReadOnlyList<CullingCandidateDto> Candidates);

/// <summary>Wording shared by the PDF and Excel versions.</summary>
internal static class ReportText
{
    public static string Category(AnimalCategory category) => category switch
    {
        AnimalCategory.Bull => "Bull (P)",
        AnimalCategory.Dam => "Dam (I)",
        AnimalCategory.MaleCalf => "Male calf (AJ)",
        AnimalCategory.FemaleCalf => "Female calf (AB)",
        _ => category.ToString(),
    };

    /// <summary>"1 year 6 months", as on the census form.</summary>
    public static string Age(int? months) => months switch
    {
        null => "",
        < 12 => $"{months} month{(months == 1 ? "" : "s")}",
        _ when months % 12 == 0 => Years(months.Value / 12),
        _ => $"{Years(months.Value / 12)} {months % 12} month{(months % 12 == 1 ? "" : "s")}",
    };

    private static string Years(int years) => $"{years} year{(years == 1 ? "" : "s")}";

    // Invariant culture: "Sep", not "Sept" or a localised name, whatever the server's culture.
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", Invariant);

    public static string Month(DateOnly date) => date.ToString("MMMM yyyy", Invariant);

    /// <summary>Generation time in Malaysia time.</summary>
    public static string Generated(DateTimeOffset at) => at.ToOffset(TimeSpan.FromHours(8)).ToString("d MMM yyyy HH:mm", Invariant) + " (MYT)";

    public static string DateRange(DateOnly from, DateOnly to) =>
        from == to ? Date(from)
        : from.Year == to.Year && from.Month == to.Month ? $"{from.Day}–{to.Day} {to.ToString("MMM yyyy", Invariant)}"
        : $"{Date(from)} – {Date(to)}";

    /// <summary>Enum name as words, e.g. DiagnosticTest → "Diagnostic test".</summary>
    public static string Humanize<T>(T value) where T : struct, Enum =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<=[a-z])(?=[A-Z])", " ") is var s && s.Length > 0
            ? s[0] + s[1..].ToLowerInvariant()
            : s;

    public static string FileSafe(string text) =>
        string.Concat(text.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-').ToLowerInvariant();
}
