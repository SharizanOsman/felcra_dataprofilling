using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Breeding;

public enum BreedingEventType
{
    /// <summary>Signs of oestrus observed.</summary>
    Heat = 1,

    /// <summary>Artificial insemination (permanian beradas).</summary>
    Insemination = 2,

    /// <summary>Mated by a bull.</summary>
    NaturalService = 3,

    PregnancyCheck = 4,
    Calving = 5,
    Abortion = 6,

    /// <summary>Breeding soundness examination of a bull.</summary>
    BullSoundnessExam = 7,
}

public enum PregnancyResult
{
    Pregnant = 1,
    Open = 2,
    Uncertain = 3,
}

public enum ReproductiveState
{
    /// <summary>No breeding records yet.</summary>
    Unknown = 0,

    /// <summary>Served (AI or natural) and waiting for a pregnancy check.</summary>
    Served = 1,

    Pregnant = 2,

    /// <summary>Calved, aborted or checked empty and not yet pregnant again.</summary>
    Open = 3,
}

public enum SoundnessResult
{
    Satisfactory = 1,
    Questionable = 2,
    Unsatisfactory = 3,
}

/// <summary>
/// A reproductive event for one animal. Append-only with a device-generated ID, like the other field records.
/// </summary>
public class BreedingEvent : AuditableEntity, IFarmScoped
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public BreedingEventType Type { get; set; }
    public DateOnly EventDate { get; set; }

    /// <summary>Bull used for natural service (or recorded as sire for AI when known).</summary>
    public Guid? SireId { get; set; }

    /// <summary>AI straw / semen code.</summary>
    public string? SemenCode { get; set; }

    public PregnancyResult? PregnancyResult { get; set; }

    /// <summary>Gestation age estimated at a positive pregnancy check, in days.</summary>
    public int? GestationDays { get; set; }

    /// <summary>Calving needed assistance (dystocia).</summary>
    public bool? Assisted { get; set; }

    /// <summary>Calves born dead at this calving.</summary>
    public int? StillbornCount { get; set; }

    public SoundnessResult? SoundnessResult { get; set; }
    public string? Notes { get; set; }

    public bool IsService => Type is BreedingEventType.Insemination or BreedingEventType.NaturalService;

    /// <summary>Events that only make sense for a female.</summary>
    public static bool IsFemaleEvent(BreedingEventType type) => type != BreedingEventType.BullSoundnessExam;
}
