using Felcra.Profiling.Contracts.Census;

namespace Felcra.Profiling.Application.Census;

/// <summary>Imports census sheets (.xlsx or .csv) into a census session, registering new animals as needed.</summary>
public interface ICensusImportService
{
    /// <summary>
    /// Parses the file and shows what an import would do. Returns null when the session does not
    /// exist (or is not visible to the user); <see cref="ImportOutcome.FileError"/> for unreadable files.
    /// </summary>
    /// <param name="matchExisting">Match rows to existing animals by tag number; otherwise every row registers a new animal.</param>
    Task<ImportOutcome?> PreviewAsync(Guid sessionId, Stream file, string fileName, bool matchExisting, CancellationToken ct = default);

    /// <summary>Imports the file if the preview has no errors.</summary>
    Task<ImportOutcome?> ImportAsync(Guid sessionId, Stream file, string fileName, bool matchExisting, CancellationToken ct = default);

    /// <summary>An empty .xlsx in the FFISB census layout.</summary>
    byte[] CreateTemplate();
}

public sealed record ImportOutcome(ImportPreviewDto? Preview, ImportResultDto? Result, string? FileError);
