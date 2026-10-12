using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using FluentValidation;

namespace Felcra.Profiling.Contracts.Breeding;

public sealed class CalfRequest
{
    /// <summary>Device-generated ID of the new calf, so a resent calving does not register it twice.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Sex Sex { get; set; } = Sex.Female;
    public string? TagNumber { get; set; }
    public decimal? BirthWeightKg { get; set; }
}

public sealed class BreedingEventRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public BreedingEventType Type { get; set; } = BreedingEventType.Heat;
    public DateOnly EventDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public Guid? SireId { get; set; }
    public string? SemenCode { get; set; }
    public PregnancyResult? PregnancyResult { get; set; }
    public int? GestationDays { get; set; }
    public bool? Assisted { get; set; }
    public int? StillbornCount { get; set; }
    public SoundnessResult? SoundnessResult { get; set; }
    public string? Notes { get; set; }

    /// <summary>Live calves born at a calving; each is registered as a new animal.</summary>
    public List<CalfRequest> Calves { get; set; } = [];
}

public sealed class BreedingEventRequestValidator : AbstractValidator<BreedingEventRequest>
{
    public BreedingEventRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.EventDate).LessThanOrEqualTo(_ => ValidationDates.LatestToday).WithMessage(_ => T("Date cannot be in the future."));
        RuleFor(x => x.SemenCode).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);

        RuleFor(x => x.PregnancyResult).NotNull().When(x => x.Type == BreedingEventType.PregnancyCheck)
            .WithMessage(_ => T("Choose the result of the pregnancy check."));
        RuleFor(x => x.GestationDays).InclusiveBetween(20, 300)
            .When(x => x.Type == BreedingEventType.PregnancyCheck && x.PregnancyResult == PregnancyResult.Pregnant && x.GestationDays.HasValue)
            .WithMessage(_ => T("Gestation age must be between 20 and 300 days."));
        RuleFor(x => x.SoundnessResult).NotNull().When(x => x.Type == BreedingEventType.BullSoundnessExam)
            .WithMessage(_ => T("Choose the outcome of the soundness exam."));

        When(x => x.Type == BreedingEventType.Calving, () =>
        {
            RuleFor(x => x.Calves.Count).LessThanOrEqualTo(3).OverridePropertyName("Calves").WithMessage(_ => T("At most 3 calves per calving."));
            RuleFor(x => x.StillbornCount).InclusiveBetween(0, 3);
            RuleFor(x => x).Must(x => x.Calves.Count + (x.StillbornCount ?? 0) > 0)
                .OverridePropertyName("Calves").WithMessage(_ => T("Add the calf (or record it as stillborn)."));
            RuleForEach(x => x.Calves).ChildRules(calf =>
            {
                calf.RuleFor(c => c.Id).NotEmpty();
                calf.RuleFor(c => c.Sex).IsInEnum();
                calf.RuleFor(c => c.TagNumber).MaximumLength(32);
                calf.RuleFor(c => c.BirthWeightKg).InclusiveBetween(5, 80).WithMessage(_ => T("Birth weight must be between 5 and 80 kg."));
            });
        });
        RuleFor(x => x.Calves).Empty().When(x => x.Type != BreedingEventType.Calving).WithMessage(_ => T("Calves can only be added to a calving."));
    }
}

public sealed record BreedingEventDto(
    Guid Id,
    BreedingEventType Type,
    DateOnly EventDate,
    Guid? SireId,
    string? SireLabel,
    string? SemenCode,
    PregnancyResult? PregnancyResult,
    int? GestationDays,
    bool? Assisted,
    int? StillbornCount,
    SoundnessResult? SoundnessResult,
    string? Notes,
    IReadOnlyList<Guid> CalfIds,
    string? RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record ReproductionDto(
    ReproductiveState State,
    DateOnly? LastCalving,
    int? CalvingIntervalDays,
    DateOnly? LastService,
    DateOnly? ExpectedCalving,
    DateOnly? PregnancyCheckDue,
    int? DaysOpen,
    int ConsecutiveOpenChecks,
    int CalvingCount);

public sealed record OffspringDto(Guid Id, string AnimalNo, string? TagNumber, Sex Sex, DateOnly? BirthDate, AnimalStatus Status);

public sealed record AnimalBreedingDto(
    ReproductionDto? Reproduction,
    IReadOnlyList<BreedingEventDto> Events,
    IReadOnlyList<OffspringDto> Offspring,
    IReadOnlyList<string> CullingReasons);

public sealed record CullingCandidateDto(
    Guid AnimalId,
    string AnimalNo,
    string? TagNumber,
    string FarmCode,
    int? AgeMonths,
    ReproductiveState State,
    DateOnly? LastCalving,
    decimal? LatestBcs,
    IReadOnlyList<string> Reasons);

public sealed record HerdBreedingSummaryDto(
    Guid FarmId,
    string FarmCode,
    int Dams,
    int Pregnant,
    int Served,
    int Open,
    int NoRecords,
    decimal? PregnancyRatePercent,
    int? AverageCalvingIntervalDays,
    int CalvesLast12Months,
    decimal? CalvingRatePercent,
    int AbortionsLast12Months);

public enum ReminderType
{
    PregnancyCheckDue = 1,
    CalvingDue = 2,
    TreatmentDue = 3,
    LowBodyCondition = 4,
}

public sealed record ReminderDto(
    ReminderType Type,
    Guid AnimalId,
    string AnimalNo,
    string? TagNumber,
    Guid FarmId,
    string FarmCode,
    DateOnly DueDate,
    string Title,
    string? Detail)
{
    public bool IsOverdueOn(DateOnly today) => DueDate < today;
}
