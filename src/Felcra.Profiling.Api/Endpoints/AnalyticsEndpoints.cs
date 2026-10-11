using Felcra.Profiling.Api.Analytics;
using Felcra.Profiling.Contracts.Analytics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>
/// Growth and lactation analysis and the HQ dashboard. Least-cost rations are formulated in the
/// browser / phone with the same Analytics library, so they work offline.
/// </summary>
internal static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");
        group.MapGet("/growth", Growth);
        group.MapGet("/lactation", Lactation);
        group.MapGet("/dashboard", Dashboard);
        return app;
    }

    private static async Task<Ok<GrowthAnalysisDto>> Growth(AnalyticsQueries analytics, CancellationToken ct, Guid? farmId = null) =>
        TypedResults.Ok(await analytics.GrowthAsync(farmId, ct));

    private static async Task<Ok<LactationAnalysisDto>> Lactation(AnalyticsQueries analytics, CancellationToken ct, Guid? farmId = null) =>
        TypedResults.Ok(await analytics.LactationAsync(farmId, ct));

    private static async Task<Ok<DashboardDto>> Dashboard(AnalyticsQueries analytics, CancellationToken ct, Guid? farmId = null) =>
        TypedResults.Ok(await analytics.DashboardAsync(farmId, ct));
}
