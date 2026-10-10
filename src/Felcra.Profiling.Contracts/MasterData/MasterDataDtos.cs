using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Domain.MasterData;
using FluentValidation;

namespace Felcra.Profiling.Contracts.MasterData;

// ---- Projects / schemes ------------------------------------------------------------------------

public sealed record ProjectDto(Guid Id, string Code, string Name, string? PlanReference, string? Description, bool IsActive, uint Version) : IHasId;

public sealed class ProjectRequest : IVersionedRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PlanReference { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }

    public static ProjectRequest From(ProjectDto dto) => new()
    {
        Code = dto.Code, Name = dto.Name, PlanReference = dto.PlanReference,
        Description = dto.Description, IsActive = dto.IsActive, Version = dto.Version,
    };
}

public sealed class ProjectRequestValidator : AbstractValidator<ProjectRequest>
{
    public ProjectRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PlanReference).MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

// ---- Breeds ------------------------------------------------------------------------------------

public sealed record BreedDto(Guid Id, string Code, string Name, string? Description, bool IsActive, uint Version) : IHasId;

public sealed class BreedRequest : IVersionedRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }

    public static BreedRequest From(BreedDto dto) => new()
    {
        Code = dto.Code, Name = dto.Name, Description = dto.Description, IsActive = dto.IsActive, Version = dto.Version,
    };
}

public sealed class BreedRequestValidator : AbstractValidator<BreedRequest>
{
    public BreedRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

// ---- Treatment products ------------------------------------------------------------------------

public sealed record TreatmentProductDto(
    Guid Id, string Name, TreatmentCategory Category, string? Unit, decimal? DefaultDose,
    int? WithdrawalDays, string? Notes, bool IsActive, uint Version) : IHasId;

public sealed class TreatmentProductRequest : IVersionedRequest
{
    public string Name { get; set; } = "";
    public TreatmentCategory Category { get; set; } = TreatmentCategory.Vaccine;
    public string? Unit { get; set; }
    public decimal? DefaultDose { get; set; }
    public int? WithdrawalDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }

    public static TreatmentProductRequest From(TreatmentProductDto dto) => new()
    {
        Name = dto.Name, Category = dto.Category, Unit = dto.Unit, DefaultDose = dto.DefaultDose,
        WithdrawalDays = dto.WithdrawalDays, Notes = dto.Notes, IsActive = dto.IsActive, Version = dto.Version,
    };
}

public sealed class TreatmentProductRequestValidator : AbstractValidator<TreatmentProductRequest>
{
    public TreatmentProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Unit).MaximumLength(20);
        RuleFor(x => x.DefaultDose).GreaterThan(0).When(x => x.DefaultDose.HasValue);
        RuleFor(x => x.WithdrawalDays).InclusiveBetween(0, 365);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

// ---- Feed ingredients --------------------------------------------------------------------------

public sealed record FeedIngredientDto(
    Guid Id, string Name, FeedCategory Category,
    decimal? DryMatterPercent, decimal? CrudeProteinPercent, decimal? TdnPercent, decimal? MetabolisableEnergy,
    decimal? CalciumPercent, decimal? PhosphorusPercent, decimal? PricePerKg, bool IsActive, uint Version) : IHasId;

public sealed class FeedIngredientRequest : IVersionedRequest
{
    public string Name { get; set; } = "";
    public FeedCategory Category { get; set; } = FeedCategory.Forage;
    public decimal? DryMatterPercent { get; set; }
    public decimal? CrudeProteinPercent { get; set; }
    public decimal? TdnPercent { get; set; }
    public decimal? MetabolisableEnergy { get; set; }
    public decimal? CalciumPercent { get; set; }
    public decimal? PhosphorusPercent { get; set; }
    public decimal? PricePerKg { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }

    public static FeedIngredientRequest From(FeedIngredientDto dto) => new()
    {
        Name = dto.Name, Category = dto.Category, DryMatterPercent = dto.DryMatterPercent,
        CrudeProteinPercent = dto.CrudeProteinPercent, TdnPercent = dto.TdnPercent,
        MetabolisableEnergy = dto.MetabolisableEnergy, CalciumPercent = dto.CalciumPercent,
        PhosphorusPercent = dto.PhosphorusPercent, PricePerKg = dto.PricePerKg, IsActive = dto.IsActive,
        Version = dto.Version,
    };
}

public sealed class FeedIngredientRequestValidator : AbstractValidator<FeedIngredientRequest>
{
    public FeedIngredientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.DryMatterPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.CrudeProteinPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.TdnPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.CalciumPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.PhosphorusPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.MetabolisableEnergy).InclusiveBetween(0, 50);
        RuleFor(x => x.PricePerKg).GreaterThanOrEqualTo(0);
    }
}
