using Felcra.Profiling.Domain.Common;
using Felcra.Profiling.Domain.MasterData;

namespace Felcra.Profiling.Domain.Farms;

/// <summary>A farm / premises, e.g. "FELCRA Berhad Kawasan Pagoh".</summary>
public class Farm : AuditableEntity
{
    /// <summary>Short unique code, e.g. "PAGOH".</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? State { get; set; }
    public decimal? AreaHectares { get; set; }

    /// <summary>Location of the farm (WGS84 decimal degrees), for the map.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>Planned grazing capacity in head of cattle.</summary>
    public int? Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Projects / schemes run on this farm (e.g. EPP5 and RP4 at Pagoh).</summary>
    public List<FarmProject> Projects { get; set; } = [];
}

public class FarmProject
{
    public Guid FarmId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
}
