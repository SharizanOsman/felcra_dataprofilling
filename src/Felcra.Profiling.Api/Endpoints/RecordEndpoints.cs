using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Common;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>
/// Records made at the pen. Every request carries a device-generated ID: sending the same record
/// twice returns the stored one instead of creating a duplicate.
/// </summary>
internal static class RecordEndpoints
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;

    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var animal = app.MapGroup("/api/animals/{animalId:guid}").WithTags("Records");
        animal.MapGet("/records", GetRecords);
        animal.MapPost("/measurements", AddMeasurement).Validate<MeasurementRequest>();
        animal.MapPost("/treatments", AddTreatment).Validate<TreatmentRecordRequest>();
        animal.MapPost("/observations", AddObservation).Validate<ObservationRequest>();
        // Multipart upload from the authenticated SPA; JWT bearer auth is not exposed to CSRF.
        animal.MapPost("/photos", UploadPhoto).DisableAntiforgery();

        app.MapGet("/api/photos/{id:guid}", GetPhoto).WithTags("Records");

        // Corrections: managers can remove wrong records (soft delete, audited).
        app.MapDelete("/api/measurements/{id:guid}", (Guid id, AppDbContext db, CancellationToken ct) => Delete<Measurement>(db, id, ct))
            .WithTags("Records").RequireAuthorization(Policies.Manager);
        app.MapDelete("/api/treatment-records/{id:guid}", (Guid id, AppDbContext db, CancellationToken ct) => Delete<TreatmentRecord>(db, id, ct))
            .WithTags("Records").RequireAuthorization(Policies.Manager);
        app.MapDelete("/api/observations/{id:guid}", (Guid id, AppDbContext db, CancellationToken ct) => Delete<Observation>(db, id, ct))
            .WithTags("Records").RequireAuthorization(Policies.Manager);

        return app;
    }

    // ---- Read ----------------------------------------------------------------------------------

    private static async Task<Results<Ok<AnimalRecordsDto>, NotFound>> GetRecords(Guid animalId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Animals.AnyAsync(a => a.Id == animalId, ct)) return TypedResults.NotFound();

        var measurements = await db.Measurements.AsNoTracking().Where(m => m.AnimalId == animalId)
            .OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.CreatedAt).ToListAsync(ct);
        var treatments = await db.TreatmentRecords.AsNoTracking().Where(t => t.AnimalId == animalId)
            .OrderByDescending(t => t.GivenOn).ThenByDescending(t => t.CreatedAt).ToListAsync(ct);
        var observations = await db.Observations.AsNoTracking().Include(o => o.Photos).Where(o => o.AnimalId == animalId)
            .OrderByDescending(o => o.ObservedOn).ThenByDescending(o => o.CreatedAt).ToListAsync(ct);

        var names = await RecorderNamesAsync(db,
            measurements.Select(m => m.CreatedBy).Concat(treatments.Select(t => t.CreatedBy)).Concat(observations.Select(o => o.CreatedBy)), ct);
        var productIds = treatments.Select(t => t.TreatmentProductId).Distinct().ToList();
        var products = await db.TreatmentProducts.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return TypedResults.Ok(new AnimalRecordsDto(
            measurements.Select(m => ToDto(m, names)).ToList(),
            treatments.Select(t => ToDto(t, products.GetValueOrDefault(t.TreatmentProductId, "?"), names)).ToList(),
            observations.Select(o => ToDto(o, names)).ToList()));
    }

    internal static async Task<Dictionary<Guid, string>> RecorderNamesAsync(AppDbContext db, IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } g ? names.GetValueOrDefault(g) : null;

    private static MeasurementDto ToDto(Measurement m, Dictionary<Guid, string> names) =>
        new(m.Id, m.Type, m.Value, m.MeasuredOn, m.Method, m.Notes, Name(names, m.CreatedBy), m.CreatedAt);

    private static TreatmentRecordDto ToDto(TreatmentRecord t, string productName, Dictionary<Guid, string> names) =>
        new(t.Id, t.TreatmentProductId, productName, t.GivenOn, t.Dose, t.DoseUnit, t.Route, t.BatchNumber, t.NextDueOn,
            t.WithdrawalUntil, t.CensusSessionId, t.Notes, Name(names, t.CreatedBy), t.CreatedAt);

    private static ObservationDto ToDto(Observation o, Dictionary<Guid, string> names) =>
        new(o.Id, o.ObservedOn, o.Category, o.Notes, o.Photos.Where(p => !p.IsDeleted).Select(p => p.Id).ToList(),
            Name(names, o.CreatedBy), o.CreatedAt);

    // ---- Write ---------------------------------------------------------------------------------

    /// <summary>Loads the animal and refuses records for animals that are no longer on the farm.</summary>
    internal static async Task<(Animal? Animal, IResult? Problem)> LoadActiveAnimalAsync(AppDbContext db, Guid animalId, CancellationToken ct)
    {
        var animal = await db.Animals.AsNoTracking().SingleOrDefaultAsync(a => a.Id == animalId, ct);
        if (animal is null) return (null, TypedResults.NotFound());
        if (animal.Status != AnimalStatus.Active)
            return (null, TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Animal"] = [T("{0} is {1}; records can only be added for active animals.", Animal.FormatAnimalNo(animal.AnimalNo), T(ValidationText.FieldName(animal.Status.ToString())).ToLowerInvariant())],
            }));
        return (animal, null);
    }

    /// <summary>Same ID already stored: return it if it is for the same animal (a retry), else conflict.</summary>
    private static async Task<IResult?> ExistingAsync<TRecord>(AppDbContext db, Guid id, Guid animalId, Func<TRecord, Task<object>> toDto, CancellationToken ct)
        where TRecord : AuditableEntity
    {
        var existing = await db.Set<TRecord>().AsNoTracking().IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleOrDefaultAsync(e => e.Id == id, ct);
        if (existing is null) return null;
        var sameAnimal = existing switch
        {
            Measurement m => m.AnimalId == animalId,
            TreatmentRecord t => t.AnimalId == animalId,
            Observation o => o.AnimalId == animalId,
            _ => false,
        };
        return sameAnimal
            ? TypedResults.Ok(await toDto(existing))
            : TypedResults.Problem(T("A record with this ID already exists."), statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> AddMeasurement(Guid animalId, MeasurementRequest request, AppDbContext db, CancellationToken ct)
    {
        var (animal, problem) = await LoadActiveAnimalAsync(db, animalId, ct);
        if (problem is not null) return problem;
        if (await ExistingAsync<Measurement>(db, request.Id, animalId,
                async m => ToDto(m, await RecorderNamesAsync(db, [m.CreatedBy], ct)), ct) is { } existing)
            return existing;

        var measurement = new Measurement
        {
            Id = request.Id,
            AnimalId = animalId,
            FarmId = animal!.FarmId,
            Type = request.Type,
            Value = request.Value!.Value,
            MeasuredOn = request.MeasuredOn,
            Method = request.Type == MeasurementType.Weight ? request.Method : null,
            Notes = request.Notes,
        };
        db.Measurements.Add(measurement);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/animals/{animalId}/records", ToDto(measurement, await RecorderNamesAsync(db, [measurement.CreatedBy], ct)));
    }

    private static async Task<IResult> AddTreatment(Guid animalId, TreatmentRecordRequest request, AppDbContext db, CancellationToken ct)
    {
        var (animal, problem) = await LoadActiveAnimalAsync(db, animalId, ct);
        if (problem is not null) return problem;

        var product = await db.TreatmentProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.TreatmentProductId, ct);
        if (await ExistingAsync<TreatmentRecord>(db, request.Id, animalId,
                async t => ToDto(t, product?.Name ?? "?", await RecorderNamesAsync(db, [t.CreatedBy], ct)), ct) is { } existing)
            return existing;
        if (product is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["TreatmentProductId"] = [T("Treatment does not exist.")] });
        if (request.CensusSessionId is { } sessionId && !await db.CensusSessions.AnyAsync(s => s.Id == sessionId && s.FarmId == animal!.FarmId, ct))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["CensusSessionId"] = [T("Programme is not on this animal's farm.")] });

        var record = new TreatmentRecord
        {
            Id = request.Id,
            AnimalId = animalId,
            FarmId = animal!.FarmId,
            TreatmentProductId = product.Id,
            GivenOn = request.GivenOn,
            Dose = request.Dose ?? product.DefaultDose,
            DoseUnit = request.DoseUnit ?? product.Unit,
            Route = request.Route,
            BatchNumber = request.BatchNumber,
            NextDueOn = request.NextDueOn,
            WithdrawalUntil = product.WithdrawalDays is > 0 ? request.GivenOn.AddDays(product.WithdrawalDays.Value) : null,
            CensusSessionId = request.CensusSessionId,
            Notes = request.Notes,
        };
        db.TreatmentRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/animals/{animalId}/records", ToDto(record, product.Name, await RecorderNamesAsync(db, [record.CreatedBy], ct)));
    }

    private static async Task<IResult> AddObservation(Guid animalId, ObservationRequest request, AppDbContext db, CancellationToken ct)
    {
        var (animal, problem) = await LoadActiveAnimalAsync(db, animalId, ct);
        if (problem is not null) return problem;
        if (await ExistingAsync<Observation>(db, request.Id, animalId, async o =>
            {
                var withPhotos = await db.Observations.AsNoTracking().Include(x => x.Photos).SingleAsync(x => x.Id == o.Id, ct);
                return ToDto(withPhotos, await RecorderNamesAsync(db, [o.CreatedBy], ct));
            }, ct) is { } existing)
            return existing;

        var photoIds = request.PhotoIds.Distinct().ToList();
        var photos = await db.Photos.Where(p => photoIds.Contains(p.Id) && p.AnimalId == animalId && p.ObservationId == null).ToListAsync(ct);
        if (photos.Count != photoIds.Count)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["PhotoIds"] = [T("One or more photos were not uploaded for this animal.")] });

        var observation = new Observation
        {
            Id = request.Id,
            AnimalId = animalId,
            FarmId = animal!.FarmId,
            ObservedOn = request.ObservedOn,
            Category = request.Category,
            Notes = request.Notes.Trim(),
        };
        db.Observations.Add(observation);
        foreach (var photo in photos) photo.ObservationId = observation.Id;
        await db.SaveChangesAsync(ct);

        observation.Photos = photos;
        return TypedResults.Created($"/api/animals/{animalId}/records", ToDto(observation, await RecorderNamesAsync(db, [observation.CreatedBy], ct)));
    }

    /// <param name="id">Optional device-generated photo ID, so a retried upload is stored once.</param>
    private static async Task<IResult> UploadPhoto(
        Guid animalId, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] Guid? id, AppDbContext db, IFileStore store, CancellationToken ct)
    {
        var (animal, problem) = await LoadActiveAnimalAsync(db, animalId, ct);
        if (problem is not null) return problem;

        if (id is { } photoId && await db.Photos.AsNoTracking().IgnoreQueryFilters([QueryFilters.SoftDelete])
                .SingleOrDefaultAsync(p => p.Id == photoId, ct) is { } existing)
        {
            return existing.AnimalId == animalId
                ? TypedResults.Ok(new PhotoDto(existing.Id, existing.ContentType, existing.SizeBytes))
                : TypedResults.Problem(T("A photo with this ID already exists."), statusCode: StatusCodes.Status409Conflict);
        }
        if (file.Length == 0 || file.Length > MaxPhotoBytes)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["File"] = [T("Photos must be between 1 byte and 8 MB.")] });

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (ImageType(header.AsSpan(0, read)) is not var (contentType, extension))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["File"] = [T("Only JPEG, PNG or WebP photos are accepted.")] });

        stream.Position = 0;
        var key = await store.SaveAsync(stream, extension, ct);
        var photo = new Photo { AnimalId = animalId, FarmId = animal!.FarmId, StorageKey = key, ContentType = contentType, SizeBytes = file.Length };
        if (id is { } clientId) photo.Id = clientId;
        db.Photos.Add(photo);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/photos/{photo.Id}", new PhotoDto(photo.Id, photo.ContentType, photo.SizeBytes));
    }

    /// <summary>Detects the image type from its first bytes (the declared content type is not trusted).</summary>
    private static (string ContentType, string Extension)? ImageType(ReadOnlySpan<byte> header) =>
        header switch
        {
            [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", ".jpg"),
            [0x89, 0x50, 0x4E, 0x47, ..] => ("image/png", ".png"),
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ("image/webp", ".webp"),
            _ => null,
        };

    private static async Task<IResult> GetPhoto(Guid id, AppDbContext db, IFileStore store, CancellationToken ct)
    {
        var photo = await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (photo is null || store.OpenRead(photo.StorageKey) is not { } stream) return TypedResults.NotFound();
        return TypedResults.File(stream, photo.ContentType, enableRangeProcessing: false);
    }

    private static async Task<Results<NoContent, NotFound>> Delete<T>(AppDbContext db, Guid id, CancellationToken ct) where T : AuditableEntity
    {
        var record = await db.Set<T>().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (record is null) return TypedResults.NotFound();
        db.Remove(record);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
