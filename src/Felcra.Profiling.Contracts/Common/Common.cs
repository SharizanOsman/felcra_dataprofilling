namespace Felcra.Profiling.Contracts.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

/// <summary>An update request carrying the version the client last read (optimistic concurrency).</summary>
public interface IVersionedRequest
{
    uint Version { get; set; }
}

/// <summary>A DTO with an identifier, used by generic client-side list/edit components.</summary>
public interface IHasId
{
    Guid Id { get; }
}
