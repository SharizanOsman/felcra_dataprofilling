using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Census;

/// <summary>
/// A census / headcount and profiling programme at one farm, e.g. Pagoh, 2–7 July 2026.
/// </summary>
public class CensusSession : AuditableEntity, IFarmScoped
{
    public Guid FarmId { get; set; }
    public required string Title { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Person in charge.</summary>
    public string? PicName { get; set; }
    public string? PicPosition { get; set; }
    public string? PicPhone { get; set; }
    public string? Notes { get; set; }

    /// <summary>"Population on record" before the count, by category (as written on the form).</summary>
    public int? RecordedBulls { get; set; }
    public int? RecordedDams { get; set; }
    public int? RecordedMaleCalves { get; set; }
    public int? RecordedFemaleCalves { get; set; }

    public bool IsClosed { get; set; }

    /// <summary>Treatments given to the whole herd during the programme (e.g. FMD booster).</summary>
    public List<CensusSessionTreatment> Treatments { get; set; } = [];
}

public class CensusSessionTreatment
{
    public Guid CensusSessionId { get; set; }
    public Guid TreatmentProductId { get; set; }
}

/// <summary>One animal as counted and assessed during a census session.</summary>
public class CensusEntry : AuditableEntity, IFarmScoped
{
    public Guid CensusSessionId { get; set; }
    public Guid AnimalId { get; set; }

    /// <summary>Denormalised from the session for farm-scoped filtering.</summary>
    public Guid FarmId { get; set; }

    /// <summary>Line number on the paper form ("Bil."), if imported.</summary>
    public int? LineNo { get; set; }

    /// <summary>Category at the time of the census.</summary>
    public AnimalCategory Category { get; set; }

    /// <summary>Age as estimated during the census (usually by dentition).</summary>
    public int? EstimatedAgeMonths { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyConditionScore { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Selected as meeting the programme specification.</summary>
    public bool Selected { get; set; }
}
