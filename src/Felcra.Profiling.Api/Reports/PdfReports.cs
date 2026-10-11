using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Felcra.Profiling.Api.Reports;

/// <summary>PDF versions of the reports (MigraDoc, MIT licence).</summary>
internal static class PdfReports
{
    private static readonly Color Green = Color.FromRgb(0x1b, 0x5e, 0x20);
    private static readonly Color HeaderFill = Color.FromRgb(0xe8, 0xf0, 0xe8);
    private static readonly Color SelectedFill = Color.FromRgb(0xff, 0xf5, 0x9d);
    private static readonly Color Grey = Color.FromRgb(0x61, 0x61, 0x61);

    private sealed record Col(string Header, string Width, ParagraphAlignment Align = ParagraphAlignment.Left);

    // ---- Census & profiling form ------------------------------------------------------------------

    public static byte[] CensusForm(CensusFormData d)
    {
        var doc = NewDocument(d.Scope, out var section);

        var title = section.AddParagraph("CATTLE DATA PROFILING REPORT");
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = 15;
        title.Format.Font.Bold = true;
        foreach (var line in new[] { d.FarmName.ToUpperInvariant(), $"SCHEMES: {d.Projects}", $"DATE: {ReportText.DateRange(d.StartDate, d.EndDate).ToUpperInvariant()}" })
        {
            var p = section.AddParagraph(line);
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.Font.Bold = true;
        }

        Heading(section, "1. Programme details");
        var details = KeyValueTable(section, "4.5cm", "11.5cm");
        KeyValue(details, "Location", d.FarmName);
        KeyValue(details, "Programme", d.SessionTitle);
        KeyValue(details, "Period", ReportText.DateRange(d.StartDate, d.EndDate));
        KeyValue(details, "Breed", d.Breeds);
        KeyValue(details, "Animals assessed", $"{d.Rows.Count} head");
        KeyValue(details, "Animals selected", $"{d.Selected.Count} head");
        KeyValue(details, "Person in charge", string.Join(", ", new[] { d.PicName, d.PicPosition }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } pic ? pic : "–");

        Heading(section, "2. Results");
        var results = NewTable(section,
            new Col("Category", "4cm"), new Col("On record", "2.3cm", ParagraphAlignment.Right), new Col("Counted", "2.3cm", ParagraphAlignment.Right),
            new Col("Difference", "2.3cm", ParagraphAlignment.Right), new Col("Selected", "2.3cm", ParagraphAlignment.Right),
            new Col("Avg weight", "2.8cm", ParagraphAlignment.Right));
        foreach (var (category, recorded, counted) in Categories(d.Recorded, d.Counted))
        {
            var rows = d.Rows.Where(r => r.Category == category).ToList();
            var weights = rows.Where(r => r.WeightKg is not null).Select(r => r.WeightKg!.Value).ToList();
            AddRow(results, ReportText.Category(category), recorded?.ToString() ?? "–", counted.ToString(),
                recorded is { } rec ? $"{counted - rec:+0;-0;0}" : "–", rows.Count(r => r.Selected).ToString(),
                weights.Count > 0 ? $"{weights.Average():0} kg" : "–");
        }
        var total = AddRow(results, "Total", d.Recorded?.Total.ToString() ?? "–", d.Counted.Total.ToString(),
            d.Recorded is { } r0 ? $"{d.Counted.Total - r0.Total:+0;-0;0}" : "–", d.Selected.Count.ToString(), "");
        total.Format.Font.Bold = true;

        var untagged = d.Rows.Count(r => r.TagNumber is null);
        var duplicates = d.Rows.Count(r => r.DuplicateTag);
        var noWeight = d.Rows.Count(r => r.WeightKg is null);
        var oldDams = d.Rows.Count(r => r.Category == AnimalCategory.Dam && r.AgeMonths >= 96);
        var notes = new List<string>();
        if (d.Recorded is not null && d.Recorded.Total != d.Counted.Total)
            notes.Add($"The count differs from the population on record by {d.Counted.Total - d.Recorded.Total:+0;-0} head.");
        if (untagged > 0) notes.Add($"{untagged} animals have no ear tag.");
        if (duplicates > 0) notes.Add($"{duplicates} animals share an ear-tag number with another animal (marked * on the form).");
        if (noWeight > 0) notes.Add($"{noWeight} animals were not weighed.");
        if (oldDams > 0) notes.Add($"{oldDams} dams are aged 8 years or more (candidates for herd rejuvenation).");
        foreach (var note in notes) Bullet(section, note);

        Heading(section, "3. Programme treatments");
        if (d.Treatments.Count == 0) section.AddParagraph("None recorded.");
        for (var i = 0; i < d.Treatments.Count; i++) section.AddParagraph($"{i + 1}. {d.Treatments[i]}");

        Heading(section, "4. Findings and recommendations");
        if (string.IsNullOrWhiteSpace(d.Notes))
            Muted(section, "No notes were recorded for this programme. Add them to the census programme's notes to include them here.");
        else
            foreach (var para in d.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries)) section.AddParagraph(para.Trim()).Format.SpaceAfter = "4pt";

        // ---- The form itself, laid out like the paper form ----
        section.AddPageBreak();
        var formTitle = section.AddParagraph("CENSUS AND DATA PROFILING FORM — HEADCOUNT AND ANIMAL HEALTH PROGRAMME");
        formTitle.Format.Alignment = ParagraphAlignment.Center;
        formTitle.Format.Font.Bold = true;
        formTitle.Format.Font.Underline = Underline.Single;
        formTitle.Format.SpaceAfter = "8pt";
        FarmBox(section, d);

        section.AddParagraph().Format.SpaceAfter = "6pt";
        var form = section.AddTable();
        form.Borders.Width = 0.5;
        form.Format.Font.Size = 9;
        foreach (var w in new[] { "1.1cm", "2.4cm", "0.9cm", "0.9cm", "0.9cm", "0.9cm", "2.6cm", "1.7cm", "1.1cm", "4.5cm" }) form.AddColumn(w);
        var h1 = form.AddRow();
        var h2 = form.AddRow();
        foreach (var h in new[] { h1, h2 })
        {
            h.HeadingFormat = true;
            h.Shading.Color = HeaderFill;
            h.Format.Font.Bold = true;
            h.Format.Alignment = ParagraphAlignment.Center;
        }
        string[] top = ["No.", "Tag no.", "Category", "", "", "", "Age", "Weight", "BCS", "Remarks"];
        for (var i = 0; i < top.Length; i++)
        {
            if (top[i].Length == 0) continue;
            h1.Cells[i].AddParagraph(top[i]);
            if (i == 2) h1.Cells[i].MergeRight = 3;
            else h1.Cells[i].MergeDown = 1;
        }
        string[] codes = ["P", "I", "AJ", "AB"];
        for (var i = 0; i < 4; i++) h2.Cells[2 + i].AddParagraph(codes[i]);

        foreach (var row in d.Rows)
        {
            var r = form.AddRow();
            if (row.Selected) r.Shading.Color = SelectedFill;
            r.Cells[0].AddParagraph(row.No.ToString()).Format.Alignment = ParagraphAlignment.Center;
            r.Cells[1].AddParagraph(row.TagNumber is null ? "NO TAG" : row.TagNumber + (row.DuplicateTag ? " *" : ""));
            r.Cells[2 + CategoryColumn(row.Category)].AddParagraph("/").Format.Alignment = ParagraphAlignment.Center;
            r.Cells[6].AddParagraph(ReportText.Age(row.AgeMonths));
            r.Cells[7].AddParagraph(row.WeightKg is { } w ? $"{w:0} kg" : "").Format.Alignment = ParagraphAlignment.Right;
            r.Cells[8].AddParagraph(row.Bcs?.ToString("0.#") ?? "").Format.Alignment = ParagraphAlignment.Center;
            r.Cells[9].AddParagraph(string.Join(" · ", new[] { row.ProjectCode, row.Remarks }.Where(s => !string.IsNullOrWhiteSpace(s))));
        }
        Muted(section, $"Highlighted rows: selected animals ({d.Selected.Count}). * Ear-tag number also used by another animal on this farm. " +
                       "P = bull, I = dam, AJ = male calf, AB = female calf.").Format.SpaceBefore = "4pt";

        SignOff(section, d.PicName, d.PicPosition);
        return Render(doc);
    }

    /// <summary>The "farm / premises information" box at the top of the paper form.</summary>
    private static void FarmBox(Section section, CensusFormData d)
    {
        var t = section.AddTable();
        t.Borders.Width = 0.5;
        t.Format.Font.Size = 9;
        foreach (var w in new[] { "3.4cm", "3.4cm", "2.3cm", "2.3cm", "2.1cm", "2.1cm", "1.4cm" }) t.AddColumn(w);

        var head = t.AddRow();
        head.Shading.Color = HeaderFill;
        Bold(head.Cells[0].AddParagraph("FARM / PREMISES INFORMATION"));
        head.Cells[0].MergeRight = 6;
        head.Cells[0].Format.Alignment = ParagraphAlignment.Center;

        var r1 = t.AddRow();
        Label(r1.Cells[0], "PROGRAMME DATE", ReportText.DateRange(d.StartDate, d.EndDate));
        Label(r1.Cells[1], "PROJECT", d.Projects);
        Label(r1.Cells[2], "FARM ADDRESS", d.FarmAddress ?? d.FarmName);
        r1.Cells[2].MergeRight = 4;

        var r2 = t.AddRow();
        Label(r2.Cells[0], "PERSON IN CHARGE",
            string.Join("\n", new[] { d.PicName, d.PicPosition, d.PicPhone is null ? null : $"Tel: {d.PicPhone}" }.Where(s => !string.IsNullOrWhiteSpace(s))));
        r2.Cells[0].MergeRight = 1;
        r2.Cells[0].MergeDown = 1;
        PopulationHeader(r2, 2, "POPULATION ON RECORD");
        var r3 = t.AddRow();
        PopulationValues(r3, 2, d.Recorded);

        var r4 = t.AddRow();
        Label(r4.Cells[0], "SPECIES", "Beef cattle");
        Label(r4.Cells[1], "BREED", d.Breeds);
        PopulationHeader(r4, 2, "POPULATION COUNTED");
        var r5 = t.AddRow();
        Label(r5.Cells[0], "PROGRAMME TREATMENTS",
            d.Treatments.Count == 0 ? "None recorded" : string.Join("\n", d.Treatments.Select((x, i) => $"{i + 1}. {x}")));
        r5.Cells[0].MergeRight = 1;
        PopulationValues(r5, 2, d.Counted);
    }

    private static void PopulationHeader(Row row, int first, string title)
    {
        var cell = row.Cells[first];
        Bold(cell.AddParagraph(title));
        cell.MergeRight = 4;
        cell.Format.Alignment = ParagraphAlignment.Center;
        cell.Shading.Color = HeaderFill;
    }

    private static void PopulationValues(Row row, int first, CategoryCounts? counts)
    {
        string[] labels = ["Bulls", "Dams", "Male calves", "Female calves", "Total"];
        int?[] values = counts is null ? [null, null, null, null, null] : [counts.Bulls, counts.Dams, counts.MaleCalves, counts.FemaleCalves, counts.Total];
        for (var i = 0; i < 5; i++)
        {
            var cell = row.Cells[first + i];
            Bold(cell.AddParagraph(labels[i] + ":"));
            cell.AddParagraph(values[i] is { } v ? $"{v} head" : "–");
        }
    }

    private static void Label(Cell cell, string label, string value)
    {
        Bold(cell.AddParagraph(label + ":"));
        cell.AddParagraph(value);
    }

    private static int CategoryColumn(AnimalCategory category) => category switch
    {
        AnimalCategory.Bull => 0,
        AnimalCategory.Dam => 1,
        AnimalCategory.MaleCalf => 2,
        _ => 3,
    };

    private static IEnumerable<(AnimalCategory Category, int? Recorded, int Counted)> Categories(CategoryCounts? recorded, CategoryCounts counted) =>
    [
        (AnimalCategory.Bull, recorded?.Bulls, counted.Bulls),
        (AnimalCategory.Dam, recorded?.Dams, counted.Dams),
        (AnimalCategory.MaleCalf, recorded?.MaleCalves, counted.MaleCalves),
        (AnimalCategory.FemaleCalf, recorded?.FemaleCalves, counted.FemaleCalves),
    ];

    private static void SignOff(Section section, string? preparedBy, string? position)
    {
        Heading(section, "Approval");
        var t = section.AddTable();
        t.Borders.Width = 0.5;
        t.AddColumn("8cm");
        t.AddColumn("8cm");
        var head = t.AddRow();
        head.Shading.Color = HeaderFill;
        Bold(head.Cells[0].AddParagraph("PREPARED BY:"));
        Bold(head.Cells[1].AddParagraph("APPROVED BY:"));
        var body = t.AddRow();
        body.Height = "3cm";
        body.VerticalAlignment = VerticalAlignment.Bottom;
        foreach (var (cell, name, pos) in new[] { (body.Cells[0], preparedBy, position), (body.Cells[1], (string?)null, (string?)null) })
        {
            cell.AddParagraph("Signature: ______________________");
            cell.AddParagraph($"Name: {name ?? "______________________"}");
            cell.AddParagraph($"Position: {pos ?? "____________________"}");
            cell.AddParagraph("Date: ______________________");
        }
    }

    // ---- Selected animals ----------------------------------------------------------------------------

    public static byte[] SelectedAnimals(CensusFormData d)
    {
        var doc = NewDocument(d.Scope with { Title = "Selected animals" }, out var section);
        TitleBlock(section, d.Scope with { Title = "Selected animals" });
        section.AddParagraph($"{d.SessionTitle}: {d.Selected.Count} of {d.Rows.Count} animals assessed were selected as meeting the programme specification.")
            .Format.SpaceAfter = "6pt";

        var t = NewTable(section,
            new Col("No.", "1.1cm", ParagraphAlignment.Center), new Col("Animal", "2.1cm"), new Col("Tag no.", "2cm"), new Col("Category", "3cm"),
            new Col("Age", "2.8cm"), new Col("Weight", "1.7cm", ParagraphAlignment.Right), new Col("BCS", "1.1cm", ParagraphAlignment.Center),
            new Col("Remarks", "3.2cm"));
        foreach (var r in d.Selected)
            AddRow(t, r.No.ToString(), r.AnimalNo, r.TagNumber ?? "No tag", ReportText.Category(r.Category), ReportText.Age(r.AgeMonths),
                r.WeightKg is { } w ? $"{w:0} kg" : "", r.Bcs?.ToString("0.#") ?? "",
                string.Join(" · ", new[] { r.ProjectCode, r.Remarks }.Where(s => !string.IsNullOrWhiteSpace(s))));

        Heading(section, "By category");
        var s = NewTable(section, new Col("Category", "4cm"), new Col("Selected", "2.5cm", ParagraphAlignment.Right), new Col("Avg weight", "2.8cm", ParagraphAlignment.Right));
        foreach (var g in d.Selected.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            var weights = g.Where(r => r.WeightKg is not null).Select(r => r.WeightKg!.Value).ToList();
            AddRow(s, ReportText.Category(g.Key), g.Count().ToString(), weights.Count > 0 ? $"{weights.Average():0} kg" : "–");
        }
        return Render(doc);
    }

    // ---- Treatments ----------------------------------------------------------------------------------

    public static byte[] Treatments(TreatmentReportData d)
    {
        var doc = NewDocument(d.Scope, out var section, landscape: true);
        TitleBlock(section, d.Scope);
        if (d.Rows.Count == 0)
        {
            section.AddParagraph("No treatments were recorded in this period.");
            return Render(doc);
        }

        Heading(section, "Summary");
        var s = NewTable(section, new Col("Product", "8cm"), new Col("Treatments", "2.5cm", ParagraphAlignment.Right), new Col("Animals", "2.5cm", ParagraphAlignment.Right));
        foreach (var (product, treatments, animals) in d.ByProduct) AddRow(s, product, treatments.ToString(), animals.ToString());
        AddRow(s, "Total", d.Rows.Count.ToString(), d.Rows.Select(r => r.AnimalNo).Distinct().Count().ToString()).Format.Font.Bold = true;

        Heading(section, "Treatments given");
        var t = NewTable(section,
            new Col("Date", "2.1cm"), new Col("Farm", "1.6cm"), new Col("Animal", "2cm"), new Col("Tag", "1.6cm"), new Col("Category", "2.6cm"),
            new Col("Product", "4.4cm"), new Col("Dose", "1.6cm"), new Col("Route", "2.2cm"), new Col("Batch", "1.8cm"),
            new Col("Withdrawal until", "2.2cm"), new Col("Next due", "2cm"), new Col("Given by", "2.6cm"));
        t.Format.Font.Size = 8.5;
        foreach (var r in d.Rows)
            AddRow(t, ReportText.Date(r.GivenOn), r.FarmCode, r.AnimalNo, r.TagNumber ?? "–", ReportText.Category(r.Category), r.Product, r.Dose ?? "",
                r.Route ?? "", r.BatchNumber ?? "", r.WithdrawalUntil is { } w ? ReportText.Date(w) : "", r.NextDueOn is { } n ? ReportText.Date(n) : "", r.GivenBy ?? "");
        return Render(doc);
    }

    // ---- Farm performance ----------------------------------------------------------------------------

    public static byte[] Performance(PerformanceData d)
    {
        var doc = NewDocument(d.Scope, out var section);
        TitleBlock(section, d.Scope);
        var h = d.Herd;

        Heading(section, "Herd today");
        var k = KeyValueTable(section, "7cm", "9cm");
        KeyValue(k, "Active animals", h.Capacity is > 0 ? $"{h.ActiveAnimals} ({100.0 * h.ActiveAnimals / h.Capacity.Value:0}% of capacity {h.Capacity})" : h.ActiveAnimals.ToString());
        KeyValue(k, "Pregnancy rate (breeding females)", h.PregnancyRatePercent is { } p ? $"{p:0.#}%" : "– (no breeding records yet)");
        KeyValue(k, "Calves born in the last 12 months", h.CalvesLast12Months.ToString());
        KeyValue(k, "Breeding tasks due in 14 days", h.TasksOverdue > 0 ? $"{h.TasksDue} ({h.TasksOverdue} overdue)" : h.TasksDue.ToString());
        KeyValue(k, "Culling candidates", d.CullingCandidates.ToString());

        section.AddParagraph().Format.SpaceAfter = "4pt";
        var c = NewTable(section, new Col("Category", "4.5cm"), new Col("Head", "2cm", ParagraphAlignment.Right),
            new Col("Avg weight", "3cm", ParagraphAlignment.Right), new Col("Avg BCS", "2.5cm", ParagraphAlignment.Right));
        foreach (var cat in h.Categories)
            AddRow(c, ReportText.Category(cat.Category), cat.Count.ToString(), cat.AverageWeightKg is { } w ? $"{w:0} kg" : "–", cat.AverageBcs?.ToString("0.0") ?? "–");
        AddRow(c, "Total", h.ActiveAnimals.ToString(), "", "").Format.Font.Bold = true;

        Heading(section, $"Recorded in the period ({d.Scope.Period})");
        var a = KeyValueTable(section, "7cm", "9cm");
        KeyValue(a, "Weighings", d.Activity.Weighings.ToString());
        KeyValue(a, "Body condition scores", d.Activity.BcsScores.ToString());
        KeyValue(a, "Treatments", d.Activity.Treatments.ToString());
        KeyValue(a, "Breeding events", d.Activity.BreedingEvents.ToString());
        KeyValue(a, "Observations", d.Activity.Observations.ToString());
        KeyValue(a, "Calves registered", d.Activity.CalvesBorn.ToString());

        Heading(section, "Animals that left the herd in the period");
        if (d.Exits.Count == 0) section.AddParagraph("None.");
        else
        {
            var e = NewTable(section, new Col("Date", "2.4cm"), new Col("Farm", "1.8cm"), new Col("Animal", "2.2cm"), new Col("Tag", "1.8cm"),
                new Col("Category", "3cm"), new Col("Status", "2cm"), new Col("Reason", "3cm"));
            foreach (var x in d.Exits)
                AddRow(e, x.Date is { } date ? ReportText.Date(date) : "", x.FarmCode, x.AnimalNo, x.TagNumber ?? "–", ReportText.Category(x.Category),
                    ReportText.Humanize(x.Status), x.Reason ?? "");
            Muted(section, $"Dead: {d.Exits.Count(x => x.Status == AnimalStatus.Dead)} · Sold: {d.Exits.Count(x => x.Status == AnimalStatus.Sold)} · " +
                           $"Culled: {d.Exits.Count(x => x.Status == AnimalStatus.Culled)}");
        }

        Heading(section, "Breeding");
        if (d.Breeding.Count == 0) section.AddParagraph("No farms.");
        else
        {
            var b = NewTable(section, new Col("Farm", "1.8cm"), new Col("Breeding females", "2.2cm", ParagraphAlignment.Right),
                new Col("Pregnant", "1.7cm", ParagraphAlignment.Right), new Col("Open", "1.4cm", ParagraphAlignment.Right),
                new Col("No records", "1.8cm", ParagraphAlignment.Right), new Col("Pregnancy rate", "2.1cm", ParagraphAlignment.Right),
                new Col("Calving interval", "2.3cm", ParagraphAlignment.Right), new Col("Calves (12 mo)", "2.1cm", ParagraphAlignment.Right));
            foreach (var s in d.Breeding)
                AddRow(b, s.FarmCode, s.Dams.ToString(), s.Pregnant.ToString(), s.Open.ToString(), s.NoRecords.ToString(),
                    s.PregnancyRatePercent is { } r && s.NoRecords < s.Dams ? $"{r:0.#}%" : "–",
                    s.AverageCalvingIntervalDays is { } ci ? $"{ci} days" : "–", s.CalvesLast12Months.ToString());
        }

        Heading(section, "Growth (Gompertz curve)");
        var g = NewTable(section, new Col("", "2.5cm"), new Col("Mature weight", "3cm", ParagraphAlignment.Right),
            new Col("Fit (R²)", "2cm", ParagraphAlignment.Right), new Col("Weighings", "2.2cm", ParagraphAlignment.Right),
            new Col("Below curve", "2.4cm", ParagraphAlignment.Right));
        foreach (var curve in d.Growth.Curves)
        {
            var below = d.Growth.BelowCurve.Count(x => curve.Animals.Any(a => a.AnimalId == x.AnimalId));
            AddRow(g, curve.Sex == Sex.Female ? "Females" : "Males", curve.Fit is { } f ? $"{f.MatureWeightKg:0} kg" : "–",
                curve.Fit?.RSquared.ToString("0.00") ?? "–", curve.Fit?.Points.ToString() ?? "0", curve.Fit is null ? "–" : below.ToString());
        }
        Muted(section, $"Animals {-d.Growth.BelowCurveThresholdPercent:0}% or more below the curve for their age (from 3 months old) are counted as below the curve.");

        Heading(section, "Data quality");
        Bullet(section, $"{h.Untagged} animals without an ear tag; {h.SharingATag} sharing a tag number.");
        Bullet(section, $"{h.ActiveAnimals - h.WeighedLast90Days} animals not weighed in the last 90 days.");
        if (h.BreedingFemalesWithoutRecords > 0) Bullet(section, $"{h.BreedingFemalesWithoutRecords} breeding females have no breeding records.");
        if (d.Growth.AnimalsWithoutBirthDate > 0) Bullet(section, $"{d.Growth.AnimalsWithoutBirthDate} animals have no birth date or estimated age.");
        return Render(doc);
    }

    // ---- Culling candidates --------------------------------------------------------------------------

    public static byte[] Culling(CullingReportData d)
    {
        var doc = NewDocument(d.Scope, out var section, landscape: true);
        TitleBlock(section, d.Scope);
        if (d.Candidates.Count == 0)
        {
            section.AddParagraph("No animals meet the culling rules.");
        }
        else
        {
            var t = NewTable(section, new Col("Animal", "2.2cm"), new Col("Tag", "1.8cm"), new Col("Farm", "1.8cm"), new Col("Age", "3cm"),
                new Col("Status", "2.4cm"), new Col("Last calving", "2.4cm"), new Col("BCS", "1.3cm", ParagraphAlignment.Center), new Col("Reasons", "10.5cm"));
            foreach (var x in d.Candidates)
                AddRow(t, x.AnimalNo, x.TagNumber ?? "–", x.FarmCode, ReportText.Age(x.AgeMonths), ReportText.Humanize(x.State),
                    x.LastCalving is { } lc ? ReportText.Date(lc) : "–", x.LatestBcs?.ToString("0.#") ?? "–", string.Join("; ", x.Reasons));
        }
        Muted(section, "Rules: aged 8 years or more · not pregnant 12 months after calving · empty at 2 pregnancy checks in a row · low body condition. " +
                       "Animals without breeding records are not judged on fertility. A final decision needs a veterinary check.").Format.SpaceBefore = "6pt";
        return Render(doc);
    }

    // ---- Building blocks -----------------------------------------------------------------------------

    private static Document NewDocument(ReportScope scope, out Section section, bool landscape = false)
    {
        ReportFonts.EnsureRegistered();
        var doc = new Document();
        doc.Info.Title = $"{scope.Title} — {scope.FarmLabel}";
        doc.Info.Author = "FELCRA Cattle Profiling";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportFonts.Family;
        normal.Font.Size = 10;

        section = doc.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        setup.TopMargin = "2cm";
        setup.BottomMargin = "1.8cm";
        setup.LeftMargin = "1.5cm";
        setup.RightMargin = "1.5cm";
        setup.HeaderDistance = "0.8cm";
        setup.FooterDistance = "0.8cm";

        var header = section.Headers.Primary.AddParagraph();
        header.Format.Font.Size = 8;
        header.Format.Font.Color = Grey;
        header.AddFormattedText("FELCRA Cattle Profiling", TextFormat.Bold);
        header.AddText($"  ·  {scope.Title}  ·  {scope.FarmLabel}");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Grey;
        footer.AddText($"Generated {ReportText.Generated(scope.GeneratedAt)}  ·  Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        return doc;
    }

    private static void TitleBlock(Section section, ReportScope scope)
    {
        var title = section.AddParagraph(scope.Title);
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Green;
        section.AddParagraph(scope.FarmLabel).Format.Font.Bold = true;
        if (scope.Period is not null) section.AddParagraph(scope.Period);
        section.LastParagraph!.Format.SpaceAfter = "8pt";
    }

    private static void Heading(Section section, string text)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = 12;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = Green;
        p.Format.SpaceBefore = "12pt";
        p.Format.SpaceAfter = "4pt";
        p.Format.KeepWithNext = true;
    }

    private static Paragraph Muted(Section section, string text)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = 8.5;
        p.Format.Font.Color = Grey;
        p.Format.SpaceBefore = "3pt";
        return p;
    }

    private static void Bullet(Section section, string text)
    {
        var p = section.AddParagraph("•\t" + text);
        p.Format.LeftIndent = "0.5cm";
        p.Format.FirstLineIndent = "-0.5cm";
        p.Format.TabStops.AddTabStop("0.5cm");
        p.Format.SpaceBefore = "2pt";
    }

    private static void Bold(Paragraph p) => p.Format.Font.Bold = true;

    private static Table NewTable(Section section, params Col[] cols)
    {
        var t = section.AddTable();
        t.Borders.Width = 0.5;
        t.Borders.Color = Color.FromRgb(0x9e, 0x9e, 0x9e);
        t.Format.Font.Size = 9;
        t.TopPadding = "1.5pt";
        t.BottomPadding = "1.5pt";
        foreach (var c in cols) t.AddColumn(c.Width).Format.Alignment = c.Align;
        var head = t.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = HeaderFill;
        head.Format.Font.Bold = true;
        for (var i = 0; i < cols.Length; i++) head.Cells[i].AddParagraph(cols[i].Header);
        return t;
    }

    private static Row AddRow(Table table, params string[] values)
    {
        var row = table.AddRow();
        for (var i = 0; i < values.Length; i++) row.Cells[i].AddParagraph(values[i]);
        return row;
    }

    private static Table KeyValueTable(Section section, string keyWidth, string valueWidth)
    {
        var t = section.AddTable();
        t.Borders.Width = 0.5;
        t.Borders.Color = Color.FromRgb(0x9e, 0x9e, 0x9e);
        t.TopPadding = "1.5pt";
        t.BottomPadding = "1.5pt";
        t.AddColumn(keyWidth);
        t.AddColumn(valueWidth);
        return t;
    }

    private static void KeyValue(Table table, string key, string value)
    {
        var row = table.AddRow();
        row.Cells[0].Shading.Color = HeaderFill;
        Bold(row.Cells[0].AddParagraph(key));
        row.Cells[1].AddParagraph(value);
    }

    private static byte[] Render(Document doc)
    {
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}
