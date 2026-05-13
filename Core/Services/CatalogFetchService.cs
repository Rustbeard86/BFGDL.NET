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
        int totalPages = 0, totalCount = 0;

        // WrapIds already in batch files on disk — used to skip known games.
        // HashSet.Add also deduplicates within the current run.
        var knownWrapIds = await cache.GetKnownWrapIdsAsync(platform, language, PageSize, ct)
            .ConfigureAwait(false);

        if (progress is null)
        {
            Console.WriteLine();
            Console.WriteLine($"┌─ {platform} / {language} ────────────────────────────────────────");
        }

        do
        {
            ct.ThrowIfCancellationRequested();

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

            totalPages = result.TotalPages;
            totalCount = result.TotalCount;
            Interlocked.Increment(ref stats.PagesFromNetwork);

            // Partition into new vs already-known, registering new ids as we go.
            var newDetails = result.Details.Where(d => knownWrapIds.Add(d.WrapId)).ToList();
            var skipped    = result.Details.Count - newDetails.Count;
            Interlocked.Add(ref stats.GamesSkipped, skipped);

            if (progress is null)
                Console.WriteLine($" {result.Details.Count} games  ({newDetails.Count} new, {skipped} skipped)");

            if (newDetails.Count > 0)
            {
                var n         = newDetails.Count;
                var processed = 0;
                var opts      = new ParallelOptions { MaxDegreeOfParallelism = concurrencyLevel, CancellationToken = ct };

                await Parallel.ForEachAsync(
                    newDetails,
                    opts,
                    async (detail, gameCt) =>
                    {
                        var idx = Interlocked.Increment(ref processed);
                        if (progress is null)
                            Console.Write($"\r│    [{idx}/{n}] {Truncate(detail.Name, 46),-47}");
                        try
                        {
                            await cache.SaveDetailAsync(detail, gameCt).ConfigureAwait(false);
                            Interlocked.Increment(ref stats.GamesNew);
                            await FetchImagesAsync(detail, stats, gameCt).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref stats.GamesFailed);
                            logger.LogError(ex, "Failed to process game {WrapId} ({Name})",
                                detail.WrapId, detail.Name);
                        }
                    }).ConfigureAwait(false);

                if (progress is null) Console.Write('\r');

                await cache.AppendBatchAsync(platform, language, PageSize,
                    newDetails, totalCount, totalPages, ct).ConfigureAwait(false);
            }

            if (progress is null)
                Console.WriteLine($"│  Page {page}/{totalPages} done — {result.Details.Count} games " +
                    $"({stats.GamesNew} new, {stats.GamesSkipped} skipped, {stats.GamesFailed} failed)");

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
        var (path, isNew) = await diskImageStore.DownloadAsync(url, wrapId, ct).ConfigureAwait(false);

        if (path is null) { Interlocked.Increment(ref stats.ImagesFailed); return; }

        if (isNew) Interlocked.Increment(ref stats.ImagesNew);
        else       Interlocked.Increment(ref stats.ImagesOnDisk);
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
        Console.WriteLine($"  {"Language",-14}  {"Pages",6}  " +
                          $"{"Games",6}  {"New",6}  {"Skip",6}  {"Fail",6}  " +
                          $"{"Images",7}  {"New",6}  {"OnDisk",7}  {"Fail",6}");
        Console.WriteLine(Div);

        var gP = 0;
        var gG = (fetched: 0, skipped: 0, failed: 0);
        var gI = (downloaded: 0, onDisk: 0, failed: 0);

        foreach (var (lang, s, elapsed) in allStats)
        {
            var tg = s.GamesNew + s.GamesSkipped + s.GamesFailed;
            var ti = s.ImagesNew + s.ImagesOnDisk + s.ImagesFailed;

            Console.WriteLine(
                $"  {lang,-14}  {s.PagesFromNetwork,6}  " +
                $"{tg,6}  {s.GamesNew,6}  {s.GamesSkipped,6}  {s.GamesFailed,6}  " +
                $"{ti,7}  {s.ImagesNew,6}  {s.ImagesOnDisk,7}  {s.ImagesFailed,6}");

            gP += s.PagesFromNetwork;
            gG = (gG.fetched + s.GamesNew, gG.skipped + s.GamesSkipped, gG.failed + s.GamesFailed);
            gI = (gI.downloaded + s.ImagesNew, gI.onDisk + s.ImagesOnDisk, gI.failed + s.ImagesFailed);
        }

        if (allStats.Count > 1)
        {
            Console.WriteLine(Div);
            var tg = gG.fetched + gG.skipped + gG.failed;
            var ti = gI.downloaded + gI.onDisk + gI.failed;
            Console.WriteLine(
                $"  {"TOTAL",-14}  {gP,6}  " +
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
