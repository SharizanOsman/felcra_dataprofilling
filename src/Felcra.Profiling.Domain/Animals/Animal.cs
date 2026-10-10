using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Animals;

public enum Sex
{
    Male = 1,
    Female = 2,
}

/// <summary>Herd category as used on the FFISB census form.</summary>
public enum AnimalCategory
{
    /// <summary>Pejantan (P).</summary>
    Bull = 1,

    /// <summary>Induk (I).</summary>
    Dam = 2,

    /// <summary>Anak jantan (AJ).</summary>
    MaleCalf = 3,

    /// <summary>Anak betina (AB).</summary>
    FemaleCalf = 4,
}

public enum AnimalStatus
{
    Active = 1,
    Sold = 2,
    Dead = 3,
    Culled = 4,
    Transferred = 5,
}

public enum AgeEstimationMethod
{
    /// <summary>Birth date is known from records.</summary>
    Recorded = 1,

    /// <summary>Estimated by examining teeth.</summary>
    Dentition = 2,

    /// <summary>Estimated by the owner / staff.</summary>
    OwnerEstimate = 3,
}

/// <summary>
/// An individual animal. The ear-tag number is only an attribute: tags can be missing, duplicated
/// or replaced, so identity is <see cref="Entity.Id"/> (encoded in the QR label) plus a readable
/// <see cref="AnimalNo"/>.
/// </summary>
public class Animal : AuditableEntity, IFarmScoped
{
    /// <summary>Sequential number assigned by the server, shown as e.g. "A000123".</summary>
    public int AnimalNo { get; set; }

    public Guid FarmId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? HerdGroupId { get; set; }
    public Guid? BreedId { get; set; }

    /// <summary>Current ear-tag number, if tagged. History is kept in <see cref="TagHistory"/>.</summary>
    public string? TagNumber { get; set; }

    public Sex Sex { get; set; }
    public AnimalCategory Category { get; set; }

    public DateOnly? BirthDate { get; set; }
    public AgeEstimationMethod? BirthDateMethod { get; set; }

    public Guid? DamId { get; set; }
    public Guid? SireId { get; set; }

    public AnimalStatus Status { get; set; } = AnimalStatus.Active;
    public DateOnly? StatusDate { get; set; }
    public string? StatusReason { get; set; }

    public string? Remarks { get; set; }

    public List<AnimalTag> TagHistory { get; set; } = [];

    public static string FormatAnimalNo(int animalNo) => $"A{animalNo:D6}";

    /// <summary>Sex implied by a census category.</summary>
    public static Sex SexOf(AnimalCategory category) =>
        category is AnimalCategory.Bull or AnimalCategory.MaleCalf ? Sex.Male : Sex.Female;

    /// <summary>Records a new tag number, closing the previous one in the history.</summary>
    public void Retag(string? newTagNumber, DateOnly on, string? reason)
    {
        newTagNumber = NormaliseTag(newTagNumber);
        if (newTagNumber == TagNumber) return;

        foreach (var open in TagHistory.Where(t => t.RemovedOn is null))
            open.RemovedOn = on;

        if (newTagNumber is not null)
            TagHistory.Add(new AnimalTag { AnimalId = Id, TagNumber = newTagNumber, AssignedOn = on, Reason = reason });

        TagNumber = newTagNumber;
    }

    /// <summary>Trims the tag and treats placeholders such as "NO. TAG" as untagged.</summary>
    public static string? NormaliseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = string.Join(' ', tag.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var compact = tag.Replace(".", "").Replace(" ", "").ToUpperInvariant();
        return compact is "NOTAG" or "TIADATAG" or "NOTAGGED" or "-" or "NA" ? null : tag;
    }

    /// <summary>Age in whole months at <paramref name="on"/>, if a birth date is known.</summary>
    public int? AgeInMonths(DateOnly on) =>
        BirthDate is { } birth
            ? Math.Max(0, (on.Year - birth.Year) * 12 + on.Month - birth.Month - (on.Day < birth.Day ? 1 : 0))
            : null;
}

/// <summary>A tag number an animal carried during a period.</summary>
public class AnimalTag : Entity
{
    public Guid AnimalId { get; set; }
    public required string TagNumber { get; set; }
    public DateOnly AssignedOn { get; set; }
    public DateOnly? RemovedOn { get; set; }
    public string? Reason { get; set; }
}
