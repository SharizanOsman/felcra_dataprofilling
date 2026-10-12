using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Domain.Records;
using FluentValidation;

namespace Felcra.Profiling.Contracts.Records;

// Requests carry an Id generated on the device: re-sending the same record is harmless.

public sealed class MeasurementRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public MeasurementType Type { get; set; } = MeasurementType.Weight;
    public decimal? Value { get; set; }
    public DateOnly MeasuredOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public WeighingMethod? Method { get; set; }
    public string? Notes { get; set; }
}

public sealed class MeasurementRequestValidator : AbstractValidator<MeasurementRequest>
{
    public MeasurementRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Value).NotNull().WithMessage(_ => T("Enter a value."))
            .Must((r, v) => v is null || (v >= Measurement.Range(r.Type).Min && v <= Measurement.Range(r.Type).Max))
            .WithMessage(r => T("{0} must be between {1} and {2}{3}.", Label(r.Type), Measurement.Range(r.Type).Min, Measurement.Range(r.Type).Max, Unit(r.Type)));
        RuleFor(x => x.MeasuredOn).LessThanOrEqualTo(_ => ValidationDates.LatestToday).WithMessage(_ => T("Date cannot be in the future."));
        RuleFor(x => x.Method).Null().When(x => x.Type != MeasurementType.Weight).WithMessage(_ => T("Method only applies to weights."));
        RuleFor(x => x.Notes).MaximumLength(500);
    }

    private static string Label(MeasurementType type) => type switch
    {
        MeasurementType.BodyCondition => "BCS",
        MeasurementType.MilkYield => T("Milk yield"),
        _ => T("Weight"),

    };

    private static string Unit(MeasurementType type) => Measurement.Unit(type) is { Length: > 0 } u ? $" {u}" : "";
}

public sealed record MeasurementDto(
    Guid Id, MeasurementType Type, decimal Value, DateOnly MeasuredOn, WeighingMethod? Method, string? Notes,
    string? RecordedBy, DateTimeOffset RecordedAt);

public sealed class TreatmentRecordRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TreatmentProductId { get; set; }
    public DateOnly GivenOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal? Dose { get; set; }
    public string? DoseUnit { get; set; }
    public AdministrationRoute? Route { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? NextDueOn { get; set; }
    public Guid? CensusSessionId { get; set; }
    public string? Notes { get; set; }
}

public sealed class TreatmentRecordRequestValidator : AbstractValidator<TreatmentRecordRequest>
{
    public TreatmentRecordRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.TreatmentProductId).NotEmpty().WithMessage(_ => T("Choose the treatment."));
        RuleFor(x => x.GivenOn).LessThanOrEqualTo(_ => ValidationDates.LatestToday).WithMessage(_ => T("Date cannot be in the future."));
        RuleFor(x => x.Dose).GreaterThan(0).When(x => x.Dose.HasValue);
        RuleFor(x => x.DoseUnit).MaximumLength(20);
        RuleFor(x => x.Route).IsInEnum().When(x => x.Route.HasValue);
        RuleFor(x => x.BatchNumber).MaximumLength(50);
        RuleFor(x => x.NextDueOn).GreaterThan(x => x.GivenOn).When(x => x.NextDueOn.HasValue)
            .WithMessage(_ => T("Next due date must be after the date given."));
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed record TreatmentRecordDto(
    Guid Id, Guid TreatmentProductId, string ProductName, DateOnly GivenOn, decimal? Dose, string? DoseUnit,
    AdministrationRoute? Route, string? BatchNumber, DateOnly? NextDueOn, DateOnly? WithdrawalUntil,
    Guid? CensusSessionId, string? Notes, string? RecordedBy, DateTimeOffset RecordedAt);

public sealed class ObservationRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateOnly ObservedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public ObservationCategory Category { get; set; } = ObservationCategory.General;
    public string Notes { get; set; } = "";

    /// <summary>Photos uploaded beforehand for this animal.</summary>
    public List<Guid> PhotoIds { get; set; } = [];
}

public sealed class ObservationRequestValidator : AbstractValidator<ObservationRequest>
{
    public ObservationRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ObservedOn).LessThanOrEqualTo(_ => ValidationDates.LatestToday).WithMessage(_ => T("Date cannot be in the future."));
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Notes).NotEmpty().WithMessage(_ => T("Describe what you observed.")).MaximumLength(2000);
        RuleFor(x => x.PhotoIds).Must(p => p.Count <= 5).WithMessage(_ => T("At most 5 photos per observation."));
    }
}

public sealed record ObservationDto(
    Guid Id, DateOnly ObservedOn, ObservationCategory Category, string Notes, IReadOnlyList<Guid> PhotoIds,
    string? RecordedBy, DateTimeOffset RecordedAt);

public sealed record PhotoDto(Guid Id, string ContentType, long SizeBytes);

/// <summary>Everything recorded for an animal at the pen, newest first.</summary>
public sealed record AnimalRecordsDto(
    IReadOnlyList<MeasurementDto> Measurements,
    IReadOnlyList<TreatmentRecordDto> Treatments,
    IReadOnlyList<ObservationDto> Observations);
