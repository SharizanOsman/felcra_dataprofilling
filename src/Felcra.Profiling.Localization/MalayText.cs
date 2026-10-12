using System.Collections.Frozen;

namespace Felcra.Profiling.Localization;

/// <summary>
/// Malay for each English text, split by the English text's first letter (MalayText.A-C.cs and so on) plus the words built
/// at run time (MalayText.Words.cs). Keep <c>{0}</c>-style placeholders as they are; tests check they match and that every
/// <c>T("…")</c> in the code has an entry.
/// </summary>
internal static partial class MalayText
{
    public static readonly FrozenDictionary<string, string> Entries = Build();

    /// <summary>Every pair as written, duplicates included (for the tests).</summary>
    internal static IEnumerable<(string English, string Malay)> Pairs =>
        Symbols().Concat(AtoC()).Concat(DtoL()).Concat(MtoR()).Concat(StoZ()).Concat(Words());

    private static FrozenDictionary<string, string> Build()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (english, malay) in Pairs) entries.TryAdd(english, malay);
        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
