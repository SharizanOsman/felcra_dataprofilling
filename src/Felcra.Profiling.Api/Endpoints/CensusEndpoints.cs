using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Application.Census;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Census;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>Census / headcount sessions and their per-animal entries.</summary>
internal static class CensusEndpoints
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapCensusEndpoints(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/api/census-sessions").WithTags("Census");

        sessions.MapGet("/", ListSessions);
        sessions.MapGet("/import-template", (ICensusImportService import) =>
            TypedResults.File(import.CreateTemplate(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "census-template.xlsx"));
        sessions.MapGet("/{id:guid}", GetSession);
        sessions.MapPost("/", CreateSession).RequireAuthorization(Policies.Manager).Validate<CensusSessionRequest>();
        sessions.MapPut("/{id:guid}", UpdateSession).RequireAuthorization(Policies.Manager).Validate<CensusSessionRequest>();
        sessions.MapDelete("/{id:guid}", DeleteSession).RequireAuthorization(Policies.Manager);

        sessions.MapGet("/{id:guid}/entries", ListEntries);
        sessions.MapPost("/{id:guid}/entries", CreateEntry).Validate<CensusEntryRequest>();
        sessions.MapPut("/{id:guid}/entries/{entryId:guid}", UpdateEntry).Validate<CensusEntryRequest>();
        sessions.MapDelete("/{id:guid}/entries/{entryId:guid}", DeleteEntry).RequireAuthorization(Policies.Manager);

        // Multipart uploads from the authenticated SPA; JWT bearer auth is not vulnerable to CSRF.
        sessions.MapPost("/{id:guid}/import/preview", PreviewImport).RequireAuthorization(Policies.Manager).DisableAntiforgery();
        sessions.MapPost("/{id:guid}/import", Import).RequireAuthorization(Policies.Manager).DisableAntiforgery();

        return app;
    }

    // ---- Sessions ------------------------------------------------------------------------------

    private static async Task<Ok<List<CensusSessionDto>>> ListSessions(AppDbContext db, CancellationToken ct, Guid? farmId = null)
    {
        var list = await db.CensusSessions.AsNoTracking()
            .Include(s => s.Treatments)
            .Where(s => farmId == null || s.FarmId == farmId)
            .OrderByDescending(s => s.StartDate)
            .ToListAsync(ct);
        return TypedResults.Ok(await ToDtosAsync(db, list, ct));
    }

    private static async Task<Results<Ok<CensusSessionDto>, NotFound>> GetSession(Guid id, AppDbContext db, CancellationToken ct)
    {
        var session = await db.CensusSessions.AsNoTracking().Include(s => s.Treatments).SingleOrDefaultAsync(s => s.Id == id, ct);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok((await ToDtosAsync(db, [session], ct))[0]);
    }

    private static async Task<Results<Created<CensusSessionDto>, NotFound, ValidationProblem>> CreateSession(
        CensusSessionRequest request, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Farms.AnyAsync(f => f.Id == request.FarmId, ct)) return TypedResults.NotFound();
        if (await ValidateTreatmentsAsync(db, request.TreatmentProductIds, ct) is { } problem) return problem;

        var session = new CensusSession { FarmId = request.FarmId, Title = request.Title.Trim() };
        Apply(request, session);
        session.Treatments = request.TreatmentProductIds.Distinct()
            .Select(t => new CensusSessionTreatment { CensusSessionId = session.Id, TreatmentProductId = t }).ToList();
        db.CensusSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/census-sessions/{session.Id}", (await ToDtosAsync(db, [session], ct))[0]);
    }

    private static async Task<Results<Ok<CensusSessionDto>, NotFound, ValidationProblem>> UpdateSession(
        Guid id, CensusSessionRequest request, AppDbContext db, CancellationToken ct)
    {
        var session = await db.CensusSessions.Include(s => s.Treatments).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return TypedResults.NotFound();
        if (await ValidateTreatmentsAsync(db, request.TreatmentProductIds, ct) is { } problem) return problem;

        // The farm cannot change: entries and animals belong to it.
        db.Entry(session).Property(s => s.Version).OriginalValue = request.Version;
        Apply(request, session);

        var wanted = request.TreatmentProductIds.Distinct().ToList();
        db.CensusSessionTreatments.RemoveRange(session.Treatments.Where(t => !wanted.Contains(t.TreatmentProductId)).ToList());
        db.CensusSessionTreatments.AddRange(wanted
            .Where(t => session.Treatments.All(st => st.TreatmentProductId != t))
            .Select(t => new CensusSessionTreatment { CensusSessionId = id, TreatmentProductId = t }));
        db.Entry(session).State = EntityState.Modified;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ToDtosAsync(db, [session], ct))[0]);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSession(Guid id, AppDbContext db, CancellationToken ct)
    {
        var session = await db.CensusSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return TypedResults.NotFound();

        db.CensusEntries.RemoveRange(await db.CensusEntries.Where(e => e.CensusSessionId == id).ToListAsync(ct));
        db.CensusSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void Apply(CensusSessionRequest request, CensusSession session)
    {
        session.Title = request.Title.Trim();
        session.StartDate = request.StartDate;
        session.EndDate = request.EndDate;
        session.PicName = request.PicName;
        session.PicPosition = request.PicPosition;
        session.PicPhone = request.PicPhone;
        session.Notes = request.Notes;
        session.RecordedBulls = request.RecordedBulls;
        session.RecordedDams = request.RecordedDams;
        session.RecordedMaleCalves = request.RecordedMaleCalves;
        session.RecordedFemaleCalves = request.RecordedFemaleCalves;
        session.IsClosed = request.IsClosed;
    }

    private static async Task<ValidationProblem?> ValidateTreatmentsAsync(AppDbContext db, List<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.TreatmentProducts.CountAsync(t => distinct.Contains(t.Id), ct) == distinct.Count
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["TreatmentProductIds"] = [T("One or more treatments do not exist.")] });
    }

    internal static async Task<List<CensusSessionDto>> ToDtosAsync(AppDbContext db, List<CensusSession> sessions, CancellationToken ct)
    {
        var ids = sessions.Select(s => s.Id).ToList();
        var farmIds = sessions.Select(s => s.FarmId).Distinct().ToList();
        var farmCodes = await db.Farms.Where(f => farmIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Code, ct);
        var counts = await db.CensusEntries.AsNoTracking()
            .Where(e => ids.Contains(e.CensusSessionId))
            .GroupBy(e => new { e.CensusSessionId, e.Category })
            .Select(g => new { g.Key.CensusSessionId, g.Key.Category, Count = g.Count(), Selected = g.Count(e => e.Selected) })
            .ToListAsync(ct);

        return sessions.Select(s =>
        {
            int Count(AnimalCategory c) => counts.Where(x => x.CensusSessionId == s.Id && x.Category == c).Sum(x => x.Count);
            return new CensusSessionDto(
                s.Id, s.FarmId, farmCodes.GetValueOrDefault(s.FarmId, "?"), s.Title, s.StartDate, s.EndDate,
                s.PicName, s.PicPosition, s.PicPhone, s.Notes,
                s.RecordedBulls, s.RecordedDams, s.RecordedMaleCalves, s.RecordedFemaleCalves, s.IsClosed,
                s.Treatments.Select(t => t.TreatmentProductId).ToList(),
                new CategoryCounts(Count(AnimalCategory.Bull), Count(AnimalCategory.Dam), Count(AnimalCategory.MaleCalf), Count(AnimalCategory.FemaleCalf)),
                counts.Where(x => x.CensusSessionId == s.Id).Sum(x => x.Selected),
                s.Version);
        }).ToList();
    }

    // ---- Entries -------------------------------------------------------------------------------

    private sealed record EntryRow(
        Guid Id, Guid AnimalId, int AnimalNo, string? TagNumber, bool HasDuplicateTag, int? LineNo, AnimalCategory Category,
        int? EstimatedAgeMonths, decimal? WeightKg, decimal? Bcs, string? ProjectCode, string? Remarks, bool Selected, uint Version);

    private static async Task<Results<Ok<List<CensusEntryDto>>, NotFound>> ListEntries(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.CensusSessions.AnyAsync(s => s.Id == id, ct)) return TypedResults.NotFound();
        return TypedResults.Ok(await QueryEntries(db, e => e.CensusSessionId == id, ct));
    }

    private static async Task<List<CensusEntryDto>> QueryEntries(
        AppDbContext db, System.Linq.Expressions.Expression<Func<CensusEntry, bool>> filter, CancellationToken ct)
    {
        var rows = await (
                from e in db.CensusEntries.AsNoTracking().Where(filter)
                join a in db.Animals on e.AnimalId equals a.Id
                orderby e.LineNo == null, e.LineNo, a.AnimalNo
                select new EntryRow(
                    e.Id, a.Id, a.AnimalNo, a.TagNumber,
                    a.TagNumber != null && db.Animals.Any(o =>
                        o.Id != a.Id && o.FarmId == a.FarmId && o.TagNumber == a.TagNumber && o.Status == AnimalStatus.Active),
                    e.LineNo, e.Category, e.EstimatedAgeMonths, e.WeightKg, e.BodyConditionScore,
                    db.Projects.Where(p => p.Id == a.ProjectId).Select(p => p.Code).FirstOrDefault(),
                    e.Remarks, e.Selected, e.Version))
            .ToListAsync(ct);

        return rows.Select(r => new CensusEntryDto(
            r.Id, r.AnimalId, Animal.FormatAnimalNo(r.AnimalNo), r.TagNumber, r.HasDuplicateTag, r.LineNo, r.Category,
            r.EstimatedAgeMonths, r.WeightKg, r.Bcs, r.ProjectCode, r.Remarks, r.Selected, r.Version)).ToList();
    }

    private static async Task<Results<Created<CensusEntryDto>, NotFound, ValidationProblem>> CreateEntry(
        Guid id, CensusEntryRequest request, AppDbContext db, CancellationToken ct)
    {
        var session = await db.CensusSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return TypedResults.NotFound();
        if (session.IsClosed) return Closed();
        if (!await db.Animals.AnyAsync(a => a.Id == request.AnimalId && a.FarmId == session.FarmId, ct))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["AnimalId"] = [T("Animal is not on this farm.")] });

        var entry = new CensusEntry { CensusSessionId = id, AnimalId = request.AnimalId, FarmId = session.FarmId };
        ApplyEntry(request, entry);
        db.CensusEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/census-sessions/{id}/entries/{entry.Id}", (await QueryEntries(db, e => e.Id == entry.Id, ct))[0]);
    }

    private static async Task<Results<Ok<CensusEntryDto>, NotFound, ValidationProblem>> UpdateEntry(
        Guid id, Guid entryId, CensusEntryRequest request, AppDbContext db, CancellationToken ct)
    {
        var session = await db.CensusSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
        var entry = await db.CensusEntries.SingleOrDefaultAsync(e => e.Id == entryId && e.CensusSessionId == id, ct);
        if (session is null || entry is null) return TypedResults.NotFound();
        if (session.IsClosed) return Closed();

        db.Entry(entry).Property(e => e.Version).OriginalValue = request.Version;
        ApplyEntry(request, entry);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await QueryEntries(db, e => e.Id == entryId, ct))[0]);
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteEntry(Guid id, Guid entryId, AppDbContext db, CancellationToken ct)
    {
        var session = await db.CensusSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
        var entry = await db.CensusEntries.SingleOrDefaultAsync(e => e.Id == entryId && e.CensusSessionId == id, ct);
        if (session is null || entry is null) return TypedResults.NotFound();
        if (session.IsClosed) return Closed();

        db.CensusEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void ApplyEntry(CensusEntryRequest request, CensusEntry entry)
    {
        // The animal of an entry is fixed; delete and re-add to correct a wrong animal.
        entry.Category = request.Category;
        entry.EstimatedAgeMonths = request.EstimatedAgeMonths;
        entry.WeightKg = request.WeightKg;
        entry.BodyConditionScore = request.BodyConditionScore;
        entry.Remarks = request.Remarks;
        entry.Selected = request.Selected;
    }

    private static ValidationProblem Closed() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Session"] = [T("This census session is closed. Reopen it to make changes.")] });

    // ---- Import --------------------------------------------------------------------------------

    private static async Task<Results<Ok<ImportPreviewDto>, NotFound, ValidationProblem>> PreviewImport(
        Guid id, IFormFile file, ICensusImportService import, AppDbContext db, CancellationToken ct, bool matchExisting = true)
    {
        if (await CheckUploadAsync(id, file, db, ct) is { } problem) return problem;

        await using var stream = file.OpenReadStream();
        var outcome = await import.PreviewAsync(id, stream, file.FileName, matchExisting, ct);
        return outcome switch
        {
            null => TypedResults.NotFound(),
            { FileError: { } error } => FileProblem(error),
            _ => TypedResults.Ok(outcome.Preview!),
        };
    }

    private static async Task<Results<Ok<ImportResultDto>, NotFound, ValidationProblem>> Import(
        Guid id, IFormFile file, ICensusImportService import, AppDbContext db, CancellationToken ct, bool matchExisting = true)
    {
        if (await CheckUploadAsync(id, file, db, ct) is { } problem) return problem;

        await using var stream = file.OpenReadStream();
        var outcome = await import.ImportAsync(id, stream, file.FileName, matchExisting, ct);
        return outcome switch
        {
            null => TypedResults.NotFound(),
            { FileError: { } error } => FileProblem(error),
            { Result: null } => FileProblem(T("{0} row(s) have errors. Preview the file and fix them first.", outcome.Preview!.RowsWithErrors)),
            _ => TypedResults.Ok(outcome.Result),
        };
    }

    private static async Task<ValidationProblem?> CheckUploadAsync(Guid id, IFormFile file, AppDbContext db, CancellationToken ct)
    {
        if (file.Length == 0) return FileProblem(T("The file is empty."));
        if (file.Length > MaxImportBytes) return FileProblem(T("The file is larger than 5 MB."));
        return await db.CensusSessions.AnyAsync(s => s.Id == id && s.IsClosed, ct) ? Closed() : null;
    }

    private static ValidationProblem FileProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["File"] = [message] });
}
