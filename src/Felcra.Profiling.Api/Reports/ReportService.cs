namespace Felcra.Profiling.Api.Reports;

/// <summary>Produces report files (PDF or Excel) with consistent names.</summary>
internal sealed class ReportService(ReportQueries queries)
{
    public DateOnly Today => queries.Today;

    /// <summary>The previous calendar month, the default period for reports and the monthly e-mail.</summary>
    public (DateOnly From, DateOnly To) LastMonth()
    {
        var first = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    public async Task<ReportFile?> CensusFormAsync(Guid sessionId, ReportFormat format, CancellationToken ct) =>
        await queries.CensusFormAsync(sessionId, ct) is { } d
            ? Make($"census-form-{d.FarmCode}-{d.StartDate:yyyy-MM-dd}", format, () => PdfReports.CensusForm(d), () => ExcelReports.CensusForm(d))
            : null;

    public async Task<ReportFile?> SelectedAnimalsAsync(Guid sessionId, ReportFormat format, CancellationToken ct) =>
        await queries.CensusFormAsync(sessionId, ct) is { } d
            ? Make($"selected-animals-{d.FarmCode}-{d.StartDate:yyyy-MM-dd}", format, () => PdfReports.SelectedAnimals(d), () => ExcelReports.SelectedAnimals(d))
            : null;

    /// <summary>Null when the farm does not exist or is not visible to the user.</summary>
    public async Task<ReportFile?> TreatmentsAsync(Guid? farmId, DateOnly from, DateOnly to, ReportFormat format, CancellationToken ct)
    {
        if (await queries.FarmLabelAsync(farmId, ct) is not { } label) return null;
        var d = await queries.TreatmentsAsync(farmId, from, to, label, ct);
        return Make($"treatments-{await FarmPartAsync(farmId, ct)}-{Period(from, to)}", format, () => PdfReports.Treatments(d), () => ExcelReports.Treatments(d));
    }

    public async Task<ReportFile?> PerformanceAsync(Guid? farmId, DateOnly from, DateOnly to, ReportFormat format, CancellationToken ct)
    {
        if (await queries.FarmLabelAsync(farmId, ct) is not { } label) return null;
        var d = await queries.PerformanceAsync(farmId, from, to, label, ct);
        return Make($"farm-performance-{await FarmPartAsync(farmId, ct)}-{Period(from, to)}", format, () => PdfReports.Performance(d), () => ExcelReports.Performance(d));
    }

    public async Task<ReportFile?> CullingAsync(Guid? farmId, ReportFormat format, CancellationToken ct)
    {
        if (await queries.FarmLabelAsync(farmId, ct) is not { } label) return null;
        var d = await queries.CullingAsync(farmId, label, ct);
        return Make($"culling-candidates-{await FarmPartAsync(farmId, ct)}-{Today:yyyy-MM-dd}", format, () => PdfReports.Culling(d), () => ExcelReports.Culling(d));
    }

    /// <summary>The monthly e-mail's PDFs plus the figures quoted in its text.</summary>
    public async Task<MonthlyPackage> MonthlyPackageAsync(Guid? farmId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var label = await queries.FarmLabelAsync(farmId, ct) ?? throw new InvalidOperationException($"Farm {farmId} not found.");
        var farm = await FarmPartAsync(farmId, ct);
        var performance = await queries.PerformanceAsync(farmId, from, to, label, ct);
        var treatments = await queries.TreatmentsAsync(farmId, from, to, label, ct);
        var culling = await queries.CullingAsync(farmId, label, ct);
        return new MonthlyPackage(performance,
        [
            new ReportFile($"{ReportText.FileSafe($"farm-performance-{farm}-{Period(from, to)}")}.pdf", ReportFile.Pdf, PdfReports.Performance(performance)),
            new ReportFile($"{ReportText.FileSafe($"treatments-{farm}-{Period(from, to)}")}.pdf", ReportFile.Pdf, PdfReports.Treatments(treatments)),
            new ReportFile($"{ReportText.FileSafe($"culling-candidates-{farm}-{Today:yyyy-MM-dd}")}.pdf", ReportFile.Pdf, PdfReports.Culling(culling)),
        ]);
    }

    private async Task<string> FarmPartAsync(Guid? farmId, CancellationToken ct) =>
        farmId is null ? "all-farms" : (await queries.FarmLabelAsync(farmId, ct))!.Split(" · ")[0];

    /// <summary>"2026-09" for a whole month, otherwise "2026-09-01-to-2026-09-15".</summary>
    private static string Period(DateOnly from, DateOnly to) =>
        from.Day == 1 && to == from.AddMonths(1).AddDays(-1) ? $"{from:yyyy-MM}" : $"{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}";

    private static ReportFile Make(string name, ReportFormat format, Func<byte[]> pdf, Func<byte[]> xlsx) =>
        format == ReportFormat.Pdf
            ? new ReportFile($"{ReportText.FileSafe(name)}.pdf", ReportFile.Pdf, pdf())
            : new ReportFile($"{ReportText.FileSafe(name)}.xlsx", ReportFile.Xlsx, xlsx());
}
