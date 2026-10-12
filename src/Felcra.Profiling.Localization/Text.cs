using System.Globalization;

namespace Felcra.Profiling.Localization;

/// <summary>
/// UI and message text in English or Malay (Bahasa Melayu). The English text is the key, so code reads naturally
/// and anything not yet translated falls back to English. The language is the current UI culture: the clients start
/// in the culture the user picked, and the API takes it from each request's Accept-Language header.
/// </summary>
/// <example><c>T("Farms")</c>, <c>T("{0} farms in {1} states", farms, states)</c></example>
public static class Text
{
    public const string English = "en";
    public const string Malay = "ms";

    /// <summary>The languages a user can choose, in the order shown.</summary>
    public static readonly IReadOnlyList<string> Languages = [Malay, English];

    /// <summary>The language used when the user has not chosen one.</summary>
    public const string DefaultLanguage = Malay;

    /// <summary>The language of the current UI culture: Malay for any "ms" culture, otherwise English.</summary>
    public static string Language => IsMalay(CultureInfo.CurrentUICulture) ? Malay : English;

    /// <summary>The text in the current language.</summary>
    public static string T(string english) =>
        Language == Malay && MalayText.Entries.TryGetValue(english, out var malay) ? malay : english;

    /// <summary>The text in the current language, with <c>{0}</c>, <c>{1}</c>… filled in.</summary>
    public static string T(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>The culture an app runs in for a language (Malaysian formats either way).</summary>
    public static string CultureName(string language) => language == Malay ? "ms-MY" : "en-MY";

    /// <summary>The language of an Accept-Language header or a saved choice (first preference); <c>null</c> if neither.</summary>
    public static string? Parse(string? value)
    {
        var first = value?.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(first)) return null;
        if (first.StartsWith(Malay, StringComparison.OrdinalIgnoreCase)) return Malay;
        return first.StartsWith(English, StringComparison.OrdinalIgnoreCase) ? English : null;
    }

    /// <summary>The name of a language, written in that language (for the language switch).</summary>
    public static string NativeName(string language) => language == Malay ? "Bahasa Melayu" : "English";

    /// <summary>Switches the current UI culture to a language until disposed (e.g. for a scheduled e-mail).</summary>
    public static IDisposable Use(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CultureName(language));
        return new Restore(previous);
    }

    private static bool IsMalay(CultureInfo culture) => culture.TwoLetterISOLanguageName == Malay;

    private sealed class Restore(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
