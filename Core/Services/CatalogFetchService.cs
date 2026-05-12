using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

/// <summary>
/// Fetches the entire Big Fish Games catalog (all pages, all details, and all images)
/// for one or more languages and stores everything to the local disk cache.
///
/// Resume-safe: re-running the command after an interruption will skip anything
/// already saved to disk — pages within TTL, game details within TTL,
/// images that already exist as files on disk.
///
/// Intended for the CLI <c>--cache-catalog</c> command and the GUI Cache Manager panel.
/// </summary>
public sealed class CatalogFetchService(
    BigFishCatalogClient catalog,
    CatalogCache cache,
    IDiskImageStore diskImageStore,
    ILogger<CatalogFetchService> logger)
{
    private const int PageSize = 48;

    public static readonly IReadOnlyList<Language> SupportedLanguages =
        Enum.GetValues<Language>().ToList();

    // ── Stats (all fields updated via Interlocked for thread safety) ──────────

    private sealed class RunStats
    {
        public int PagesFromNetwork;
        public int PagesFromCache;
        public int GamesNew;
        public int GamesSkipped;
        public int GamesFailed;
        public int ImagesNew;
        public int ImagesOnDisk;
        public int ImagesFailed;
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <param name="platform">Target platform.</param>
    /// <param name="languages">Languages to fetch.</param>
    /// <param name="concurrencyLevel">Max parallel game-detail/image fetches per page (default 4).</param>
    /// <param name="progress">
    /// Optional GUI progress sink. Receives a snapshot after each page and on language completion.
    /// Pass <c>null</c> for CLI usage — Console output is used instead.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task FetchAllAsync(
        Platform platform,
        IEnumerable<Language> languages,
        int concurrencyLevel = 4,
        IProgress<CatalogFetchProgress>? progress = null,
        CancellationToken ct = default)
    {
        var langList = languages.ToList();
        var allStats = new List<(Language lang, RunStats stats, TimeSpan elapsed)>();
        var totalSw = Stopwatch.StartNew();

        foreach (var language in langList)
        {
            ct.ThrowIfCancellationRequested();
            var (stats, elapsed) = await FetchLanguageAsync(
                platform, language, concurrencyLevel, progress, ct);
            allStats.Add((language, stats, elapsed));
        }

        if (progress is null)
            PrintFinalReport(platform, allStats, totalSw.Elapsed);
    }

    // ── Per-language ──────────────────────────────────────────────────────────

    private async Task<(RunStats stats, TimeSpan elapsed)> FetchLanguageAsync(
        Platform platform, Language language, int concurrencyLevel,
        IProgress<CatalogFetchProgress>? progress, CancellationToken ct)
    {
        var stats = new RunStats();
        var sw = Stopwatch.StartNew();
        var langId = LanguageIdForEnum(language);
        var page = 1;
        int totalPages = 0;
        // Thread-safe dedup across parallel game fetches within a page
        var detailFetched = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        if (progress is null)
        {
            Console.WriteLine();
            Console.WriteLine($"┌─ {platform} / {language} ────────────────────────────────────────");
        }

        do
        {
            ct.ThrowIfCancellationRequested();

            // ── Try page cache first (resume support) ──────────────────────
            var cachedPage = await cache.TryGetPageAsync(
                platform, language, page, PageSize, ct).ConfigureAwait(false);

            IReadOnlyList<CatalogGameSummary> pageItems;
            List<CatalogGameDetail>? networkDetails = null;

            if (cachedPage is not null)
            {
                pageItems  = cachedPage.Items;
                totalPages = cachedPage.TotalPages;
                Interlocked.Increment(ref stats.PagesFromCache);
                if (progress is null)
                    Console.WriteLine($"│  Page {page}/{totalPages} — {pageItems.Count} games [cached]");
            }
            else
            {
                if (progress is null) Console.Write($"│  Page {page}…");
                (List<CatalogGameDetail> Details, int TotalCount, int TotalPages) result;
                try
                {
                    result = await catalog.GetCatalogPageWithDetailAsync(
                        platform, language, langId, page, PageSize, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (progress is null) Console.WriteLine($" FAILED: {ex.Message}");
                    logger.LogError(ex, "Failed to fetch page {Page} for {Platform}/{Language}",
                        page, platform, language);
                    break;
                }

                networkDetails = result.Details;
                pageItems  = networkDetails; // CatalogGameDetail : CatalogGameSummary — covariant
                totalPages = result.TotalPages;
                Interlocked.Increment(ref stats.PagesFromNetwork);
                if (progress is null)
                    Console.WriteLine($" {networkDetails.Count} games  (total catalog: {result.TotalCount})");

                await cache.SavePageAsync(platform, language, page, PageSize,
                    new CachedPageData(pageItems, result.TotalCount, result.TotalPages), ct)
                    .ConfigureAwait(false);
            }

            // ── Process games in parallel ──────────────────────────────────
            var n = pageItems.Count;
            var processed = 0;
            var opts = new ParallelOptions { MaxDegreeOfParallelism = concurrencyLevel, CancellationToken = ct };

            if (networkDetails is not null)
            {
                // Network path: details already in the page response — save + download images, no extra HTTP
                await Parallel.ForEachAsync(
                    networkDetails,
                    opts,
                    async (detail, gameCt) =>
                    {
                        var idx = Interlocked.Increment(ref processed);
                        if (progress is null)
                            Console.Write($"\r│    [{idx}/{n}] {Truncate(detail.Name, 46),-47}");
                        var key = $"{detail.WrapId}_{language}";
                        if (!detailFetched.TryAdd(key, 0)) { Interlocked.Increment(ref stats.GamesSkipped); return; }
                        await cache.SaveDetailAsync(detail, gameCt).ConfigureAwait(false);
                        Interlocked.Increment(ref stats.GamesNew);
                        await FetchImagesAsync(detail, stats, gameCt).ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }
            else
            {
                // Cached page path: load detail from cache, download any missing images
                await Parallel.ForEachAsync(
                    pageItems,
                    opts,
                    async (game, gameCt) =>
                    {
                        var idx = Interlocked.Increment(ref processed);
                        if (progress is null)
                            Console.Write($"\r│    [{idx}/{n}] {Truncate(game.Name, 46),-47}");
                        await FetchGameFromCacheAsync(game, platform, language, detailFetched, stats, gameCt)
                            .ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }

            if (progress is null)
            {
                Console.Write('\r');
                Console.WriteLine($"│  Page {page}/{totalPages} done — {n} games " +
                    $"({stats.GamesNew} new, {stats.GamesSkipped} skipped, {stats.GamesFailed} failed)");
            }

            progress?.Report(new CatalogFetchProgress
            {
                Language      = language,
                Page          = page,
                TotalPages    = totalPages,
                GamesNew      = stats.GamesNew,
                GamesSkipped  = stats.GamesSkipped,
                GamesFailed   = stats.GamesFailed,
                ImagesNew     = stats.ImagesNew,
                ImagesOnDisk  = stats.ImagesOnDisk,
                ImagesFailed  = stats.ImagesFailed,
                IsComplete    = false,
            });

            page++;
            if (page <= totalPages)
                await Task.Delay(200, ct).ConfigureAwait(false);

        } while (totalPages == 0 || page <= totalPages);

        if (progress is null)
            Console.WriteLine($"└─ {platform} / {language} complete ({FormatElapsed(sw.Elapsed)})");

        // Final completion snapshot for this language
        progress?.Report(new CatalogFetchProgress
        {
            Language      = language,
            Page          = totalPages,
            TotalPages    = totalPages,
            GamesNew      = stats.GamesNew,
            GamesSkipped  = stats.GamesSkipped,
            GamesFailed   = stats.GamesFailed,
            ImagesNew     = stats.ImagesNew,
            ImagesOnDisk  = stats.ImagesOnDisk,
            ImagesFailed  = stats.ImagesFailed,
            IsComplete    = true,
        });

        return (stats, sw.Elapsed);
    }

    // ── Per-game (cached-page path) ───────────────────────────────────────────

    private async Task FetchGameFromCacheAsync(
        CatalogGameSummary game,
        Platform platform,
        Language language,
        ConcurrentDictionary<string, byte> detailFetched,
        RunStats stats,
        CancellationToken ct)
    {
        var detailKey = $"{game.WrapId}_{language}";
        if (!detailFetched.TryAdd(detailKey, 0))
        {
            Interlocked.Increment(ref stats.GamesSkipped);
            return;
        }

        // Detail should already be cached from the original network fetch
        var detail = await cache.TryGetDetailAsync(game.WrapId, language, ct).ConfigureAwait(false)
                  ?? await cache.TryGetDetailStaleAsync(game.WrapId, language, ct).ConfigureAwait(false);

        if (detail is null)
        {
            // Fallback: detail somehow missing — fetch from network
            try
            {
                detail = await catalog.GetProductDetailAsync(game.WrapId, platform, language, ct)
                    .ConfigureAwait(false);
                if (detail is not null)
                    await cache.SaveDetailAsync(detail, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch detail for {WrapId}", game.WrapId);
            }
        }

        if (detail is null) { Interlocked.Increment(ref stats.GamesFailed); return; }

        Interlocked.Increment(ref stats.GamesSkipped);
        await FetchImagesAsync(detail, stats, ct).ConfigureAwait(false);
    }

    private async Task FetchImagesAsync(CatalogGameDetail detail, RunStats stats, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(detail.ThumbnailUrl))
            await TryDownloadImage(detail.ThumbnailUrl, detail.WrapId, stats, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(detail.HeroImageUrl))
            await TryDownloadImage(detail.HeroImageUrl, detail.WrapId, stats, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(detail.FeatureImageUrl))
            await TryDownloadImage(detail.FeatureImageUrl, detail.WrapId, stats, ct).ConfigureAwait(false);
        foreach (var url in detail.ScreenshotUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
            await TryDownloadImage(url, detail.WrapId, stats, ct).ConfigureAwait(false);
    }

    // ── Image download with stats ─────────────────────────────────────────────

    private async Task TryDownloadImage(
        string url, string wrapId, RunStats stats, CancellationToken ct)
    {
        var alreadyKnown = diskImageStore.GetLocalPath(url) is not null;
        var path = await diskImageStore.DownloadAsync(url, wrapId, ct).ConfigureAwait(false);

        if (path is null) { Interlocked.Increment(ref stats.ImagesFailed); return; }

        if (alreadyKnown) { Interlocked.Increment(ref stats.ImagesOnDisk); return; }

        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
        if (age > TimeSpan.FromSeconds(5))
             { Interlocked.Increment(ref stats.ImagesOnDisk); return; }

        Interlocked.Increment(ref stats.ImagesNew);
    }

    // ── Final report (CLI only) ───────────────────────────────────────────────

    private static void PrintFinalReport(
        Platform platform,
        List<(Language lang, RunStats stats, TimeSpan elapsed)> allStats,
        TimeSpan totalElapsed)
    {
        const string Bar = "═══════════════════════════════════════════════════════════════";
        const string Div = "───────────────────────────────────────────────────────────────";

        Console.WriteLine();
        Console.WriteLine(Bar);
        Console.WriteLine($"  CATALOG CACHE REPORT  ·  {platform}  ·  {FormatElapsed(totalElapsed)}");
        Console.WriteLine(Div);
        Console.WriteLine($"  {"Language",-14}  {"Pages",6}  {"Network",8}  {"Cached",8}  " +
                          $"{"Games",6}  {"New",6}  {"Skip",6}  {"Fail",6}  " +
                          $"{"Images",7}  {"New",6}  {"OnDisk",7}  {"Fail",6}");
        Console.WriteLine(Div);

        var gP = (net: 0, cached: 0);
        var gG = (fetched: 0, skipped: 0, failed: 0);
        var gI = (downloaded: 0, onDisk: 0, failed: 0);

        foreach (var (lang, s, elapsed) in allStats)
        {
            var tp = s.PagesFromNetwork + s.PagesFromCache;
            var tg = s.GamesNew + s.GamesSkipped + s.GamesFailed;
            var ti = s.ImagesNew + s.ImagesOnDisk + s.ImagesFailed;

            Console.WriteLine(
                $"  {lang,-14}  {tp,6}  {s.PagesFromNetwork,8}  {s.PagesFromCache,8}  " +
                $"{tg,6}  {s.GamesNew,6}  {s.GamesSkipped,6}  {s.GamesFailed,6}  " +
                $"{ti,7}  {s.ImagesNew,6}  {s.ImagesOnDisk,7}  {s.ImagesFailed,6}");

            gP = (gP.net + s.PagesFromNetwork, gP.cached + s.PagesFromCache);
            gG = (gG.fetched + s.GamesNew, gG.skipped + s.GamesSkipped, gG.failed + s.GamesFailed);
            gI = (gI.downloaded + s.ImagesNew, gI.onDisk + s.ImagesOnDisk, gI.failed + s.ImagesFailed);
        }

        if (allStats.Count > 1)
        {
            Console.WriteLine(Div);
            var tp = gP.net + gP.cached;
            var tg = gG.fetched + gG.skipped + gG.failed;
            var ti = gI.downloaded + gI.onDisk + gI.failed;
            Console.WriteLine(
                $"  {"TOTAL",-14}  {tp,6}  {gP.net,8}  {gP.cached,8}  " +
                $"{tg,6}  {gG.fetched,6}  {gG.skipped,6}  {gG.failed,6}  " +
                $"{ti,7}  {gI.downloaded,6}  {gI.onDisk,7}  {gI.failed,6}");
        }

        Console.WriteLine(Bar);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1   ? $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s" :
        t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:D2}s" :
                              $"{t.Seconds}.{t.Milliseconds / 100}s";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    internal static string LanguageIdForEnum(Language lang) => lang switch
    {
        Language.English    => "114",
        Language.German     => "117",
        Language.Spanish    => "120",
        Language.French     => "123",
        Language.Italian    => "126",
        Language.Japanese   => "129",
        Language.Dutch      => "135",
        Language.Swedish    => "138",
        Language.Danish     => "141",
        Language.Portuguese => "144",
        _                   => "114"
    };
}
