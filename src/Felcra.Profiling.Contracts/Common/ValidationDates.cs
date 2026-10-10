namespace Felcra.Profiling.Contracts.Common;

public static class ValidationDates
{
    /// <summary>
    /// The latest date that is "today" somewhere (UTC+14). Validators run on the browser and on a UTC
    /// server; comparing with the server's DateTime.Today would reject records made in Malaysia
    /// between midnight and 8 am.
    /// </summary>
    public static DateOnly LatestToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14));
}
