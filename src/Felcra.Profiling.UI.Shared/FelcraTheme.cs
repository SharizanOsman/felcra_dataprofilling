using MudBlazor;

namespace Felcra.Profiling.UI.Shared;

public static class FelcraTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2E7D32",
            Secondary = "#6D4C41",
            AppbarBackground = "#1B5E20",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#66BB6A",
            Secondary = "#A1887F",
        },
    };
}
