using System.Collections.Concurrent;
using System.Text.Json;
using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

/// <summary>
/// Downloads game images to disk, keyed by game WrapId.
///
/// Each game gets its own subfolder:
///   cache/games/{WrapId}/images/{filename}
///   cache/games/{WrapId}/images/index.json
///
/// The index.json maps original URL → local filename so the mapping survives
/// CDN URL changes or filename collisions across different games.
///
/// Stale/old images are never deleted — they serve as offline fallback.
/// </summary>
public sealed class DiskImageStore(IAppPaths paths, HttpClient httpClient) : IDiskImageStore
{
    // URL (exact) → absolute local file path
    private readonly ConcurrentDictionary<string, string> _index =
        new(StringComparer.Ordinal);

    private string GamesDir => Path.Combine(paths.CacheDirectory, "games");

    private static string GameImagesDir(string gamesBase, string wrapId)
        => Path.Combine(gamesBase, wrapId, "images");

    private static string IndexFile(string imagesDir)
        => Path.Combine(imagesDir, "index.json");

    // ── Initialisation ────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var gamesDir = GamesDir;
        if (!Directory.Exists(gamesDir)) return;

        foreach (var indexFile in Directory.EnumerateFiles(gamesDir, "index.json",
                     SearchOption.AllDirectories))
        {
            try
            {
                await using var s = File.OpenRead(indexFile);
                var dict = await JsonSerializer.DeserializeAsync(
                    s, AppJsonSerializerContext.Default.DictionaryStringString, ct)
                    .ConfigureAwait(false);

                if (dict is null) continue;

                var imagesDir = Path.GetDirectoryName(indexFile)!;
                foreach (var (url, filename) in dict)
                {
                    var localPath = Path.Combine(imagesDir, filename);
                    if (File.Exists(localPath))
                        _index[url] = localPath;
                }
            }
            catch { /* corrupted index — skip this game's entry */ }
        }
    }

    // ── Lookup (any thread) ───────────────────────────────────────────────────

    public string? GetLocalPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return _index.TryGetValue(url, out var path) ? path : null;
    }

    // ── Download ──────────────────────────────────────────────────────────────

    public async Task<string?> DownloadAsync(string url, string wrapId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(wrapId))
            return null;

        // Already in local cache — no download needed
        if (_index.TryGetValue(url, out var cached)) return cached;

        try
        {
            var imagesDir = GameImagesDir(GamesDir, wrapId);
            Directory.CreateDirectory(imagesDir);

            var filename = UrlToFilename(url);
            var localPath = Path.Combine(imagesDir, filename);

            // File already on disk (e.g., downloaded by a previous run but index missing)
            if (File.Exists(localPath))
            {
                await UpdateIndexAsync(url, filename, imagesDir, ct).ConfigureAwait(false);
                _index[url] = localPath;
                return localPath;
            }

            // Download → temp file → atomic move
            var tmp = localPath + ".tmp";
            try
            {
                using var res = await httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                res.EnsureSuccessStatusCode();

                await using (var fileStream = File.Create(tmp))
                await using (var netStream = await res.Content
                                 .ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await netStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

                File.Move(tmp, localPath, overwrite: true);
            }
            catch (OperationCanceledException)
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                return null;
            }

            await UpdateIndexAsync(url, filename, imagesDir, ct).ConfigureAwait(false);
            _index[url] = localPath;
            return localPath;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends (or updates) a URL→filename entry in the game's images/index.json file.
    /// </summary>
    private static async Task UpdateIndexAsync(
        string url, string filename, string imagesDir, CancellationToken ct)
    {
        var indexFile = IndexFile(imagesDir);

        // Read existing entries so we don't overwrite them
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(indexFile))
        {
            try
            {
                await using var rs = File.OpenRead(indexFile);
                var existing = await JsonSerializer.DeserializeAsync(
                    rs, AppJsonSerializerContext.Default.DictionaryStringString, ct)
                    .ConfigureAwait(false);
                if (existing is not null)
                    foreach (var kv in existing)
                        dict[kv.Key] = kv.Value;
            }
            catch { /* corrupted — start fresh */ }
        }

        dict[url] = filename;

        var tmp = indexFile + ".tmp";
        await using (var ws = File.Create(tmp))
            await JsonSerializer.SerializeAsync(
                ws, dict, AppJsonSerializerContext.Default.DictionaryStringString, ct)
                .ConfigureAwait(false);
        File.Move(tmp, indexFile, overwrite: true);
    }

    /// <summary>
    /// Derives a safe local filename from a URL, falling back to a hash-based name.
    /// </summary>
    private static string UrlToFilename(string url)
    {
        try
        {
            var filename = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(filename)) filename = "image";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = filename.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            return new string(chars);
        }
        catch
        {
            return $"image_{Math.Abs(url.GetHashCode())}.bin";
        }
    }
}
