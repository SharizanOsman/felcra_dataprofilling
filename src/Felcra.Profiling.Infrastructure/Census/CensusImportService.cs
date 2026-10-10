using System.Text;
using ClosedXML.Excel;
using Felcra.Profiling.Application.Census;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Census;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Infrastructure.Census;

internal sealed class CensusImportService(AppDbContext db) : ICensusImportService
{
    private const string ImportReason = "Census import";

    public async Task<ImportOutcome?> PreviewAsync(Guid sessionId, Stream file, string fileName, bool matchExisting, CancellationToken ct = default)
    {
        var session = await db.CensusSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;

        var (plan, error) = await PlanAsync(session, file, fileName, matchExisting, ct);
        return error is not null ? new ImportOutcome(null, null, error) : new ImportOutcome(ToPreview(plan!), null, null);
    }

    public async Task<ImportOutcome?> ImportAsync(Guid sessionId, Stream file, string fileName, bool matchExisting, CancellationToken ct = default)
    {
        var session = await db.CensusSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;

        var (plan, error) = await PlanAsync(session, file, fileName, matchExisting, ct);
        if (error is not null) return new ImportOutcome(null, null, error);

        var preview = ToPreview(plan!);
        if (!preview.CanImport) return new ImportOutcome(preview, null, null);

        var existingEntries = await db.CensusEntries
            .Where(e => e.CensusSessionId == sessionId)
            .ToDictionaryAsync(e => e.AnimalId, ct);

        // EF Core orders inserts by key, and UUIDv7s made in the same millisecond are not ordered.
        // Spacing the IDs 1 ms apart keeps animal numbers in the same order as the sheet's lines.
        var idClock = DateTimeOffset.UtcNow;
        var idSequence = 0;

        int created = 0, matched = 0, entriesCreated = 0, entriesUpdated = 0;
        foreach (var item in plan!)
        {
            var row = item.Row;
            var category = row.Category!.Value;
            Animal animal;

            if (item.Match is { } existing)
            {
                animal = existing;
                animal.Category = category;
                animal.Sex = Animal.SexOf(category);
                animal.ProjectId ??= item.ProjectId;
                if (animal.BirthDate is null && row.AgeMonths is { } months)
                {
                    animal.BirthDate = session.StartDate.AddMonths(-months);
                    animal.BirthDateMethod = AgeEstimationMethod.Dentition;
                }
                matched++;
            }
            else
            {
                animal = new Animal
                {
                    Id = Guid.CreateVersion7(idClock.AddMilliseconds(idSequence++)),
                    FarmId = session.FarmId,
                    Category = category,
                    Sex = Animal.SexOf(category),
                    ProjectId = item.ProjectId,
                    BirthDate = row.AgeMonths is { } months ? session.StartDate.AddMonths(-months) : null,
                    BirthDateMethod = row.AgeMonths is null ? null : AgeEstimationMethod.Dentition,
                };
                animal.Retag(row.TagNumber, session.StartDate, ImportReason);
                db.Animals.Add(animal);
                created++;
            }

            if (!existingEntries.TryGetValue(animal.Id, out var entry))
            {
                entry = new CensusEntry { CensusSessionId = session.Id, AnimalId = animal.Id, FarmId = session.FarmId };
                db.CensusEntries.Add(entry);
                existingEntries[animal.Id] = entry;
                entriesCreated++;
            }
            else
            {
                entriesUpdated++;
            }

            entry.LineNo = row.LineNo;
            entry.Category = category;
            entry.EstimatedAgeMonths = row.AgeMonths;
            entry.WeightKg = row.WeightKg;
            entry.BodyConditionScore = row.BodyConditionScore;
            entry.Remarks = item.Remarks;
            entry.Selected = row.Selected;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ImportOutcome(preview, new ImportResultDto(created, matched, entriesCreated, entriesUpdated), null);
    }

    private sealed record PlannedRow(ParsedCensusRow Row, Animal? Match, Guid? ProjectId, string? ProjectCode, string? Remarks, List<string> Errors, List<string> Warnings);

    private async Task<(List<PlannedRow>? Plan, string? Error)> PlanAsync(
        CensusSession session, Stream file, string fileName, bool matchExisting, CancellationToken ct)
    {
        List<string?[]> sheet;
        try
        {
            sheet = ReadSheet(file, fileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, "The file could not be read. Upload an Excel (.xlsx) or CSV file.");
        }
        if (sheet.Count == 0) return (null, "The file is empty.");

        var parsed = CensusSheetParser.Parse(sheet);
        if (parsed.FatalError is not null) return (null, parsed.FatalError);
        if (parsed.Rows.Count == 0) return (null, "No animal rows were found below the header.");

        var projects = await db.Projects.Where(p => p.IsActive).ToDictionaryAsync(p => p.Code.ToUpperInvariant(), ct);
        var farmProjectIds = await db.FarmProjects.Where(fp => fp.FarmId == session.FarmId).Select(fp => fp.ProjectId).ToListAsync(ct);

        var tags = parsed.Rows.Where(r => r.TagNumber is not null).Select(r => TagKey(r.TagNumber!)).Distinct().ToList();
        var animalsByTag = (await db.Animals
                .Where(a => a.FarmId == session.FarmId && a.Status == AnimalStatus.Active && a.TagNumber != null)
                .ToListAsync(ct))
            .Where(a => tags.Contains(TagKey(a.TagNumber!)))
            .GroupBy(a => TagKey(a.TagNumber!))
            .ToDictionary(g => g.Key, g => g.ToList());
        var inSession = await db.CensusEntries.Where(e => e.CensusSessionId == session.Id).Select(e => e.AnimalId).ToListAsync(ct);
        var fileTagCounts = parsed.Rows.Where(r => r.TagNumber is not null).GroupBy(r => TagKey(r.TagNumber!)).ToDictionary(g => g.Key, g => g.Count());

        var plan = new List<PlannedRow>();
        foreach (var row in parsed.Rows)
        {
            var errors = row.Errors.ToList();
            var warnings = row.Warnings.ToList();

            // Project: an explicit column, or the remarks column when it holds a project code (as on the paper form).
            Guid? projectId = null;
            string? projectCode = null;
            var remarks = row.Remarks;
            var projectText = row.ProjectText;
            if (projectText is null && remarks is not null && projects.ContainsKey(remarks.Trim().ToUpperInvariant()))
                (projectText, remarks) = (remarks, null);
            if (projectText is not null)
            {
                if (projects.TryGetValue(projectText.Trim().ToUpperInvariant(), out var project))
                {
                    (projectId, projectCode) = (project.Id, project.Code);
                    if (!farmProjectIds.Contains(project.Id))
                        warnings.Add($"Project {project.Code} is not linked to this farm.");
                }
                else
                {
                    warnings.Add($"Unknown project '{projectText}'; ignored.");
                }
            }

            Animal? match = null;
            if (row.TagNumber is { } tag)
            {
                var key = TagKey(tag);
                var existing = animalsByTag.GetValueOrDefault(key) ?? [];
                var inFile = fileTagCounts[key];

                if (matchExisting && existing.Count == 1 && inFile == 1)
                {
                    match = existing[0];
                    if (inSession.Contains(match.Id))
                        warnings.Add("Already counted in this session; the entry will be updated.");
                }
                else if (matchExisting && existing.Count > 0)
                {
                    errors.Add(existing.Count > 1
                        ? $"Tag {tag} is used by {existing.Count} animals ({string.Join(", ", existing.Select(a => Animal.FormatAnimalNo(a.AnimalNo)))}); give them distinct tags first, or import without matching."
                        : $"Tag {tag} appears {inFile} times in this file but matches one existing animal; fix the file or import without matching.");
                }
                else
                {
                    if (inFile > 1) warnings.Add($"Tag {tag} appears {inFile} times in this file; each row becomes a separate animal.");
                    if (existing.Count > 0)
                        warnings.Add($"Tag {tag} is already used by {string.Join(", ", existing.Select(a => Animal.FormatAnimalNo(a.AnimalNo)))}; another animal with this tag will be registered.");
                }
            }

            plan.Add(new PlannedRow(row, match, projectId, projectCode, remarks, errors, warnings));
        }
        return (plan, null);
    }

    private static ImportPreviewDto ToPreview(List<PlannedRow> plan)
    {
        var rows = plan.Select(p => new ImportRowDto(
            p.Row.RowNumber, p.Row.LineNo, p.Row.TagNumber, p.Row.Category, p.Row.AgeText, p.Row.AgeMonths, p.Row.WeightKg,
            p.Row.BodyConditionScore, p.ProjectCode, p.Remarks, p.Row.Selected,
            p.Errors.Count > 0 ? ImportRowAction.Error : p.Match is null ? ImportRowAction.CreateAnimal : ImportRowAction.MatchExisting,
            p.Match is null ? null : Animal.FormatAnimalNo(p.Match.AnimalNo),
            p.Errors, p.Warnings)).ToList();

        var valid = rows.Where(r => r.Action != ImportRowAction.Error).ToList();
        int Count(AnimalCategory c) => valid.Count(r => r.Category == c);

        return new ImportPreviewDto(
            rows,
            valid.Count(r => r.Action == ImportRowAction.CreateAnimal),
            valid.Count(r => r.Action == ImportRowAction.MatchExisting),
            rows.Count(r => r.Action == ImportRowAction.Error),
            rows.Count(r => r.Warnings.Count > 0),
            new CategoryCounts(Count(AnimalCategory.Bull), Count(AnimalCategory.Dam), Count(AnimalCategory.MaleCalf), Count(AnimalCategory.FemaleCalf)));
    }

    private static string TagKey(string tag) => tag.Replace(" ", "").ToUpperInvariant();

    private static List<string?[]> ReadSheet(Stream file, string fileName)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return CsvReader.Read(reader.ReadToEnd());
        }

        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported file type.");

        using var workbook = new XLWorkbook(file);
        var worksheet = workbook.Worksheets.First();
        var range = worksheet.RangeUsed();
        if (range is null) return [];

        var lastColumn = range.LastColumn().ColumnNumber();
        return range.Rows()
            .Select(r => Enumerable.Range(1, lastColumn).Select(c => (string?)r.WorksheetRow().Cell(c).GetFormattedString()).ToArray())
            .ToList();
    }

    public byte[] CreateTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Census");
        string[] headers = ["Line", "Tag No", "P", "I", "AJ", "AB", "Age", "Weight (kg)", "BCS", "Project", "Remarks", "Selected"];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");

        object[] example = [1, "0125", "", "", "/", "", "1 Tahun", 119, "", "RP4", "", ""];
        for (var i = 0; i < example.Length; i++) sheet.Cell(2, i + 1).Value = XLCellValue.FromObject(example[i]);
        sheet.Row(2).Style.Font.Italic = true;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        var help = workbook.Worksheets.Add("Instructions");
        string[] lines =
        [
            "One row per animal. Delete the example row before importing.",
            "Tag No: ear-tag number. Leave empty (or write NO. TAG) for untagged animals.",
            "Category: put / in exactly one of P (bull / pejantan), I (dam / induk), AJ (male calf / anak jantan), AB (female calf / anak betina).",
            "Age: e.g. '1 Tahun 6 Bulan', '8 Bulan', '1.5 years'. Used to estimate the birth date.",
            "Weight (kg): number, e.g. 119 or '119 Kg'.",
            "BCS: body condition score (optional).",
            "Project: project code such as EPP5 or RP4 (may also be written in Remarks, as on the paper form).",
            "Selected: put / (or Y) for animals selected in this programme.",
            "Malay headers from the paper form (Bil, No. Tag, Jenis, Umur, Berat, Catatan, Dipilih) are also accepted.",
        ];
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Column(1).Width = 120;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
