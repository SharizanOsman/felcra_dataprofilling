using PdfSharp.Fonts;

namespace Felcra.Profiling.Api.Reports;

/// <summary>
/// Serves the bundled Carlito fonts (metric-compatible with Calibri, used by the original Pagoh report)
/// for every font request, so PDFs render the same on Windows and on a Linux server without fonts.
/// </summary>
internal sealed class ReportFonts : IFontResolver
{
    public const string Family = "Carlito";

    private static readonly Lock Gate = new();
    private static bool _registered;

    /// <summary>Installs the resolver once per process (PDFsharp's font settings are global).</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            GlobalFontSettings.FontResolver ??= new ReportFonts();
            _registered = true;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "Carlito-BoldItalic",
            (true, false) => "Carlito-Bold",
            (false, true) => "Carlito-Italic",
            _ => "Carlito-Regular",
        });

    public byte[] GetFont(string faceName)
    {
        using var stream = typeof(ReportFonts).Assembly.GetManifestResourceStream($"Fonts.{faceName}.ttf")
            ?? throw new InvalidOperationException($"Font {faceName} is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
