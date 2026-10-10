using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Records;

// Records made at the pen are append-only events with IDs generated on the device, so the same
// record sent twice (e.g. after a dropped connection, or by offline sync in Step 5) is stored once.

public enum MeasurementType
{
    /// <summary>Body weight in kg.</summary>
    Weight = 1,

    /// <summary>Body condition score (scale to be confirmed by FELCRA; 1–9 accepted).</summary>
    BodyCondition = 2,

    /// <summary>Milk yield in litres for the day.</summary>
    MilkYield = 3,
}

public enum WeighingMethod
{
    Scale = 1,
    HeartGirthTape = 2,
    VisualEstimate = 3,
}

public class Measurement : AuditableEntity, IFarmScoped
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public MeasurementType Type { get; set; }
    public decimal Value { get; set; }
    public DateOnly MeasuredOn { get; set; }
    public WeighingMethod? Method { get; set; }
    public string? Notes { get; set; }

    public static string Unit(MeasurementType type) => type switch
    {
        MeasurementType.Weight => "kg",
        MeasurementType.MilkYield => "L",
        _ => "",
    };

    /// <summary>Plausible range for a value; anything outside is almost certainly a typing error.</summary>
    public static (decimal Min, decimal Max) Range(MeasurementType type) => type switch
    {
        MeasurementType.Weight => (1, 1500),
        MeasurementType.BodyCondition => (1, 9),
        MeasurementType.MilkYield => (0, 60),
        _ => (decimal.MinValue, decimal.MaxValue),
    };
}

public enum AdministrationRoute
{
    Intramuscular = 1,
    Subcutaneous = 2,
    Intravenous = 3,
    Oral = 4,
    PourOn = 5,
    Topical = 6,
    Other = 99,
}

/// <summary>A treatment, vaccination, test or procedure given to one animal.</summary>
public class TreatmentRecord : AuditableEntity, IFarmScoped
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public Guid TreatmentProductId { get; set; }
    public DateOnly GivenOn { get; set; }
    public decimal? Dose { get; set; }
    public string? DoseUnit { get; set; }
    public AdministrationRoute? Route { get; set; }
    public string? BatchNumber { get; set; }

    /// <summary>When the next dose / booster is due, for reminders (Step 6).</summary>
    public DateOnly? NextDueOn { get; set; }

    /// <summary>End of the meat withdrawal period, from the product's withdrawal days.</summary>
    public DateOnly? WithdrawalUntil { get; set; }

    /// <summary>The census / health programme this was part of, if any.</summary>
    public Guid? CensusSessionId { get; set; }
    public string? Notes { get; set; }
}

public enum ObservationCategory
{
    General = 1,
    LegsAndHooves = 2,
    PhysicalStructure = 3,
    Illness = 4,
    Injury = 5,
    Behaviour = 6,
}

/// <summary>A health or condition note, optionally with photos.</summary>
public class Observation : AuditableEntity, IFarmScoped
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public DateOnly ObservedOn { get; set; }
    public ObservationCategory Category { get; set; }
    public required string Notes { get; set; }
    public List<Photo> Photos { get; set; } = [];
}

/// <summary>An uploaded image. The bytes live in file storage; this row holds the metadata.</summary>
public class Photo : AuditableEntity, IFarmScoped
{
    public Guid FarmId { get; set; }
    public Guid AnimalId { get; set; }

    /// <summary>Set once the photo is attached to an observation.</summary>
    public Guid? ObservationId { get; set; }

    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
}
