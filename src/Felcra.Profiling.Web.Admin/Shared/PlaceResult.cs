using Felcra.Profiling.Contracts.Geo;

namespace Felcra.Profiling.Web.Admin.Shared;

/// <summary>One line in the map search: a farm (<see cref="FarmId"/> set), a place or a GPS point.</summary>
public sealed record PlaceResult(string Title, string? Detail, decimal? Latitude, decimal? Longitude, Guid? FarmId, string? State, string? Type = null)
{
    public static PlaceResult From(GeoPlaceDto p) => new(p.Name, p.DisplayName, p.Latitude, p.Longitude, null, p.State, p.Type);
}
