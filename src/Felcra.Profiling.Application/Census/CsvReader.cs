using System.Text;

namespace Felcra.Profiling.Application.Census;

/// <summary>Minimal RFC 4180 CSV reader (quoted fields, embedded delimiters, quotes and newlines).</summary>
public static class CsvReader
{
    public static List<string?[]> Read(string text)
    {
        // Excel in some locales saves "CSV" with semicolons; pick whichever the first line uses more.
        var firstLine = text.AsSpan()[..Math.Max(0, text.IndexOf('\n') is var i and >= 0 ? i : text.Length)];
        var delimiter = firstLine.Count(';') > firstLine.Count(',') ? ';' : ',';

        var rows = new List<string?[]>();
        var row = new List<string?>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var p = 0; p < text.Length; p++)
        {
            var c = text[p];
            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (p + 1 < text.Length && text[p + 1] == '"') { field.Append('"'); p++; }
                else inQuotes = false;
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().TrimEnd('\r'));
            rows.Add([.. row]);
        }
        return rows;
    }
}
