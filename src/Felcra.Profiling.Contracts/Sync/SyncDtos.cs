using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Contracts.Sync;

/// <summary>An animal as cached on a field device.</summary>
public sealed record SyncAnimalDto(
    Guid Id,
    string AnimalNo,
    string? TagNumber,
    Sex Sex,
    AnimalCategory Category,
    DateOnly? BirthDate,
    AgeEstimationMethod? BirthDateMethod,
    Guid FarmId,
    string FarmCode,
    string? ProjectCode,
    AnimalStatus Status,
    decimal? LatestWeightKg,
    DateOnly? LatestWeighedOn,
    decimal? LatestBcs,
    DateOnly? LatestBcsOn);

/// <summary>
/// Changes since the device's last pull. Animals are incremental; the small reference lists are
/// always sent in full.
/// </summary>
public sealed record SyncPullDto(
    DateTimeOffset ServerTime,
    bool IsFull,
    IReadOnlyList<SyncAnimalDto> Animals,
    IReadOnlyList<Guid> RemovedAnimalIds,
    IReadOnlyList<TreatmentProductDto> TreatmentProducts,
    IReadOnlyList<CensusSessionDto> OpenCensusSessions);
