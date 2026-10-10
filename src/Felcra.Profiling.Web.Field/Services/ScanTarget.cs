using System.Text.RegularExpressions;

namespace Felcra.Profiling.Web.Field.Services;

/// <summary>Interprets the text of a scanned code: one of our QR labels, or anything else (treated as a tag number).</summary>
public static partial class ScanTarget
{
    /// <summary>Animal ID from a label link (".../field/a/{id}") or a bare GUID; null otherwise.</summary>
    public static Guid? AnimalId(string text)
    {
        text = text.Trim();
        if (Guid.TryParse(text, out var bare)) return bare;
        var match = LabelLink().Match(text);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"/field/a/(?<id>[0-9a-fA-F-]{32,36})(?:[/?#]|$)")]
    private static partial Regex LabelLink();
}
