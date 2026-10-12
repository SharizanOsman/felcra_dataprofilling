using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Geo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Felcra.Profiling.Api.Geocoding;

public sealed class GeocodingOptions
{
    public const string Section = "Geocoding";

    /// <summary>Nominatim server. The public one allows light use with an identifying User-Agent (one request a second).</summary>
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";

    /// <summary>Identifies this application to the Nominatim operators, as their usage policy requires.</summary>
    public string UserAgent { get; set; } = "FELCRA-Cattle-Profiling/1.0";

    /// <summary>Optional contact address sent with each request, so the operators can get in touch instead of blocking.</summary>
    public string? Email { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>Finds places by name and names GPS points, within Malaysia.</summary>
public interface IGeocoder
{
    Task<IReadOnlyList<GeoPlaceDto>> SearchAsync(string query, string language, CancellationToken ct);

    Task<GeoPlaceDto?> ReverseAsync(decimal latitude, decimal longitude, string language, CancellationToken ct);
}

/// <summary>
/// OpenStreetMap's Nominatim, called from the server so the browser only talks to this site, every request carries
/// the application's User-Agent, results are cached, and the public server's one-request-a-second limit is kept.
/// </summary>
internal sealed class NominatimGeocoder(HttpClient http, IMemoryCache cache, IOptions<GeocodingOptions> options, TimeProvider clock) : IGeocoder
{
    public const string HttpClientName = "nominatim";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastRequest;
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(12);

    public async Task<IReadOnlyList<GeoPlaceDto>> SearchAsync(string query, string language, CancellationToken ct)
    {
        query = query.Trim();
        return await cache.GetOrCreateAsync($"geo:search:{language}:{query.ToLowerInvariant()}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var places = await GetAsync<List<NominatimPlace>>(
                $"search?q={Uri.EscapeDataString(query)}&countrycodes=my&limit=8&addressdetails=1&format=jsonv2", language, ct);
            return (IReadOnlyList<GeoPlaceDto>)(places ?? []).Select(ToDto).OfType<GeoPlaceDto>().ToList();
        }) ?? [];
    }

    public async Task<GeoPlaceDto?> ReverseAsync(decimal latitude, decimal longitude, string language, CancellationToken ct)
    {
        var (lat, lon) = (Math.Round(latitude, 5).ToString(CultureInfo.InvariantCulture), Math.Round(longitude, 5).ToString(CultureInfo.InvariantCulture));
        return await cache.GetOrCreateAsync($"geo:reverse:{language}:{lat},{lon}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var place = await GetAsync<NominatimPlace>($"reverse?lat={lat}&lon={lon}&zoom=14&addressdetails=1&format=jsonv2", language, ct);
            return place is { Error: null } ? ToDto(place) : null;
        });
    }

    private async Task<TResult?> GetAsync<TResult>(string query, string language, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"{query}&accept-language={language},{(language == Malay ? English : Malay)}" +
                  (o.Email is { Length: > 0 } email ? $"&email={Uri.EscapeDataString(email)}" : "");

        await Gate.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + TimeSpan.FromSeconds(1) - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);
            _lastRequest = clock.GetUtcNow();
            return await http.GetFromJsonAsync<TResult>(url, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static GeoPlaceDto? ToDto(NominatimPlace p)
    {
        if (!decimal.TryParse(p.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !decimal.TryParse(p.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return null;
        var a = p.Address;
        var name = p.Name is { Length: > 0 } n ? n
            : a?.Village ?? a?.Town ?? a?.City ?? a?.Suburb ?? a?.County ?? p.DisplayName?.Split(',')[0] ?? "";
        return new GeoPlaceDto(name, p.DisplayName ?? name, Math.Round(lat, 6), Math.Round(lon, 6),
            MalaysianStates.Normalize(a?.State ?? a?.Territory ?? a?.City), p.Type);
    }

    private sealed record NominatimPlace(
        string? Lat, string? Lon, string? Name,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        string? Type, NominatimAddress? Address, string? Error);

    private sealed record NominatimAddress(
        string? Village, string? Town, string? City, string? Suburb, string? County, string? State, string? Territory);
}
