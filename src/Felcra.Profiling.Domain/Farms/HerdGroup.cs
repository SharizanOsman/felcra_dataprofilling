using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Farms;

/// <summary>A group or pen of animals within a farm, used for management and ration planning.</summary>
public class HerdGroup : AuditableEntity, IFarmScoped
{
    public Guid FarmId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
