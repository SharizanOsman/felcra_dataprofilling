namespace Felcra.Profiling.Web.Field.Offline;

public enum OutboxKind
{
    Photo = 1,
    Measurement = 2,
    Treatment = 3,
    Observation = 4,
}

/// <summary>
/// A record saved on the phone and waiting to be sent. Items are sent in the order they were queued
/// (photos before the observation that uses them); each carries its own ID, so resending is safe.
/// </summary>
public sealed record OutboxItem
{
    /// <summary>Sort key: queue time + ID, so IndexedDB returns items in order.</summary>
    public required string Key { get; init; }

    public required Guid Id { get; init; }
    public required OutboxKind Kind { get; init; }
    public required Guid AnimalId { get; init; }

    /// <summary>The request body (JSON). Photos keep their bytes in the photos store instead.</summary>
    public required string Payload { get; init; }

    /// <summary>Short description for lists, e.g. "Weight 362 kg".</summary>
    public required string Summary { get; init; }

    public required DateOnly Date { get; init; }
    public required DateTimeOffset QueuedAt { get; init; }
    public int Attempts { get; init; }

    /// <summary>Set when the server rejected the item; it then waits for the user to retry or discard it.</summary>
    public string? Error { get; init; }

    public static string MakeKey(DateTimeOffset queuedAt, Guid id) => $"{queuedAt.ToUnixTimeMilliseconds():D15}-{id:N}";

    public string Path => Kind switch
    {
        OutboxKind.Photo => $"api/animals/{AnimalId}/photos",
        OutboxKind.Measurement => $"api/animals/{AnimalId}/measurements",
        OutboxKind.Treatment => $"api/animals/{AnimalId}/treatments",
        _ => $"api/animals/{AnimalId}/observations",
    };
}

/// <summary>Photo bytes waiting to be uploaded.</summary>
public sealed record PendingPhoto(Guid Id, Guid AnimalId, string ContentType, string Base64);
