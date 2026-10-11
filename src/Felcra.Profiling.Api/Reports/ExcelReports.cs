using ClosedXML.Excel;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Api.Reports;

/// <summary>Excel versions of the reports, for further analysis.</summary>
internal static class ExcelReports
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E8F0E8");
    private static readonly XLColor SelectedFill = XLColor.FromHtml("#FFF59D");

    /// <summary>
    /// Census sheet in the import template's columns (so it can be corrected and re-imported), plus a
    /// summary sheet.
    /// </summary>
    public static byte[] CensusForm(CensusFormData d)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Census");
        var row = TitleBlock(sheet, d.Scope, $"{d.SessionTitle} · person in charge: {d.PicName ?? "–"}");

        string[] headers = ["Line", "Tag No", "P", "I", "AJ", "AB", "Age", "Weight (kg)", "BCS", "Project", "Remarks", "Selected", "Animal no."];
        var headerRow = row;
        Headers(sheet, row++, headers);
        foreach (var r in d.Rows)
        {
            sheet.Cell(row, 1).Value = r.No;
            sheet.Cell(row, 2).Value = r.TagNumber ?? "NO. TAG";
            sheet.Cell(row, 3 + r.Category switch { AnimalCategory.Bull => 0, AnimalCategory.Dam => 1, AnimalCategory.MaleCalf => 2, _ => 3 }).Value = "/";
            sheet.Cell(row, 7).Value = ReportText.Age(r.AgeMonths);
            if (r.WeightKg is { } w) sheet.Cell(row, 8).Value = w;
            if (r.Bcs is { } b) sheet.Cell(row, 9).Value = b;
            sheet.Cell(row, 10).Value = r.ProjectCode ?? "";
            sheet.Cell(row, 11).Value = r.Remarks ?? "";
            sheet.Cell(row, 12).Value = r.Selected ? "/" : "";
            sheet.Cell(row, 13).Value = r.AnimalNo;
            if (r.Selected) sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = SelectedFill;
            if (r.DuplicateTag) sheet.Cell(row, 2).CreateComment().AddText("Tag number also used by another animal on this farm.");
            row++;
        }
        Finish(sheet, headerRow, row - 1, headers.Length);
        sheet.Range(headerRow, 3, row - 1, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(headerRow, 12, row - 1, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var summary = workbook.Worksheets.Add("Summary");
        var s = TitleBlock(summary, d.Scope);
        string[] cols = ["Category", "On record", "Counted", "Selected", "Avg weight (kg)"];
        var first = s;
        Headers(summary, s++, cols);
        (AnimalCategory Category, int? Recorded, int Counted)[] categories =
        [
            (AnimalCategory.Bull, d.Recorded?.Bulls, d.Counted.Bulls),
            (AnimalCategory.Dam, d.Recorded?.Dams, d.Counted.Dams),
            (AnimalCategory.MaleCalf, d.Recorded?.MaleCalves, d.Counted.MaleCalves),
            (AnimalCategory.FemaleCalf, d.Recorded?.FemaleCalves, d.Counted.FemaleCalves),
        ];
        foreach (var (category, recorded, counted) in categories)
        {
            var rows = d.Rows.Where(r => r.Category == category).ToList();
            var weights = rows.Where(r => r.WeightKg is not null).Select(r => r.WeightKg!.Value).ToList();
            summary.Cell(s, 1).Value = ReportText.Category(category);
            if (recorded is { } rec) summary.Cell(s, 2).Value = rec;
            summary.Cell(s, 3).Value = counted;
            summary.Cell(s, 4).Value = rows.Count(r => r.Selected);
            if (weights.Count > 0) summary.Cell(s, 5).Value = Math.Round(weights.Average(), 1);
            s++;
        }
        summary.Cell(s, 1).Value = "Total";
        if (d.Recorded is { } total) summary.Cell(s, 2).Value = total.Total;
        summary.Cell(s, 3).Value = d.Counted.Total;
        summary.Cell(s, 4).Value = d.Selected.Count;
        summary.Row(s).Style.Font.Bold = true;
        Finish(summary, first, s, cols.Length);

        s += 2;
        summary.Cell(s++, 1).Value = "Programme treatments";
        summary.Cell(s - 1, 1).Style.Font.Bold = true;
        foreach (var t in d.Treatments) summary.Cell(s++, 1).Value = t;
        if (!string.IsNullOrWhiteSpace(d.Notes))
        {
            s++;
            summary.Cell(s++, 1).Value = "Findings and recommendations";
            summary.Cell(s - 1, 1).Style.Font.Bold = true;
            summary.Cell(s, 1).Value = d.Notes;
        }
        return Save(workbook);
    }

    public static byte[] SelectedAnimals(CensusFormData d)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Selected");
        var scope = d.Scope with { Title = "Selected animals" };
        var row = TitleBlock(sheet, scope, $"{d.Selected.Count} of {d.Rows.Count} animals assessed");
        string[] headers = ["Line", "Animal no.", "Tag no.", "Category", "Age", "Weight (kg)", "BCS", "Project", "Remarks"];
        var first = row;
        Headers(sheet, row++, headers);
        foreach (var r in d.Selected)
        {
            Values(sheet, row++, r.No, r.AnimalNo, r.TagNumber ?? "No tag", ReportText.Category(r.Category), ReportText.Age(r.AgeMonths),
                r.WeightKg, r.Bcs, r.ProjectCode, r.Remarks);
        }
        Finish(sheet, first, row - 1, headers.Length);
        return Save(workbook);
    }

    public static byte[] Treatments(TreatmentReportData d)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Treatments");
        var row = TitleBlock(sheet, d.Scope);
        string[] headers = ["Date", "Farm", "Animal no.", "Tag no.", "Category", "Product", "Product type", "Dose", "Route", "Batch", "Withdrawal until", "Next due", "Given by"];
        var first = row;
        Headers(sheet, row++, headers);
        foreach (var r in d.Rows)
        {
            Values(sheet, row++, r.GivenOn, r.FarmCode, r.AnimalNo, r.TagNumber, ReportText.Category(r.Category), r.Product, r.ProductCategory,
                r.Dose, r.Route, r.BatchNumber, r.WithdrawalUntil, r.NextDueOn, r.GivenBy);
        }
        Finish(sheet, first, row - 1, headers.Length);

        var summary = workbook.Worksheets.Add("By product");
        var s = TitleBlock(summary, d.Scope);
        var sf = s;
        Headers(summary, s++, ["Product", "Treatments", "Animals"]);
        foreach (var (product, treatments, animals) in d.ByProduct) Values(summary, s++, product, treatments, animals);
        Finish(summary, sf, s - 1, 3);
        return Save(workbook);
    }

    public static byte[] Performance(PerformanceData d)
    {
        using var workbook = new XLWorkbook();
        var h = d.Herd;
        var sheet = workbook.Worksheets.Add("Summary");
        var row = TitleBlock(sheet, d.Scope);
        (string, object?)[] figures =
        [
            ("Active animals", h.ActiveAnimals),
            ("Capacity", h.Capacity),
            ("Pregnancy rate (%)", h.PregnancyRatePercent),
            ("Breeding females without records", h.BreedingFemalesWithoutRecords),
            ("Calves born in the last 12 months", h.CalvesLast12Months),
            ("Breeding tasks due in 14 days", h.TasksDue),
            ("…of which overdue", h.TasksOverdue),
            ("Culling candidates", d.CullingCandidates),
            ("Untagged animals", h.Untagged),
            ("Animals sharing a tag number", h.SharingATag),
            ("Weighed in the last 90 days", h.WeighedLast90Days),
            ($"Weighings in period", d.Activity.Weighings),
            ("BCS scores in period", d.Activity.BcsScores),
            ("Treatments in period", d.Activity.Treatments),
            ("Breeding events in period", d.Activity.BreedingEvents),
            ("Observations in period", d.Activity.Observations),
            ("Calves registered in period", d.Activity.CalvesBorn),
            ("Animals that left the herd in period", d.Exits.Count),
        ];
        var first = row;
        Headers(sheet, row++, ["Figure", "Value"]);
        foreach (var (label, value) in figures) Values(sheet, row++, label, value);
        Finish(sheet, first, row - 1, 2);

        var categories = workbook.Worksheets.Add("By category");
        var c = TitleBlock(categories, d.Scope);
        var cf = c;
        Headers(categories, c++, ["Category", "Head", "Avg weight (kg)", "Avg BCS"]);
        foreach (var cat in h.Categories) Values(categories, c++, ReportText.Category(cat.Category), cat.Count, cat.AverageWeightKg, cat.AverageBcs);
        Finish(categories, cf, c - 1, 4);

        var breeding = workbook.Worksheets.Add("Breeding");
        var b = TitleBlock(breeding, d.Scope);
        var bf = b;
        Headers(breeding, b++, ["Farm", "Breeding females", "Pregnant", "Served", "Open", "No records", "Pregnancy rate (%)", "Avg calving interval (days)",
            "Calves (12 mo)", "Calving rate (%)", "Abortions (12 mo)"]);
        foreach (var s in d.Breeding)
            Values(breeding, b++, s.FarmCode, s.Dams, s.Pregnant, s.Served, s.Open, s.NoRecords, s.PregnancyRatePercent, s.AverageCalvingIntervalDays,
                s.CalvesLast12Months, s.CalvingRatePercent, s.AbortionsLast12Months);
        Finish(breeding, bf, b - 1, 11);

        var exits = workbook.Worksheets.Add("Left the herd");
        var e = TitleBlock(exits, d.Scope);
        var ef = e;
        Headers(exits, e++, ["Date", "Farm", "Animal no.", "Tag no.", "Category", "Status", "Reason"]);
        foreach (var x in d.Exits) Values(exits, e++, x.Date, x.FarmCode, x.AnimalNo, x.TagNumber, ReportText.Category(x.Category), ReportText.Humanize(x.Status), x.Reason);
        Finish(exits, ef, e - 1, 7);

        var growth = workbook.Worksheets.Add("Below growth curve");
        var g = TitleBlock(growth, d.Scope, $"Latest weight {-d.Growth.BelowCurveThresholdPercent:0}% or more below the Gompertz curve for the animal's age");
        var gf = g;
        Headers(growth, g++, ["Animal no.", "Tag no.", "Category", "Age (months)", "Age estimated", "Weight (kg)", "Weighed on", "Expected (kg)", "Difference (%)"]);
        foreach (var a in d.Growth.BelowCurve)
            Values(growth, g++, a.AnimalNo, a.TagNumber, ReportText.Category(a.Category), Math.Round(a.AgeDays / 30.44, 1), a.AgeIsEstimated ? "Yes" : "No",
                a.WeightKg, a.WeighedOn, a.ExpectedKg, a.DeviationPercent);
        Finish(growth, gf, g - 1, 9);
        return Save(workbook);
    }

    public static byte[] Culling(CullingReportData d)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Culling candidates");
        var row = TitleBlock(sheet, d.Scope);
        var first = row;
        Headers(sheet, row++, ["Animal no.", "Tag no.", "Farm", "Age", "Status", "Last calving", "BCS", "Reasons"]);
        foreach (var x in d.Candidates)
            Values(sheet, row++, x.AnimalNo, x.TagNumber, x.FarmCode, ReportText.Age(x.AgeMonths), ReportText.Humanize(x.State), x.LastCalving, x.LatestBcs,
                string.Join("; ", x.Reasons));
        Finish(sheet, first, row - 1, 8);
        return Save(workbook);
    }

    // ---- Building blocks -----------------------------------------------------------------------------

    /// <summary>Title, farm, period and generation time; returns the next free row.</summary>
    private static int TitleBlock(IXLWorksheet sheet, ReportScope scope, string? subtitle = null)
    {
        sheet.Cell(1, 1).Value = scope.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = scope.FarmLabel + (scope.Period is null ? "" : $" · {scope.Period}");
        var row = 3;
        if (subtitle is not null) sheet.Cell(row++, 1).Value = subtitle;
        sheet.Cell(row, 1).Value = $"Generated {ReportText.Generated(scope.GeneratedAt)} by FELCRA Cattle Profiling";
        sheet.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
        return row + 2;
    }

    private static void Headers(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) sheet.Cell(row, i + 1).Value = headers[i];
        var range = sheet.Range(row, 1, row, headers.Count);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static void Values(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            switch (values[i])
            {
                case null: break;
                case DateOnly date:
                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    cell.Style.DateFormat.Format = "d mmm yyyy";
                    break;
                default:
                    cell.Value = XLCellValue.FromObject(values[i]);
                    break;
            }
        }
    }

    /// <summary>Borders, filter, frozen header and column widths for a table.</summary>
    private static void Finish(IXLWorksheet sheet, int headerRow, int lastRow, int columns)
    {
        var range = sheet.Range(headerRow, 1, Math.Max(headerRow, lastRow), columns);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        if (lastRow > headerRow) range.SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, columns).AdjustToContents(headerRow, Math.Max(headerRow, lastRow));
        foreach (var column in sheet.Columns(1, columns)) if (column.Width > 60) column.Width = 60;
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
