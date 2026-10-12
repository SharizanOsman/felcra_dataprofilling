namespace Felcra.Profiling.Web.Admin.Shared;

/// <summary>Words the map script shows itself (layer switcher, popup link), in the current language.</summary>
internal static class MapLabels
{
    public static object Current => new { street = T("Street"), satellite = T("Satellite"), openFarm = T("Open farm") };
}
