namespace BFGDL.NET.Services;

/// <summary>
/// Downloads game images to per-game directories on disk and provides a fast
/// URL-to-local-path lookup so the WPF layer can load images without a network hit.
///
/// Directory layout:
///   cache/games/{WrapId}/images/{filename}
///   cache/games/{WrapId}/images/index.json   — URL → filename mapping
/// </summary>
public interface IDiskImageStore
{
    /// <summary>
    /// Scans existing per-game image index files and rebuilds the in-memory URL→path lookup.
    /// Must be called once at startup before any GetLocalPath / DownloadAsync calls.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the absolute local file path for a URL that has already been downloaded,
    /// or <c>null</c> if the URL is not in the local cache. Thread-safe.
    /// </summary>
    string? GetLocalPath(string url);

    /// <summary>
    /// Downloads the image at <paramref name="url"/> into
    /// <c>cache/games/{wrapId}/images/</c> if it has not been downloaded yet,
    /// then returns the absolute local path and whether the file was newly written.
    /// Returns <c>(null, false)</c> on failure.
    /// </summary>
    Task<(string? Path, bool IsNew)> DownloadAsync(string url, string wrapId, CancellationToken ct = default);
}
