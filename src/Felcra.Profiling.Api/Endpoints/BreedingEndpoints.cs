using Felcra.Profiling.Api.Breeding;
using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>Breeding events, reproduction status, herd figures, culling candidates and reminders.</summary>
internal static class BreedingEndpoints
{
    public static IEndpointRouteBuilder MapBreedingEndpoints(this IEndpointRouteBuilder app)
    {
        var animal = app.MapGroup("/api/animals/{animalId:guid}").WithTags("Breeding");
        animal.MapGet("/breeding", GetAnimalBreeding);
        animal.MapPost("/breeding-events", AddEvent).Validate<BreedingEventRequest>();

        app.MapDelete("/api/breeding-events/{id:guid}", DeleteEvent).WithTags("Breeding").RequireAuthorization(Policies.Manager);

        var herd = app.MapGroup("/api/breeding").WithTags("Breeding");
        herd.MapGet("/summary", async (BreedingQueries queries, CancellationToken ct, Guid? farmId = null) =>
            TypedResults.Ok(await queries.SummaryAsync(farmId, ct)));
        herd.MapGet("/culling-candidates", async (BreedingQueries queries, CancellationToken ct, Guid? farmId = null) =>
                TypedResults.Ok(await queries.CullingCandidatesAsync(farmId, ct)))
            .RequireAuthorization(Policies.Manager);

        app.MapGet("/api/reminders", async (BreedingQueries queries, CancellationToken ct, Guid? farmId = null, int days = 14) =>
                TypedResults.Ok(await queries.RemindersAsync(farmId, Math.Clamp(days, 0, 90), ct)))
            .WithTags("Breeding");

        return app;
    }

    private static async Task<Results<Ok<AnimalBreedingDto>, NotFound>> GetAnimalBreeding(
        Guid animalId, AppDbContext db, BreedingQueries queries, CancellationToken ct)
    {
        var animal = await db.Animals.AsNoTracking().SingleOrDefaultAsync(a => a.Id == animalId, ct);
        if (animal is null) return TypedResults.NotFound();

        var events = await db.BreedingEvents.AsNoTracking().Where(e => e.AnimalId == animalId)
            .OrderByDescending(e => e.EventDate).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
        var offspring = await db.Animals.AsNoTracking()
            .Where(a => a.DamId == animalId || a.SireId == animalId)
            .OrderByDescending(a => a.BirthDate)
            .Select(a => new { a.Id, a.AnimalNo, a.TagNumber, a.Sex, a.BirthDate, a.Status, a.BirthEventId })
            .ToListAsync(ct);

        ReproductionDto? reproduction = null;
        List<string> reasons = [];
        if (animal.Sex == Sex.Female)
        {
            var status = queries.Evaluate(events.Select(e => new Application.Breeding.EventFact(e.EventDate, e.Type, e.PregnancyResult, e.GestationDays)));
            reproduction = new ReproductionDto(status.State, status.LastCalving, status.CalvingIntervalDays, status.LastService,
                status.ExpectedCalving, status.PregnancyCheckDue, status.DaysOpen, status.ConsecutiveOpenChecks, status.CalvingCount);
            if (animal.Status == AnimalStatus.Active && animal.Category == AnimalCategory.Dam)
            {
                var bcs = await db.Measurements.AsNoTracking()
                    .Where(m => m.AnimalId == animalId && m.Type == MeasurementType.BodyCondition)
                    .OrderByDescending(m => m.MeasuredOn).Select(m => (decimal?)m.Value).FirstOrDefaultAsync(ct);
                reasons = queries.CullingReasons(status, animal.BirthDate, bcs);
            }
        }

        var names = await RecordEndpoints.RecorderNamesAsync(db, events.Select(e => e.CreatedBy), ct);
        var sireLabels = await SireLabelsAsync(db, events.Select(e => e.SireId), ct);
        return TypedResults.Ok(new AnimalBreedingDto(
            reproduction,
            events.Select(e => ToDto(e, sireLabels, names, offspring.Where(o => o.BirthEventId == e.Id).Select(o => o.Id).ToList())).ToList(),
            offspring.Select(o => new OffspringDto(o.Id, Animal.FormatAnimalNo(o.AnimalNo), o.TagNumber, o.Sex, o.BirthDate, o.Status)).ToList(),
            reasons));
    }

    private static async Task<IResult> AddEvent(Guid animalId, BreedingEventRequest request, AppDbContext db, CancellationToken ct)
    {
        var (animal, problem) = await RecordEndpoints.LoadActiveAnimalAsync(db, animalId, ct);
        if (problem is not null) return problem;

        // A retry of an event that was already stored.
        if (await db.BreedingEvents.AsNoTracking().IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleOrDefaultAsync(e => e.Id == request.Id, ct) is { } existing)
        {
            if (existing.AnimalId != animalId)
                return TypedResults.Problem("A record with this ID already exists.", statusCode: StatusCodes.Status409Conflict);
            var calves = await db.Animals.AsNoTracking().Where(a => a.BirthEventId == existing.Id).Select(a => a.Id).ToListAsync(ct);
            return TypedResults.Ok(ToDto(existing, await SireLabelsAsync(db, [existing.SireId], ct),
                await RecordEndpoints.RecorderNamesAsync(db, [existing.CreatedBy], ct), calves));
        }

        if (BreedingEvent.IsFemaleEvent(request.Type) != (animal!.Sex == Sex.Female))
            return Invalid("Type", request.Type == BreedingEventType.BullSoundnessExam
                ? "Soundness exams are for bulls."
                : "Breeding events other than soundness exams are recorded on the female.");
        if (request.SireId is { } sireId && !await db.Animals.AnyAsync(a => a.Id == sireId && a.Sex == Sex.Male, ct))
            return Invalid("SireId", "The sire must be a male animal.");

        var breedingEvent = new BreedingEvent
        {
            Id = request.Id,
            AnimalId = animalId,
            FarmId = animal.FarmId,
            Type = request.Type,
            EventDate = request.EventDate,
            SireId = request.SireId,
            SemenCode = request.Type == BreedingEventType.Insemination ? request.SemenCode : null,
            PregnancyResult = request.Type == BreedingEventType.PregnancyCheck ? request.PregnancyResult : null,
            GestationDays = request.Type == BreedingEventType.PregnancyCheck && request.PregnancyResult == PregnancyResult.Pregnant ? request.GestationDays : null,
            Assisted = request.Type == BreedingEventType.Calving ? request.Assisted : null,
            StillbornCount = request.Type == BreedingEventType.Calving ? request.StillbornCount : null,
            SoundnessResult = request.Type == BreedingEventType.BullSoundnessExam ? request.SoundnessResult : null,
            Notes = request.Notes,
        };
        db.BreedingEvents.Add(breedingEvent);

        var calfIds = new List<Guid>();
        if (request.Type == BreedingEventType.Calving)
        {
            var tracked = await db.Animals.SingleAsync(a => a.Id == animalId, ct);
            // A heifer becomes a dam at her first calving.
            if (tracked.Category == AnimalCategory.FemaleCalf) tracked.Category = AnimalCategory.Dam;

            // Sire: as recorded on the calving, otherwise the bull from the last natural service.
            var sire = request.SireId ?? await db.BreedingEvents.AsNoTracking()
                .Where(e => e.AnimalId == animalId && e.SireId != null && e.EventDate <= request.EventDate
                            && (e.Type == BreedingEventType.NaturalService || e.Type == BreedingEventType.Insemination))
                .OrderByDescending(e => e.EventDate).Select(e => e.SireId).FirstOrDefaultAsync(ct);
            breedingEvent.SireId = sire;

            foreach (var calfRequest in request.Calves)
            {
                if (await db.Animals.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(a => a.Id == calfRequest.Id, ct))
                    return TypedResults.Problem("A calf with this ID already exists.", statusCode: StatusCodes.Status409Conflict);

                var calf = new Animal
                {
                    Id = calfRequest.Id,
                    FarmId = animal.FarmId,
                    ProjectId = animal.ProjectId,
                    BreedId = animal.BreedId,
                    HerdGroupId = animal.HerdGroupId,
                    Sex = calfRequest.Sex,
                    Category = calfRequest.Sex == Sex.Male ? AnimalCategory.MaleCalf : AnimalCategory.FemaleCalf,
                    BirthDate = request.EventDate,
                    BirthDateMethod = AgeEstimationMethod.Recorded,
                    DamId = animalId,
                    SireId = sire,
                    BirthEventId = breedingEvent.Id,
                };
                calf.Retag(calfRequest.TagNumber, request.EventDate, "Tagged at birth");
                db.Animals.Add(calf);
                calfIds.Add(calf.Id);

                if (calfRequest.BirthWeightKg is { } weight)
                {
                    db.Measurements.Add(new Measurement
                    {
                        AnimalId = calf.Id, FarmId = animal.FarmId, Type = MeasurementType.Weight, Value = weight,
                        MeasuredOn = request.EventDate, Notes = "Birth weight",
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/animals/{animalId}/breeding", ToDto(breedingEvent,
            await SireLabelsAsync(db, [breedingEvent.SireId], ct), await RecordEndpoints.RecorderNamesAsync(db, [breedingEvent.CreatedBy], ct), calfIds));
    }

    private static async Task<IResult> DeleteEvent(Guid id, AppDbContext db, CancellationToken ct)
    {
        var breedingEvent = await db.BreedingEvents.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (breedingEvent is null) return TypedResults.NotFound();
        if (await db.Animals.AnyAsync(a => a.BirthEventId == id, ct))
            return TypedResults.Problem("This calving registered calves. Delete the calves first if the calving was recorded by mistake.",
                statusCode: StatusCodes.Status409Conflict);

        db.BreedingEvents.Remove(breedingEvent);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Dictionary<Guid, string>> SireLabelsAsync(AppDbContext db, IEnumerable<Guid?> sireIds, CancellationToken ct)
    {
        var ids = sireIds.OfType<Guid>().Distinct().ToList();
        return (await db.Animals.AsNoTracking().IgnoreQueryFilters([QueryFilters.FarmScope])
                .Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.AnimalNo, a.TagNumber }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => $"{Animal.FormatAnimalNo(a.AnimalNo)}{(a.TagNumber is null ? "" : $" (tag {a.TagNumber})")}");
    }

    private static BreedingEventDto ToDto(BreedingEvent e, Dictionary<Guid, string> sires, Dictionary<Guid, string> names, IReadOnlyList<Guid> calfIds) =>
        new(e.Id, e.Type, e.EventDate, e.SireId, e.SireId is { } s ? sires.GetValueOrDefault(s) : null, e.SemenCode, e.PregnancyResult,
            e.GestationDays, e.Assisted, e.StillbornCount, e.SoundnessResult, e.Notes, calfIds,
            e.CreatedBy is { } u ? names.GetValueOrDefault(u) : null, e.CreatedAt);

    private static ValidationProblem Invalid(string key, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}
