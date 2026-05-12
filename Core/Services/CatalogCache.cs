using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

/// <summary>
/// Disk-based JSON cache for catalog pages and game details.
///
/// TTLs (time-to-live):
///   Catalog page : 6 hours  — new games release roughly daily.
///   Game detail  : 24 hours — descriptions/screenshots rarely change.
///
/// The Refresh command in the GUI bypasses the cache (bypassCache = true in LoadPageAsync).
/// A background HTTP request is never made on cache-hit; the server is only contacted
/// when the cache is missing or stale.
/// </summary>
public sealed class CatalogCache(IAppPaths paths)
{
    private static readonly TimeSpan PageTtl   = TimeSpan.FromHours(6);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromHours(24);

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    // cache/catalog/ — paginated list snapshots
    private string CatalogDir => Path.Combine(paths.CacheDirectory, "catalog");

    // cache/games/{sku}/ — per-game detail + images
    internal string GamesDir => Path.Combine(paths.CacheDirectory, "games");

    // ── Pages ─────────────────────────────────────────────────────────────────

    public Task<CachedPageData?> TryGetPageAsync(
        Platform platform, Language language, int page, int pageSize,
        CancellationToken ct = default)
    {
        var file = PageFile(platform, language, page, pageSize);
        return TryReadAsync(file, PageTtl, AppJsonSerializerContext.Default.CachedPageData, ct);
    }

    /// <summary>
    /// Returns cached page data even if stale — used as offline fallback when the network is unavailable.
    /// </summary>
    public Task<CachedPageData?> TryGetPageStaleAsync(
        Platform platform, Language language, int page, int pageSize,
        CancellationToken ct = default)
    {
        var file = PageFile(platform, language, page, pageSize);
        return TryReadIgnoreTtlAsync(file, AppJsonSerializerContext.Default.CachedPageData, ct);
    }

    public Task SavePageAsync(
        Platform platform, Language language, int page, int pageSize,
        CachedPageData data, CancellationToken ct = default)
    {
        var file = PageFile(platform, language, page, pageSize);
        return WriteAsync(file, data, AppJsonSerializerContext.Default.CachedPageData, ct);
    }

    private string PageFile(Platform platform, Language language, int page, int pageSize)
        => Path.Combine(CatalogDir, $"{platform}_{language}_{page}_{pageSize}.json");

    // ── Details ───────────────────────────────────────────────────────────────

    public Task<CatalogGameDetail?> TryGetDetailAsync(
        string sku, Language language, CancellationToken ct = default)
    {
        var file = DetailFile(sku, language);
        return TryReadAsync(file, DetailTtl, AppJsonSerializerContext.Default.CatalogGameDetail, ct);
    }

    /// <summary>
    /// Returns detail data even if stale — used as offline fallback when the network is unavailable.
    /// </summary>
    public Task<CatalogGameDetail?> TryGetDetailStaleAsync(
        string sku, Language language, CancellationToken ct = default)
    {
        var file = DetailFile(sku, language);
        return TryReadIgnoreTtlAsync(file, AppJsonSerializerContext.Default.CatalogGameDetail, ct);
    }

    public Task SaveDetailAsync(CatalogGameDetail detail, CancellationToken ct = default)
    {
        var file = DetailFile(detail.WrapId, detail.Language);
        return WriteAsync(file, detail, AppJsonSerializerContext.Default.CatalogGameDetail, ct);
    }

    private string DetailFile(string sku, Language language)
        => Path.Combine(GamesDir, Sanitize(sku), $"detail_{language}.json");

    // ── Internals ─────────────────────────────────────────────────────────────

    private static bool IsFresh(string path, TimeSpan ttl)
        => File.Exists(path) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)) < ttl;

    private static async Task<T?> TryReadAsync<T>(
        string path, TimeSpan ttl, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (!IsFresh(path, ttl)) return default;
        try
        {
            await using var s = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(s, typeInfo, ct).ConfigureAwait(false);
        }
        catch
        {
            // Corrupted or incompatible cache file — treat as a miss.
            TryDelete(path);
            return default;
        }
    }

    private static async Task<T?> TryReadIgnoreTtlAsync<T>(
        string path, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        try
        {
            await using var s = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(s, typeInfo, ct).ConfigureAwait(false);
        }
        catch { return default; }
    }

    private static async Task WriteAsync<T>(
        string path, T data, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            await using (var s = File.Create(tmp))
                await JsonSerializer.SerializeAsync(s, data, typeInfo, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Cache write failures are non-fatal — the app continues without caching.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static string Sanitize(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(InvalidChars, chars[i]) >= 0)
                chars[i] = '_';
        return new string(chars);
    }
}
