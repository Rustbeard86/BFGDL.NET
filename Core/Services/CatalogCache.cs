using System.Runtime.CompilerServices;
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

    // ── Full-catalog enumeration ──────────────────────────────────────────────

    /// <summary>
    /// Yields every cached page for the given platform/language.
    /// Batch files written by <see cref="CatalogFetchService"/> are yielded newest-first (LIFO).
    /// Legacy browser page-numbered files are yielded afterward for backward compatibility.
    /// Callers are responsible for deduplication by WrapId.
    /// </summary>
    public async IAsyncEnumerable<CachedPageData> EnumerateAllCachedPagesAsync(
        Platform platform, Language language, int pageSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = CatalogDir;
        if (!Directory.Exists(dir)) yield break;

        // Batch files — LIFO so newest content is returned first
        await foreach (var page in EnumerateBatchFilesAsync(platform, language, pageSize, ct))
            yield return page;

        // Legacy browser page-numbered files — ascending, for backward compatibility
        var prefix = $"{platform}_{language}_";
        var suffix = $"_{pageSize}.json";
        var pageFiles = Directory.GetFiles(dir, $"{prefix}*{suffix}")
            .Select(f => (file: f, page: ParsePageNumber(Path.GetFileNameWithoutExtension(f), prefix, pageSize)))
            .Where(x => x.page > 0)
            .OrderBy(x => x.page)
            .Select(x => x.file);

        foreach (var file in pageFiles)
        {
            ct.ThrowIfCancellationRequested();
            CachedPageData? data = null;
            try
            {
                await using var s = File.OpenRead(file);
                data = await JsonSerializer.DeserializeAsync(
                    s, AppJsonSerializerContext.Default.CachedPageData, ct).ConfigureAwait(false);
            }
            catch { }
            if (data is not null) yield return data;
        }
    }

    private static int ParsePageNumber(string nameWithoutExt, string prefix, int pageSize)
    {
        if (!nameWithoutExt.StartsWith(prefix, StringComparison.Ordinal)) return 0;
        var inner = nameWithoutExt[prefix.Length..];
        var pageSuffix = $"_{pageSize}";
        if (!inner.EndsWith(pageSuffix, StringComparison.Ordinal)) return 0;
        var pageStr = inner[..^pageSuffix.Length];
        return int.TryParse(pageStr, out var n) ? n : 0;
    }

    // ── One-time migration ────────────────────────────────────────────────────

    /// <summary>
    /// Converts any legacy page-numbered cache files
    /// (<c>{Platform}_{Language}_{page}_{pageSize}.json</c>) into a single batch file
    /// and deletes the originals. Safe to call on every startup — skips any
    /// platform/language combination that already has batch files.
    /// </summary>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var dir = CatalogDir;
        if (!Directory.Exists(dir)) return;

        // Collect legacy page files grouped by (prefix, pageSize).
        var groups = new Dictionary<(string prefix, int pageSize), List<(string file, int page)>>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var name  = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_');
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[^1], out var pageSize)) continue;
            if (!int.TryParse(parts[^2], out var pageNum))  continue; // batch files have "batch######" here — skip
            var prefix = string.Join("_", parts[..^2]);
            var key    = (prefix, pageSize);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add((file, pageNum));
        }

        foreach (var ((prefix, pageSize), pages) in groups)
        {
            ct.ThrowIfCancellationRequested();

            // Parse "Windows_English" → Platform.Windows, Language.English
            var sep = prefix.IndexOf('_');
            if (sep < 0) continue;
            if (!Enum.TryParse<Platform>(prefix[..sep],   out var platform)) continue;
            if (!Enum.TryParse<Language>(prefix[(sep+1)..], out var language)) continue;

            var batchPrefix = $"{platform}_{language}_batch";
            var batchSuffix = $"_{pageSize}.json";
            var hasBatch    = Directory.EnumerateFiles(dir, $"{batchPrefix}*{batchSuffix}").Any();

            if (!hasBatch)
            {
                // Read all pages ascending, dedup by WrapId.
                var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allItems = new List<CatalogGameSummary>();
                int totalCount = 0, totalPages = 0;

                foreach (var (file, _) in pages.OrderBy(p => p.page))
                {
                    ct.ThrowIfCancellationRequested();
                    CachedPageData? data = null;
                    try
                    {
                        await using var s = File.OpenRead(file);
                        data = await JsonSerializer.DeserializeAsync(
                            s, AppJsonSerializerContext.Default.CachedPageData, ct).ConfigureAwait(false);
                    }
                    catch { continue; }
                    if (data is null) continue;
                    totalCount = data.TotalCount;
                    totalPages = data.TotalPages;
                    foreach (var item in data.Items)
                        if (seen.Add(item.WrapId))
                            allItems.Add(item);
                }

                if (allItems.Count == 0) continue;

                await AppendBatchAsync(platform, language, pageSize,
                    allItems, totalCount, totalPages, ct).ConfigureAwait(false);
            }

            // Always delete legacy page files — even if the batch already existed
            // (handles the case where deletion was partial on a previous run).
            foreach (var (file, _) in pages)
                TryDelete(file);
        }
    }

    // ── Append-only batch store (used by CatalogFetchService) ─────────────────

    /// <summary>
    /// Appends a batch of new game summaries as a new sequenced file.
    /// Each call gets a monotonically increasing sequence number so newer fetches
    /// always sort after older ones under LIFO enumeration. Does nothing when
    /// <paramref name="items"/> is empty.
    /// </summary>
    public Task AppendBatchAsync(
        Platform platform, Language language, int pageSize,
        IReadOnlyList<CatalogGameSummary> items, int totalCount, int totalPages,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return Task.CompletedTask;
        var seq  = NextBatchSeqNum(platform, language, pageSize);
        var file = BatchFile(platform, language, seq, pageSize);
        return WriteAsync(file, new CachedPageData(items, totalCount, totalPages),
            AppJsonSerializerContext.Default.CachedPageData, ct);
    }

    /// <summary>
    /// Returns the set of all WrapIds stored in any cache file (batch or legacy page-numbered)
    /// for the given platform/language. Used by <see cref="CatalogFetchService"/> to skip games
    /// already on disk, including on first run after migrating from the old page-file format.
    /// </summary>
    public async Task<HashSet<string>> GetKnownWrapIdsAsync(
        Platform platform, Language language, int pageSize,
        CancellationToken ct = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var page in EnumerateAllCachedPagesAsync(platform, language, pageSize, ct))
            foreach (var item in page.Items)
                ids.Add(item.WrapId);
        return ids;
    }

    private string BatchFile(Platform platform, Language language, int seqNum, int pageSize)
        => Path.Combine(CatalogDir, $"{platform}_{language}_batch{seqNum:D6}_{pageSize}.json");

    private int NextBatchSeqNum(Platform platform, Language language, int pageSize)
    {
        var dir = CatalogDir;
        if (!Directory.Exists(dir)) return 1;
        var prefix = $"{platform}_{language}_batch";
        var suffix = $"_{pageSize}.json";
        var max = Directory.EnumerateFiles(dir, $"{prefix}*{suffix}")
            .Select(f => ParseBatchSeqNum(Path.GetFileNameWithoutExtension(f), prefix, pageSize))
            .DefaultIfEmpty(0)
            .Max();
        return max + 1;
    }

    private static int ParseBatchSeqNum(string nameWithoutExt, string prefix, int pageSize)
    {
        if (!nameWithoutExt.StartsWith(prefix, StringComparison.Ordinal)) return 0;
        var inner = nameWithoutExt[prefix.Length..];
        var pageSuffix = $"_{pageSize}";
        if (!inner.EndsWith(pageSuffix, StringComparison.Ordinal)) return 0;
        var seqStr = inner[..^pageSuffix.Length];
        return int.TryParse(seqStr, out var n) ? n : 0;
    }

    private async IAsyncEnumerable<CachedPageData> EnumerateBatchFilesAsync(
        Platform platform, Language language, int pageSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = CatalogDir;
        if (!Directory.Exists(dir)) yield break;
        var prefix = $"{platform}_{language}_batch";
        var suffix = $"_{pageSize}.json";
        var files = Directory.GetFiles(dir, $"{prefix}*{suffix}")
            .Select(f => (file: f, seq: ParseBatchSeqNum(Path.GetFileNameWithoutExtension(f), prefix, pageSize)))
            .Where(x => x.seq > 0)
            .OrderByDescending(x => x.seq)
            .Select(x => x.file);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            CachedPageData? data = null;
            try
            {
                await using var s = File.OpenRead(file);
                data = await JsonSerializer.DeserializeAsync(
                    s, AppJsonSerializerContext.Default.CachedPageData, ct).ConfigureAwait(false);
            }
            catch { }
            if (data is not null) yield return data;
        }
    }

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

    public bool DetailExistsOnDisk(string sku, Language language)
        => File.Exists(DetailFile(sku, language));

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
