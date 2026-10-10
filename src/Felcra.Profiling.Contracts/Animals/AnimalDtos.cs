using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Domain.Animals;
using FluentValidation;

namespace Felcra.Profiling.Contracts.Animals;

public sealed record AnimalListItemDto(
    Guid Id,
    string AnimalNo,
    string? TagNumber,
    bool HasDuplicateTag,
    Sex Sex,
    AnimalCategory Category,
    string? BreedName,
    DateOnly? BirthDate,
    AgeEstimationMethod? BirthDateMethod,
    Guid FarmId,
    string FarmCode,
    string? ProjectCode,
    string? HerdGroupName,
    AnimalStatus Status,
    decimal? LatestWeightKg,
    DateOnly? LatestWeighedOn) : IHasId;

public sealed record AnimalTagDto(string TagNumber, DateOnly AssignedOn, DateOnly? RemovedOn, string? Reason);

public sealed record AnimalCensusHistoryDto(
    Guid CensusSessionId,
    string SessionTitle,
    DateOnly SessionStartDate,
    AnimalCategory Category,
    int? EstimatedAgeMonths,
    decimal? WeightKg,
    decimal? BodyConditionScore,
    bool Selected,
    string? Remarks);

public sealed record AnimalDto(
    Guid Id,
    string AnimalNo,
    string? TagNumber,
    Sex Sex,
    AnimalCategory Category,
    Guid FarmId,
    string FarmCode,
    Guid? ProjectId,
    Guid? HerdGroupId,
    Guid? BreedId,
    DateOnly? BirthDate,
    AgeEstimationMethod? BirthDateMethod,
    Guid? DamId,
    string? DamLabel,
    Guid? SireId,
    string? SireLabel,
    AnimalStatus Status,
    DateOnly? StatusDate,
    string? StatusReason,
    string? Remarks,
    IReadOnlyList<Guid> DuplicateTagAnimalIds,
    IReadOnlyList<AnimalTagDto> TagHistory,
    IReadOnlyList<AnimalCensusHistoryDto> CensusHistory,
    uint Version) : IHasId;

/// <summary>Create or update an animal. <see cref="FarmId"/> is only used when creating.</summary>
public sealed class AnimalRequest : IVersionedRequest
{
    public Guid FarmId { get; set; }
    public string? TagNumber { get; set; }
    public AnimalCategory Category { get; set; } = AnimalCategory.Dam;
    public Sex Sex { get; set; } = Sex.Female;
    public Guid? BreedId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? HerdGroupId { get; set; }
    public DateOnly? BirthDate { get; set; }
    public AgeEstimationMethod? BirthDateMethod { get; set; }
    public Guid? DamId { get; set; }
    public Guid? SireId { get; set; }
    public string? Remarks { get; set; }
    public uint Version { get; set; }

    public static AnimalRequest From(AnimalDto dto) => new()
    {
        FarmId = dto.FarmId,
        TagNumber = dto.TagNumber,
        Category = dto.Category,
        Sex = dto.Sex,
        BreedId = dto.BreedId,
        ProjectId = dto.ProjectId,
        HerdGroupId = dto.HerdGroupId,
        BirthDate = dto.BirthDate,
        BirthDateMethod = dto.BirthDateMethod,
        DamId = dto.DamId,
        SireId = dto.SireId,
        Remarks = dto.Remarks,
        Version = dto.Version,
    };
}

public sealed class AnimalRequestValidator : AbstractValidator<AnimalRequest>
{
    public AnimalRequestValidator()
    {
        RuleFor(x => x.FarmId).NotEmpty().WithMessage("Choose a farm.");
        RuleFor(x => x.TagNumber).MaximumLength(32);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Sex).IsInEnum()
            .Must((request, sex) => sex == Animal.SexOf(request.Category))
            .WithMessage("Sex does not match the category (bulls and male calves are male; dams and female calves are female).");
        RuleFor(x => x.BirthDate)
            .LessThanOrEqualTo(_ => ValidationDates.LatestToday)
            .WithMessage("Birth date cannot be in the future.");
        RuleFor(x => x.BirthDateMethod).NotNull().When(x => x.BirthDate.HasValue)
            .WithMessage("Say whether the birth date is recorded or estimated.");
        RuleFor(x => x.SireId).NotEqual(x => x.DamId).When(x => x.SireId.HasValue);
        RuleFor(x => x.Remarks).MaximumLength(1000);
    }
}

public sealed class ChangeStatusRequest : IVersionedRequest
{
    public AnimalStatus Status { get; set; } = AnimalStatus.Sold;
    public DateOnly? Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Reason { get; set; }
    public uint Version { get; set; }
}

public sealed class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    public ChangeStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Date).NotNull().When(x => x.Status != AnimalStatus.Active)
            .LessThanOrEqualTo(_ => ValidationDates.LatestToday).WithMessage("Date cannot be in the future.");
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Status is AnimalStatus.Dead or AnimalStatus.Culled)
            .WithMessage("Give a reason for deaths and culling.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class RetagRequest : IVersionedRequest
{
    /// <summary>New tag number; empty means the tag was lost and the animal is now untagged.</summary>
    public string? TagNumber { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Reason { get; set; }
    public uint Version { get; set; }
}

public sealed class RetagRequestValidator : AbstractValidator<RetagRequest>
{
    public RetagRequestValidator()
    {
        RuleFor(x => x.TagNumber).MaximumLength(32);
        RuleFor(x => x.Date).LessThanOrEqualTo(_ => ValidationDates.LatestToday).WithMessage("Date cannot be in the future.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed record DuplicateTagDto(string TagNumber, Guid FarmId, string FarmCode, IReadOnlyList<Guid> AnimalIds, IReadOnlyList<string> AnimalNos);
