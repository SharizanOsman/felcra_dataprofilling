namespace Felcra.Profiling.Application.Abstractions;

/// <summary>Stores uploaded files (photos). Local disk now; can be swapped for object storage.</summary>
public interface IFileStore
{
    /// <returns>A storage key to read the file back with.</returns>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <returns>The file, or null when it no longer exists.</returns>
    Stream? OpenRead(string storageKey);
}
