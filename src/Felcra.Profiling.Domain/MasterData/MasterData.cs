using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.MasterData;

/// <summary>A funding project / scheme, e.g. EPP5 (Entry Point Project 5) or RP4 (Rolling Plan 4).</summary>
public class Project : AuditableEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }

    /// <summary>National plan reference, e.g. "RMK-11".</summary>
    public string? PlanReference { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Breed : AuditableEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum TreatmentCategory
{
    Vaccine = 1,
    Antiparasitic = 2,
    Antibiotic = 3,
    Vitamin = 4,
    DiagnosticTest = 5,
    Procedure = 6,
    Other = 99,
}

/// <summary>A medicine, vaccine, test or procedure that can be given to animals (e.g. FMD booster, Doramectin).</summary>
public class TreatmentProduct : AuditableEntity
{
    public required string Name { get; set; }
    public TreatmentCategory Category { get; set; }

    /// <summary>Dose unit, e.g. "ml".</summary>
    public string? Unit { get; set; }
    public decimal? DefaultDose { get; set; }

    /// <summary>Days before meat from a treated animal may be sold.</summary>
    public int? WithdrawalDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum FeedCategory
{
    Forage = 1,
    Concentrate = 2,
    ByProduct = 3,
    Mineral = 4,
    Supplement = 5,
}

/// <summary>
/// A feed ingredient with its nutrient composition (dry-matter basis) and price, used by the
/// least-cost ration model.
/// </summary>
public class FeedIngredient : AuditableEntity
{
    public required string Name { get; set; }
    public FeedCategory Category { get; set; }

    public decimal? DryMatterPercent { get; set; }
    public decimal? CrudeProteinPercent { get; set; }
    public decimal? TdnPercent { get; set; }

    /// <summary>Metabolisable energy, MJ per kg dry matter.</summary>
    public decimal? MetabolisableEnergy { get; set; }
    public decimal? CalciumPercent { get; set; }
    public decimal? PhosphorusPercent { get; set; }

    /// <summary>Price in RM per kg as fed.</summary>
    public decimal? PricePerKg { get; set; }
    public bool IsActive { get; set; } = true;
}
