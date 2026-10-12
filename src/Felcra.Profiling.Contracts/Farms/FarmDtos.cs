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
    uint Version,
    decimal? Latitude = null,
    decimal? Longitude = null) : IHasId;

public sealed class FarmRequest : IVersionedRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? State { get; set; }
    public decimal? AreaHectares { get; set; }
    public int? Capacity { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
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
        Latitude = dto.Latitude,
        Longitude = dto.Longitude,
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
            .WithMessage(_ => T("Code may only contain letters, digits, '-' and '_'."));
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.State).Must(s => string.IsNullOrEmpty(s) || MalaysianStates.All.Contains(s))
            .WithMessage(_ => T("Choose one of the Malaysian states or federal territories."));
        RuleFor(x => x.AreaHectares).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.Capacity).InclusiveBetween(0, 1_000_000);
        // Latitude and longitude come together, inside Malaysia.
        RuleFor(x => x.Latitude).NotNull().When(x => x.Longitude is not null).WithMessage(_ => T("Enter the latitude too."))
            .InclusiveBetween(0.5m, 7.5m).WithMessage(_ => T("Latitude must be in Malaysia (0.5 to 7.5)."));
        RuleFor(x => x.Longitude).NotNull().When(x => x.Latitude is not null).WithMessage(_ => T("Enter the longitude too."))
            .InclusiveBetween(99.5m, 119.5m).WithMessage(_ => T("Longitude must be in Malaysia (99.5 to 119.5)."));
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

/// <summary>Malaysia's states and federal territories, spelled as on the farm map.</summary>
public static class MalaysianStates
{
    public static readonly IReadOnlyList<string> All =
    [
        "Johor", "Kedah", "Kelantan", "Melaka", "Negeri Sembilan", "Pahang", "Perak", "Perlis", "Pulau Pinang",
        "Sabah", "Sarawak", "Selangor", "Terengganu", "Kuala Lumpur", "Labuan", "Putrajaya",
    ];

    // Other spellings (English, "WP …", OpenStreetMap's), mapped to the names above.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Penang"] = "Pulau Pinang", ["Malacca"] = "Melaka", ["Negri Sembilan"] = "Negeri Sembilan",
        ["WP Kuala Lumpur"] = "Kuala Lumpur", ["Wilayah Persekutuan Kuala Lumpur"] = "Kuala Lumpur", ["Federal Territory of Kuala Lumpur"] = "Kuala Lumpur",
        ["WP Labuan"] = "Labuan", ["Wilayah Persekutuan Labuan"] = "Labuan", ["Federal Territory of Labuan"] = "Labuan",
        ["WP Putrajaya"] = "Putrajaya", ["Wilayah Persekutuan Putrajaya"] = "Putrajaya", ["Federal Territory of Putrajaya"] = "Putrajaya",
    };

    /// <summary>The state as spelled in <see cref="All"/>, or null when the text is not a Malaysian state.</summary>
    public static string? Normalize(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var text = state.Trim();
        if (Aliases.TryGetValue(text, out var alias)) return alias;
        return All.FirstOrDefault(s => s.Equals(text, StringComparison.OrdinalIgnoreCase));
    }
}


/// <summary>A farm on the map: its place and herd size.</summary>
public sealed record FarmMapPointDto(
    Guid FarmId, string Code, string Name, string? State, decimal? Latitude, decimal? Longitude, int ActiveAnimals, int? Capacity, bool IsActive);
