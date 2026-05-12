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
/// Intended for the CLI <c>--cache-catalog</c> command.
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

    // ── Stats ─────────────────────────────────────────────────────────────────

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

    public async Task FetchAllAsync(
        Platform platform,
        IEnumerable<Language> languages,
        CancellationToken ct)
    {
        var langList = languages.ToList();
        var allStats = new List<(Language lang, RunStats stats, TimeSpan elapsed)>();
        var totalSw = Stopwatch.StartNew();

        foreach (var language in langList)
        {
            ct.ThrowIfCancellationRequested();
            var (stats, elapsed) = await FetchLanguageAsync(platform, language, ct);
            allStats.Add((language, stats, elapsed));
        }

        PrintFinalReport(platform, allStats, totalSw.Elapsed);
    }

    // ── Per-language ──────────────────────────────────────────────────────────

    private async Task<(RunStats stats, TimeSpan elapsed)> FetchLanguageAsync(
        Platform platform, Language language, CancellationToken ct)
    {
        var stats = new RunStats();
        var sw = Stopwatch.StartNew();
        var langId = LanguageIdForEnum(language);
        var page = 1;
        int totalPages = 0;
        var detailFetched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine();
        Console.WriteLine($"┌─ {platform} / {language} ────────────────────────────────────────");

        do
        {
            ct.ThrowIfCancellationRequested();

            // ── Try page cache first (resume support) ──────────────────────
            var cachedPage = await cache.TryGetPageAsync(
                platform, language, page, PageSize, ct).ConfigureAwait(false);

            IReadOnlyList<CatalogGameSummary> pageItems;

            if (cachedPage is not null)
            {
                pageItems  = cachedPage.Items;
                totalPages = cachedPage.TotalPages;
                stats.PagesFromCache++;
                Console.WriteLine($"│  Page {page}/{totalPages} — {pageItems.Count} games [cached]");
            }
            else
            {
                Console.Write($"│  Page {page}…");
                BigFishCatalogClient.CatalogPageSummary result;
                try
                {
                    result = await catalog.GetCatalogPageWithSummaryAsync(
                        platform, language, langId, page, PageSize, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Console.WriteLine($" FAILED: {ex.Message}");
                    logger.LogError(ex, "Failed to fetch page {Page} for {Platform}/{Language}",
                        page, platform, language);
                    break;
                }

                pageItems  = result.Items;
                totalPages = result.TotalPages;
                stats.PagesFromNetwork++;
                Console.WriteLine($" {result.Items.Count} games  (total catalog: {result.TotalCount})");

                await cache.SavePageAsync(platform, language, page, PageSize,
                    new CachedPageData(result.Items, result.TotalCount, result.TotalPages), ct)
                    .ConfigureAwait(false);
            }

            // Process each game
            var n = pageItems.Count;
            for (var i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                Console.Write($"\r│    [{i + 1}/{n}] {Truncate(pageItems[i].Name, 46),-47}");
                await FetchGameAsync(pageItems[i], platform, language, detailFetched, stats, ct)
                    .ConfigureAwait(false);
            }

            // Clear rolling line
            Console.Write('\r');
            Console.WriteLine($"│  Page {page}/{totalPages} done — {n} games " +
                $"({stats.GamesNew} new, {stats.GamesSkipped} skipped, {stats.GamesFailed} failed)");

            page++;
            if (page <= totalPages)
                await Task.Delay(500, ct).ConfigureAwait(false);

        } while (totalPages == 0 || page <= totalPages);

        Console.WriteLine($"└─ {platform} / {language} complete ({FormatElapsed(sw.Elapsed)})");

        return (stats, sw.Elapsed);
    }

    // ── Per-game ──────────────────────────────────────────────────────────────

    private async Task FetchGameAsync(
        CatalogGameSummary game,
        Platform platform,
        Language language,
        HashSet<string> detailFetched,
        RunStats stats,
        CancellationToken ct)
    {
        // Thumbnail — cheap, DiskImageStore skips files already on disk
        if (!string.IsNullOrWhiteSpace(game.ThumbnailUrl))
            await TryDownloadImage(game.ThumbnailUrl, game.WrapId, stats, ct).ConfigureAwait(false);

        var detailKey = $"{game.WrapId}_{language}";
        if (detailFetched.Contains(detailKey)) { stats.GamesSkipped++; return; }

        var existing = await cache.TryGetDetailAsync(game.WrapId, language, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            detailFetched.Add(detailKey);
            stats.GamesSkipped++;
            return;
        }

        try
        {
            var detail = await catalog.GetProductDetailAsync(game.WrapId, platform, language, ct)
                .ConfigureAwait(false);

            if (detail is null)
            {
                stats.GamesFailed++;
                logger.LogWarning("No detail returned for {WrapId} ({Name})", game.WrapId, game.Name);
                return;
            }

            await cache.SaveDetailAsync(detail, ct).ConfigureAwait(false);
            detailFetched.Add(detailKey);
            stats.GamesNew++;

            var imageUrls = new List<string>(8);
            if (!string.IsNullOrWhiteSpace(detail.HeroImageUrl))    imageUrls.Add(detail.HeroImageUrl);
            if (!string.IsNullOrWhiteSpace(detail.FeatureImageUrl)) imageUrls.Add(detail.FeatureImageUrl);
            imageUrls.AddRange(detail.ScreenshotUrls.Where(u => !string.IsNullOrWhiteSpace(u)));

            foreach (var url in imageUrls)
                await TryDownloadImage(url, detail.WrapId, stats, ct).ConfigureAwait(false);

            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            stats.GamesFailed++;
            logger.LogWarning(ex, "Failed to fetch detail for {WrapId}", game.WrapId);
        }
    }

    // ── Image download with stats ─────────────────────────────────────────────

    private async Task TryDownloadImage(
        string url, string wrapId, RunStats stats, CancellationToken ct)
    {
        // If already indexed in memory, it was downloaded this session or a prior one
        var alreadyKnown = diskImageStore.GetLocalPath(url) is not null;
        var path = await diskImageStore.DownloadAsync(url, wrapId, ct).ConfigureAwait(false);

        if (path is null)  { stats.ImagesFailed++; return; }

        if (alreadyKnown)  { stats.ImagesOnDisk++;  return; }

        // Not in memory index but file existed on disk (cold start, previous run)
        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
        if (age > TimeSpan.FromSeconds(5))
             { stats.ImagesOnDisk++; return; }

        stats.ImagesNew++;
    }

    // ── Final report ──────────────────────────────────────────────────────────

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

    private static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1  ? $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s" :
        t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:D2}s" :
                              $"{t.Seconds}.{t.Milliseconds / 100}s";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string LanguageIdForEnum(Language lang) => lang switch
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
