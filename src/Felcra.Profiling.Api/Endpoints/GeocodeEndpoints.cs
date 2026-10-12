using Felcra.Profiling.Api.Geocoding;
using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Contracts.Geo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>Place search for the farm map: by name, by GPS coordinates, or the name of a point on the map.</summary>
internal static class GeocodeEndpoints
{
    public static IEndpointRouteBuilder MapGeocodeEndpoints(this IEndpointRouteBuilder app)
    {
        var geo = app.MapGroup("/api/geocode").WithTags("Map").RequireAuthorization(Policies.Manager);
        geo.MapGet("/search", Search);
        geo.MapGet("/reverse", Reverse);
        return app;
    }

    /// <summary>Places in Malaysia matching the text. GPS coordinates (or a Google Maps link) return the place at that point.</summary>
    private static async Task<Results<Ok<IReadOnlyList<GeoPlaceDto>>, ValidationProblem, ProblemHttpResult>> Search(
        string? q, IGeocoder geocoder, IOptions<GeocodingOptions> options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2 || q.Length > 200)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["q"] = [T("Type at least 2 characters.")] });

        if (GeoCoordinates.TryParse(q, out var lat, out var lon))
        {
            var here = await TryAsync(() => geocoder.ReverseAsync(lat, lon, Language, ct), options);
            // The point itself is the answer, named after the place it is in when that is known.
            var point = new GeoPlaceDto(here.Value?.Name ?? T("GPS location"), here.Value?.DisplayName ?? $"{lat}, {lon}", lat, lon, here.Value?.State, "coordinates");
            return TypedResults.Ok<IReadOnlyList<GeoPlaceDto>>([point]);
        }

        var found = await TryAsync(() => geocoder.SearchAsync(q, Language, ct), options);
        return found.Problem is { } problem ? problem : TypedResults.Ok(found.Value ?? []);
    }

    private static async Task<Results<Ok<GeoPlaceDto>, NotFound, ValidationProblem, ProblemHttpResult>> Reverse(
        decimal lat, decimal lon, IGeocoder geocoder, IOptions<GeocodingOptions> options, CancellationToken ct)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["lat"] = [T("Not a valid GPS location.")] });
        var found = await TryAsync(() => geocoder.ReverseAsync(lat, lon, Language, ct), options);
        if (found.Problem is { } problem) return problem;
        return found.Value is { } place ? TypedResults.Ok(place) : TypedResults.NotFound();
    }

    /// <summary>The map service being down or switched off is reported, not thrown: the farm can still be placed by hand.</summary>
    private static async Task<(TResult? Value, ProblemHttpResult? Problem)> TryAsync<TResult>(Func<Task<TResult>> call, IOptions<GeocodingOptions> options)
    {
        if (!options.Value.Enabled)
            return (default, TypedResults.Problem(T("Place search is turned off on this server."), statusCode: StatusCodes.Status503ServiceUnavailable));
        try
        {
            return (await call(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (default, TypedResults.Problem(T("Place search is not available right now. Try again later, or click the map to place the farm."),
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }
}
