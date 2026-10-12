using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Api.Reports;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Reports;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>PDF / Excel reports for HQ and farm managers, and the monthly report e-mail.</summary>
internal static class ReportEndpoints
{
    /// <summary>Longest period a report may cover.</summary>
    private const int MaxPeriodDays = 3 * 366;

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization(Policies.Manager);
        group.MapGet("/census-sessions/{id:guid}/form", CensusForm);
        group.MapGet("/census-sessions/{id:guid}/selected", SelectedAnimals);
        group.MapGet("/treatments", Treatments);
        group.MapGet("/performance", Performance);
        group.MapGet("/culling-candidates", Culling);
        group.MapGet("/monthly", MonthlyStatus).RequireAuthorization(Policies.HqAdmin);
        group.MapPost("/monthly/test", SendTest).RequireAuthorization(Policies.HqAdmin);
        return app;
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> CensusForm(
        Guid id, ReportService reports, CancellationToken ct, string format = "pdf") =>
        Parse(format, out var f) is { } bad ? bad : File(await reports.CensusFormAsync(id, f, ct));

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> SelectedAnimals(
        Guid id, ReportService reports, CancellationToken ct, string format = "pdf") =>
        Parse(format, out var f) is { } bad ? bad : File(await reports.SelectedAnimalsAsync(id, f, ct));

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> Treatments(
        ReportService reports, CancellationToken ct, Guid? farmId = null, DateOnly? from = null, DateOnly? to = null, string format = "pdf")
    {
        if (Parse(format, out var f) is { } bad) return bad;
        if (Period(reports, from, to, out var start, out var end) is { } badPeriod) return badPeriod;
        return File(await reports.TreatmentsAsync(farmId, start, end, f, ct));
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> Performance(
        ReportService reports, CancellationToken ct, Guid? farmId = null, DateOnly? from = null, DateOnly? to = null, string format = "pdf")
    {
        if (Parse(format, out var f) is { } bad) return bad;
        if (Period(reports, from, to, out var start, out var end) is { } badPeriod) return badPeriod;
        return File(await reports.PerformanceAsync(farmId, start, end, f, ct));
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> Culling(
        ReportService reports, CancellationToken ct, Guid? farmId = null, string format = "pdf") =>
        Parse(format, out var f) is { } bad ? bad : File(await reports.CullingAsync(farmId, f, ct));

    private static async Task<Ok<MonthlyReportStatusDto>> MonthlyStatus(MonthlyReportJob job, CancellationToken ct) =>
        TypedResults.Ok(await job.StatusAsync(ct));

    /// <summary>Sends last month's reports to the signed-in HQ user only, to check the e-mail set-up.</summary>
    private static async Task<Results<Ok<TestEmailResultDto>, ValidationProblem>> SendTest(
        MonthlyReportJob job, IEmailSender email, ICurrentUser user, AppDbContext db, CancellationToken ct, Guid? farmId = null)
    {
        if (!email.IsConfigured)
            return Invalid("Email", T("E-mail is not configured on the server (Email:From and Email:Smtp:Host)."));
        var me = await db.Users.AsNoTracking().Where(u => u.Id == user.UserId).Select(u => new { u.Email, u.FullName }).SingleAsync(ct);
        var message = await job.TestMessageAsync(me.Email!, me.FullName, farmId, ct);
        await email.SendAsync(message, ct);
        return TypedResults.Ok(new TestEmailResultDto(me.Email!, message.Attachments.Select(a => a.FileName).ToList()));
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static Results<FileContentHttpResult, NotFound, ValidationProblem> File(ReportFile? file) =>
        file is null ? TypedResults.NotFound() : TypedResults.File(file.Content, file.ContentType, file.FileName);

    private static ValidationProblem? Parse(string format, out ReportFormat value)
    {
        (var ok, value) = format.ToLowerInvariant() switch
        {
            "pdf" => (true, ReportFormat.Pdf),
            "xlsx" or "excel" => (true, ReportFormat.Xlsx),
            _ => (false, ReportFormat.Pdf),
        };
        return ok ? null : Invalid("format", T("Format must be pdf or xlsx."));
    }

    /// <summary>Defaults to last calendar month.</summary>
    private static ValidationProblem? Period(ReportService reports, DateOnly? from, DateOnly? to, out DateOnly start, out DateOnly end)
    {
        var lastMonth = reports.LastMonth();
        (start, end) = (from ?? (to is { } t ? new DateOnly(t.Year, t.Month, 1) : lastMonth.From), to ?? (from is null ? lastMonth.To : reports.Today));
        if (end < start) return Invalid("to", T("The end date must be on or after the start date."));
        if (end.DayNumber - start.DayNumber > MaxPeriodDays) return Invalid("from", T("A report can cover at most 3 years."));
        return null;
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
