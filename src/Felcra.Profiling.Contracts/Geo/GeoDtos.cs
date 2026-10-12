using System.Globalization;
using System.Text.RegularExpressions;

namespace Felcra.Profiling.Contracts.Geo;

/// <summary>A place found by name, or the place at a GPS point (OpenStreetMap / Nominatim).</summary>
/// <param name="Name">Short name, e.g. "Pagoh".</param>
/// <param name="DisplayName">Full address, e.g. "Pagoh, Muar, Johor, 84600, Malaysia".</param>
/// <param name="State">The Malaysian state as spelled in <see cref="Farms.MalaysianStates"/>, when it is one.</param>
public sealed record GeoPlaceDto(string Name, string DisplayName, decimal Latitude, decimal Longitude, string? State, string? Type);

/// <summary>Reads GPS coordinates typed or pasted by a user.</summary>
public static partial class GeoCoordinates
{
    /// <summary>
    /// Accepts "2.1483, 102.7712", "2.1483 102.7712", Google Maps links (".../@2.1483,102.7712,15z" or "?q=2.1483,102.7712")
    /// and degrees/minutes/seconds such as 2°08'53.9"N 102°46'16.3"E. Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, out decimal latitude, out decimal longitude)
    {
        latitude = longitude = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = Uri.UnescapeDataString(text.Trim());

        if (Dms().Match(text) is { Success: true } dms)
        {
            latitude = FromDms(dms, "lat");
            longitude = FromDms(dms, "lon");
            return InRange(latitude, longitude);
        }

        // Google Maps puts the map centre after "@" and a searched point after "q=" / "query=" / "ll=".
        var match = Link().Match(text);
        if (!match.Success) match = Pair().Match(text);
        if (!match.Success) return false;
        if (!decimal.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
            || !decimal.TryParse(match.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
            return false;
        latitude = Math.Round(latitude, 6, MidpointRounding.AwayFromZero);
        longitude = Math.Round(longitude, 6, MidpointRounding.AwayFromZero);
        return InRange(latitude, longitude);
    }

    private static decimal FromDms(Match m, string part)
    {
        var value = decimal.Parse(m.Groups[part + "d"].Value, CultureInfo.InvariantCulture)
                    + (m.Groups[part + "m"].Success ? decimal.Parse(m.Groups[part + "m"].Value, CultureInfo.InvariantCulture) / 60 : 0)
                    + (m.Groups[part + "s"].Success ? decimal.Parse(m.Groups[part + "s"].Value, CultureInfo.InvariantCulture) / 3600 : 0);
        var hemisphere = m.Groups[part + "h"].Value.ToUpperInvariant();
        return Math.Round(hemisphere is "S" or "W" ? -value : value, 6, MidpointRounding.AwayFromZero);
    }

    private static bool InRange(decimal latitude, decimal longitude) => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    [GeneratedRegex(@"(?:@|[?&](?:q|query|ll)=)(?<lat>-?\d{1,2}(?:\.\d+)?),\s*(?<lon>-?\d{1,3}(?:\.\d+)?)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^\s*\(?\s*(?<lat>-?\d{1,2}(?:\.\d+)?)\s*[,;\s]\s*(?<lon>-?\d{1,3}(?:\.\d+)?)\s*\)?\s*$")]
    private static partial Regex Pair();

    [GeneratedRegex("""(?<latd>\d{1,2})\s*°\s*(?:(?<latm>\d{1,2}(?:\.\d+)?)\s*['′]\s*)?(?:(?<lats>\d{1,2}(?:\.\d+)?)\s*(?:"|″|'')\s*)?(?<lath>[NS])[\s,]+(?<lond>\d{1,3})\s*°\s*(?:(?<lonm>\d{1,2}(?:\.\d+)?)\s*['′]\s*)?(?:(?<lons>\d{1,2}(?:\.\d+)?)\s*(?:"|″|'')\s*)?(?<lonh>[EW])""", RegexOptions.IgnoreCase)]
    private static partial Regex Dms();
}
