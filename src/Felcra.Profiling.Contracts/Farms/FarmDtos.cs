using Felcra.Profiling.Contracts.Common;
using FluentValidation;

namespace Felcra.Profiling.Contracts.Farms;

public sealed record FarmDto(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string? State,
    decimal? AreaHectares,
    int? Capacity,
    bool IsActive,
    IReadOnlyList<Guid> ProjectIds,
    uint Version) : IHasId;

public sealed class FarmRequest : IVersionedRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? State { get; set; }
    public decimal? AreaHectares { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Guid> ProjectIds { get; set; } = [];
    public uint Version { get; set; }

    public static FarmRequest From(FarmDto dto) => new()
    {
        Code = dto.Code,
        Name = dto.Name,
        Address = dto.Address,
        State = dto.State,
        AreaHectares = dto.AreaHectares,
        Capacity = dto.Capacity,
        IsActive = dto.IsActive,
        ProjectIds = [.. dto.ProjectIds],
        Version = dto.Version,
    };
}

public sealed class FarmRequestValidator : AbstractValidator<FarmRequest>
{
    public FarmRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Code may only contain letters, digits, '-' and '_'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.AreaHectares).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.Capacity).InclusiveBetween(0, 1_000_000);
    }
}

public sealed record HerdGroupDto(Guid Id, Guid FarmId, string Name, string? Description, bool IsActive, uint Version) : IHasId;

public sealed class HerdGroupRequest : IVersionedRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }

    public static HerdGroupRequest From(HerdGroupDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        IsActive = dto.IsActive,
        Version = dto.Version,
    };
}

public sealed class HerdGroupRequestValidator : AbstractValidator<HerdGroupRequest>
{
    public HerdGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
