namespace Felcra.Profiling.Application.Abstractions;

/// <summary>Calendar dates in Malaysian time. Servers run in UTC, which is 8 hours behind.</summary>
public static class LocalDate
{
    private static readonly TimeZoneInfo Malaysia = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Malaysia).DateTime);
}
