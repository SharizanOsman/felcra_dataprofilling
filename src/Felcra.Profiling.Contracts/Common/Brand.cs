namespace Felcra.Profiling.Contracts.Common;

/// <summary>
/// Product name and logo colours. The name is a brand and stays in English in both languages,
/// so it is never passed through the translator.
/// </summary>
public static class Brand
{
    public const string Name = "FFISB LIVESTOCK 360";
    public const string Tagline = "Integrated Livestock Data Management System";
    public const string FullName = Name + " — " + Tagline;

    /// <summary>Logo navy.</summary>
    public const string Navy = "#2A347F";

    /// <summary>Logo red.</summary>
    public const string Red = "#E41E26";
}
