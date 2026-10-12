using Felcra.Profiling.Api.Geocoding;
using Felcra.Profiling.Contracts.Geo;

namespace Felcra.Profiling.Api.IntegrationTests;

/// <summary>Answers place searches without calling OpenStreetMap. "offline" in the query fails like a down service.</summary>
internal sealed class FakeGeocoder : IGeocoder
{
    public static readonly GeoPlaceDto Pagoh = new("Pagoh", "Pagoh, Muar, Johor, 84600, Malaysia", 2.1483m, 102.7712m, "Johor", "town");

    public List<string> Languages { get; } = [];

    public Task<IReadOnlyList<GeoPlaceDto>> SearchAsync(string query, string language, CancellationToken ct)
    {
        Languages.Add(language);
        if (query.Contains("offline")) throw new HttpRequestException("down");
        IReadOnlyList<GeoPlaceDto> found = query.Contains("pagoh", StringComparison.OrdinalIgnoreCase) ? [Pagoh] : [];
        return Task.FromResult(found);
    }

    public Task<GeoPlaceDto?> ReverseAsync(decimal latitude, decimal longitude, string language, CancellationToken ct)
    {
        Languages.Add(language);
        return Task.FromResult<GeoPlaceDto?>(Math.Abs(latitude - Pagoh.Latitude) < 0.05m ? Pagoh : null);
    }
}
