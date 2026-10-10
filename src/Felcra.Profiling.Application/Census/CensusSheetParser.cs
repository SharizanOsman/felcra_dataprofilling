using System.Globalization;
using System.Text.RegularExpressions;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Application.Census;

/// <summary>A data row from a census sheet, parsed but not yet matched against the database.</summary>
public sealed record ParsedCensusRow(
    int RowNumber,
    int? LineNo,
    string? TagNumber,
    AnimalCategory? Category,
    string? AgeText,
    int? AgeMonths,
    decimal? WeightKg,
    decimal? BodyConditionScore,
    string? ProjectText,
    string? Remarks,
    bool Selected,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CensusSheetParseResult(IReadOnlyList<ParsedCensusRow> Rows, string? FatalError);

/// <summary>
/// Parses census sheets in either the FFISB paper-form layout (Malay headers, one tick column each
/// for P / I / AJ / AB, ages such as "1 Tahun 6 Bulan", weights such as "119 Kg") or a simple
/// English layout with a single Category column.
/// </summary>
public static partial class CensusSheetParser
{
    private enum Column { LineNo, Tag, Category, Bull, Dam, MaleCalf, FemaleCalf, Age, Weight, Bcs, Project, Remarks, Selected }

    private static readonly Dictionary<string, Column> HeaderNames = new()
    {
        ["bil"] = Column.LineNo, ["no"] = Column.LineNo, ["line"] = Column.LineNo, ["lineno"] = Column.LineNo, ["#"] = Column.LineNo,
        ["notag"] = Column.Tag, ["tag"] = Column.Tag, ["tagno"] = Column.Tag, ["tagnumber"] = Column.Tag, ["eartag"] = Column.Tag,
        ["jenis"] = Column.Category, ["category"] = Column.Category, ["kategori"] = Column.Category,
        ["p"] = Column.Bull, ["i"] = Column.Dam, ["aj"] = Column.MaleCalf, ["ab"] = Column.FemaleCalf,
        ["umur"] = Column.Age, ["age"] = Column.Age,
        ["berat"] = Column.Weight, ["beratkg"] = Column.Weight, ["weight"] = Column.Weight, ["weightkg"] = Column.Weight,
        ["bcs"] = Column.Bcs, ["bodyconditionscore"] = Column.Bcs,
        ["projek"] = Column.Project, ["project"] = Column.Project, ["scheme"] = Column.Project, ["skim"] = Column.Project,
        ["catatan"] = Column.Remarks, ["remarks"] = Column.Remarks, ["notes"] = Column.Remarks,
        ["dipilih"] = Column.Selected, ["selected"] = Column.Selected, ["pilih"] = Column.Selected,
    };

    public static CensusSheetParseResult Parse(IReadOnlyList<string?[]> sheet)
    {
        // Find the header row: the first of the top 30 rows with a tag column.
        var headerIndex = -1;
        Dictionary<Column, int> columns = [];
        for (var r = 0; r < Math.Min(sheet.Count, 30) && headerIndex < 0; r++)
        {
            var mapped = MapHeaders(sheet[r]);
            if (mapped.ContainsKey(Column.Tag)) (headerIndex, columns) = (r, mapped);
        }
        if (headerIndex < 0)
            return new([], "Could not find the header row. The sheet needs a 'Tag No' (or 'No. Tag') column.");

        // The paper form has "JENIS" on one row and "P I AJ AB" on the row below.
        var dataStart = headerIndex + 1;
        if (dataStart < sheet.Count && !HasTickColumns(columns))
        {
            var next = MapHeaders(sheet[dataStart]);
            if (HasTickColumns(next))
            {
                foreach (var (column, index) in next) columns.TryAdd(column, index);
                dataStart++;
            }
        }

        if (!columns.ContainsKey(Column.Category) && !HasTickColumns(columns))
            return new([], "Could not find the category: add a 'Category' column or the P / I / AJ / AB columns.");

        var rows = new List<ParsedCensusRow>();
        for (var r = dataStart; r < sheet.Count; r++)
        {
            var row = ParseRow(sheet[r], r + 1, columns);
            if (row is not null) rows.Add(row);
        }
        return new(rows, null);
    }

    private static ParsedCensusRow? ParseRow(string?[] cells, int rowNumber, Dictionary<Column, int> columns)
    {
        string? Cell(Column c) => columns.TryGetValue(c, out var i) && i < cells.Length && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i]!.Trim() : null;

        var tagText = Cell(Column.Tag);
        var ageText = Cell(Column.Age);
        var weightText = Cell(Column.Weight);
        var categoryText = Cell(Column.Category);
        var ticks = new[] { Column.Bull, Column.Dam, Column.MaleCalf, Column.FemaleCalf }.Where(c => IsTicked(Cell(c))).ToList();

        // Blank lines at the end of the paper form.
        if (tagText is null && ageText is null && weightText is null && categoryText is null && ticks.Count == 0) return null;

        var errors = new List<string>();
        var warnings = new List<string>();

        AnimalCategory? category = null;
        if (ticks.Count > 1)
            errors.Add("More than one of P / I / AJ / AB is ticked.");
        else if (ticks.Count == 1)
            category = ticks[0] switch
            {
                Column.Bull => AnimalCategory.Bull,
                Column.Dam => AnimalCategory.Dam,
                Column.MaleCalf => AnimalCategory.MaleCalf,
                _ => AnimalCategory.FemaleCalf,
            };
        else if (categoryText is not null)
            category = ParseCategory(categoryText) ?? AddError<AnimalCategory?>(errors, $"Unknown category '{categoryText}'. Use P, I, AJ or AB.");
        else
            errors.Add("Category is missing.");

        var tag = Animal.NormaliseTag(tagText);
        if (tag is null) warnings.Add("No ear tag: the animal will be registered untagged.");

        int? ageMonths = null;
        if (ageText is not null)
        {
            ageMonths = ParseAgeMonths(ageText);
            if (ageMonths is null) warnings.Add($"Could not read age '{ageText}'; age left empty.");
        }

        decimal? weight = null;
        if (weightText is not null)
        {
            weight = ParseDecimal(weightText.Replace("kg", "", StringComparison.OrdinalIgnoreCase));
            if (weight is null) errors.Add($"Weight '{weightText}' is not a number.");
            else if (weight is < 1 or > 1500) errors.Add($"Weight {weight} kg is outside 1–1500 kg.");
        }
        else
        {
            warnings.Add("No weight recorded.");
        }

        decimal? bcs = null;
        if (Cell(Column.Bcs) is { } bcsText)
        {
            bcs = ParseDecimal(bcsText);
            if (bcs is null or < 1 or > 9) errors.Add($"BCS '{bcsText}' must be a number from 1 to 9.");
        }

        int? lineNo = int.TryParse(Cell(Column.LineNo), out var n) ? n : null;

        if (category is { } c && ageMonths is { } months)
        {
            if (c is AnimalCategory.MaleCalf or AnimalCategory.FemaleCalf && months >= 30)
                warnings.Add($"Recorded as a calf but age is {FormatAge(months)}; check the category.");
            if (c is AnimalCategory.Bull or AnimalCategory.Dam && months < 12)
                warnings.Add($"Recorded as an adult but age is {FormatAge(months)}; check the category.");
        }

        return new ParsedCensusRow(
            rowNumber, lineNo, tag, category, ageText, ageMonths, weight, bcs,
            Cell(Column.Project), Cell(Column.Remarks), IsTicked(Cell(Column.Selected)), errors, warnings);
    }

    public static AnimalCategory? ParseCategory(string text) =>
        Normalise(text) switch
        {
            "p" or "pejantan" or "bull" => AnimalCategory.Bull,
            "i" or "induk" or "dam" or "cow" => AnimalCategory.Dam,
            "aj" or "anakjantan" or "malecalf" => AnimalCategory.MaleCalf,
            "ab" or "anakbetina" or "femalecalf" => AnimalCategory.FemaleCalf,
            _ => null,
        };

    /// <summary>"1 Tahun 6 Bulan" → 18; "8 Bulan" → 8; "1.5 years" → 18; "2y 3m" → 27.</summary>
    public static int? ParseAgeMonths(string text)
    {
        var matches = AgePart().Matches(text.ToLowerInvariant());
        if (matches.Count == 0) return null;

        decimal months = 0;
        foreach (Match m in matches)
        {
            var value = ParseDecimal(m.Groups["n"].Value);
            if (value is null) return null;
            months += m.Groups["unit"].Value.StartsWith('b') || m.Groups["unit"].Value.StartsWith('m') ? value.Value : value.Value * 12;
        }

        // Anything left over (other than separators) means we did not understand the text.
        var rest = AgePart().Replace(text.ToLowerInvariant(), "").Trim(' ', ',', '&', '+').Replace("dan", "").Replace("and", "").Trim();
        return rest.Length == 0 ? (int)Math.Round(months) : null;
    }

    public static string FormatAge(int months) =>
        (months / 12, months % 12) switch
        {
            (0, var m) => $"{m} mo",
            (var y, 0) => $"{y} yr",
            (var y, var m) => $"{y} yr {m} mo",
        };

    private static bool IsTicked(string? value) =>
        value is not null && Normalise(value) is "/" or "✓" or "✔" or "x" or "y" or "yes" or "ya" or "1" or "true" or "v";

    private static decimal? ParseDecimal(string text)
    {
        text = text.Trim().Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static Dictionary<Column, int> MapHeaders(string?[] row)
    {
        var map = new Dictionary<Column, int>();
        for (var i = 0; i < row.Length; i++)
        {
            if (row[i] is { } text && HeaderNames.TryGetValue(Normalise(text), out var column))
                map.TryAdd(column, i);
        }
        return map;
    }

    private static bool HasTickColumns(Dictionary<Column, int> columns) =>
        columns.ContainsKey(Column.Bull) && columns.ContainsKey(Column.Dam)
        && columns.ContainsKey(Column.MaleCalf) && columns.ContainsKey(Column.FemaleCalf);

    /// <summary>Lower-case and strip spaces, dots and brackets: "No. Tag" → "notag", "Berat (kg)" → "beratkg".</summary>
    private static string Normalise(string text) =>
        NonKeyChars().Replace(text.Trim().ToLowerInvariant(), "") is { Length: > 0 } key ? key : text.Trim();

    private static T AddError<T>(List<string> errors, string message)
    {
        errors.Add(message);
        return default!;
    }

    [GeneratedRegex(@"(?<n>\d+(?:[.,]\d+)?)\s*(?<unit>tahun|thn|years?|yrs?|y|bulan|bln|months?|mths?|mo|m)\b")]
    private static partial Regex AgePart();

    [GeneratedRegex(@"[\s\.\(\)\-_:]")]
    private static partial Regex NonKeyChars();
}
