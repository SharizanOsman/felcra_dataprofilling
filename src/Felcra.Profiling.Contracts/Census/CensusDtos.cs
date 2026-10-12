using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Domain.Animals;
using FluentValidation;

namespace Felcra.Profiling.Contracts.Census;

/// <summary>Head count per census category.</summary>
public sealed record CategoryCounts(int Bulls, int Dams, int MaleCalves, int FemaleCalves)
{
    public int Total => Bulls + Dams + MaleCalves + FemaleCalves;
}

public sealed record CensusSessionDto(
    Guid Id,
    Guid FarmId,
    string FarmCode,
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    string? PicName,
    string? PicPosition,
    string? PicPhone,
    string? Notes,
    int? RecordedBulls,
    int? RecordedDams,
    int? RecordedMaleCalves,
    int? RecordedFemaleCalves,
    bool IsClosed,
    IReadOnlyList<Guid> TreatmentProductIds,
    CategoryCounts Counted,
    int SelectedCount,
    uint Version) : IHasId;

public sealed class CensusSessionRequest : IVersionedRequest
{
    public Guid FarmId { get; set; }
    public string Title { get; set; } = "";
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? PicName { get; set; }
    public string? PicPosition { get; set; }
    public string? PicPhone { get; set; }
    public string? Notes { get; set; }
    public int? RecordedBulls { get; set; }
    public int? RecordedDams { get; set; }
    public int? RecordedMaleCalves { get; set; }
    public int? RecordedFemaleCalves { get; set; }
    public bool IsClosed { get; set; }
    public List<Guid> TreatmentProductIds { get; set; } = [];
    public uint Version { get; set; }

    public static CensusSessionRequest From(CensusSessionDto dto) => new()
    {
        FarmId = dto.FarmId,
        Title = dto.Title,
        StartDate = dto.StartDate,
        EndDate = dto.EndDate,
        PicName = dto.PicName,
        PicPosition = dto.PicPosition,
        PicPhone = dto.PicPhone,
        Notes = dto.Notes,
        RecordedBulls = dto.RecordedBulls,
        RecordedDams = dto.RecordedDams,
        RecordedMaleCalves = dto.RecordedMaleCalves,
        RecordedFemaleCalves = dto.RecordedFemaleCalves,
        IsClosed = dto.IsClosed,
        TreatmentProductIds = [.. dto.TreatmentProductIds],
        Version = dto.Version,
    };
}

public sealed class CensusSessionRequestValidator : AbstractValidator<CensusSessionRequest>
{
    public CensusSessionRequestValidator()
    {
        RuleFor(x => x.FarmId).NotEmpty().WithMessage(_ => T("Choose a farm."));
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).WithMessage(_ => T("End date must be on or after the start date."));
        RuleFor(x => x.PicName).MaximumLength(200);
        RuleFor(x => x.PicPosition).MaximumLength(200);
        RuleFor(x => x.PicPhone).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.RecordedBulls).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RecordedDams).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RecordedMaleCalves).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RecordedFemaleCalves).GreaterThanOrEqualTo(0);
    }
}

public sealed record CensusEntryDto(
    Guid Id,
    Guid AnimalId,
    string AnimalNo,
    string? TagNumber,
    bool HasDuplicateTag,
    int? LineNo,
    AnimalCategory Category,
    int? EstimatedAgeMonths,
    decimal? WeightKg,
    decimal? BodyConditionScore,
    string? ProjectCode,
    string? Remarks,
    bool Selected,
    uint Version) : IHasId;

public sealed class CensusEntryRequest : IVersionedRequest
{
    public Guid AnimalId { get; set; }
    public AnimalCategory Category { get; set; } = AnimalCategory.Dam;
    public int? EstimatedAgeMonths { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyConditionScore { get; set; }
    public string? Remarks { get; set; }
    public bool Selected { get; set; }
    public uint Version { get; set; }

    public static CensusEntryRequest From(CensusEntryDto dto) => new()
    {
        AnimalId = dto.AnimalId,
        Category = dto.Category,
        EstimatedAgeMonths = dto.EstimatedAgeMonths,
        WeightKg = dto.WeightKg,
        BodyConditionScore = dto.BodyConditionScore,
        Remarks = dto.Remarks,
        Selected = dto.Selected,
        Version = dto.Version,
    };
}

public sealed class CensusEntryRequestValidator : AbstractValidator<CensusEntryRequest>
{
    public CensusEntryRequestValidator()
    {
        RuleFor(x => x.AnimalId).NotEmpty();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.EstimatedAgeMonths).InclusiveBetween(0, 360);
        RuleFor(x => x.WeightKg).InclusiveBetween(1, 1500).WithMessage(_ => T("Weight must be between 1 and 1500 kg."));
        // Scale not yet confirmed by FELCRA (URS open question 1); accept 1–9.
        RuleFor(x => x.BodyConditionScore).InclusiveBetween(1, 9);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

// ---- Import ------------------------------------------------------------------------------------

public enum ImportRowAction
{
    /// <summary>A new animal will be registered.</summary>
    CreateAnimal = 1,

    /// <summary>The row matches one existing animal by tag number.</summary>
    MatchExisting = 2,

    /// <summary>The row cannot be imported; see messages.</summary>
    Error = 3,
}

public sealed record ImportRowDto(
    int RowNumber,
    int? LineNo,
    string? TagNumber,
    AnimalCategory? Category,
    string? AgeText,
    int? AgeMonths,
    decimal? WeightKg,
    decimal? BodyConditionScore,
    string? ProjectCode,
    string? Remarks,
    bool Selected,
    ImportRowAction Action,
    string? MatchedAnimalNo,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ImportPreviewDto(
    IReadOnlyList<ImportRowDto> Rows,
    int AnimalsToCreate,
    int AnimalsToMatch,
    int RowsWithErrors,
    int RowsWithWarnings,
    CategoryCounts Counts)
{
    public bool CanImport => Rows.Count > 0 && RowsWithErrors == 0;
}

public sealed record ImportResultDto(int AnimalsCreated, int AnimalsMatched, int EntriesCreated, int EntriesUpdated);
