using Felcra.Profiling.Contracts.Common;
using MudBlazor;

namespace Felcra.Profiling.UI.Shared;

/// <summary>Colours from the FFISB logo: navy as the main colour, red as the accent.</summary>
public static class FelcraTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Brand.Navy,
            PrimaryDarken = "#1C2358",
            PrimaryLighten = "#5C66B0",
            Secondary = Brand.Red,
            Tertiary = "#5C66B0",
            AppbarBackground = Brand.Navy,
            AppbarText = "#FFFFFF",
            DrawerIcon = Brand.Navy,
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#8E97E0",
            Secondary = "#FF6B70",
            Tertiary = "#A9B0EA",
            AppbarBackground = "#1C2358",
        },
    };
}
