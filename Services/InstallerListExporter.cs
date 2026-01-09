using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using BFGDL.NET.Models;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET.Services;

public sealed partial class InstallerListExporter(
    AppConfiguration configuration,
    IBigFishGamesClient apiClient,
    BigFishCatalogClient catalogClient,
    ILogger<InstallerListExporter> logger)
{
    private const int PageSize = 250;

    private static readonly JsonSerializerOptions PrettyJsonSerializerOptions = new() { WriteIndented = true };

    public async Task ExportAsync(string exportFormat, int jobs, int? exportLimit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportFormat);
        ArgumentOutOfRangeException.ThrowIfLessThan(jobs, 1);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var pretty = exportFormat.Equals("pretty", StringComparison.OrdinalIgnoreCase);
        var jsonOptions = new JsonWriterOptions { Indented = pretty, SkipValidation = false };

        var outputRoot = AppContext.BaseDirectory;

        var pagesParsedByL = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var wrapIdsFoundByL = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gamesExportedByL = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var totalSegmentsExported = 0;
        var failedGames = 0;
        var failures = new ConcurrentBag<InstallerListExportFailure>();

        var writerByL = new Dictionary<string, Utf8JsonWriter>(StringComparer.OrdinalIgnoreCase);
        var streamByL = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var languagesToFetch = new (string LangLabel, string LanguageId)[]
            {
                ("L1", "114"),
                ("L2", "117"),
                ("L3", "120"),
                ("L4", "123"),
                ("L7", "126"),
                ("L8", "129"),
                ("L10", "135"),
                ("L11", "138"),
                ("L12", "141"),
                ("L13", "144")
            };

            var wrapIdsByLang = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var totalPagesByLang = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalCountByLang = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            const int catalogConcurrency = 4;
            using var catalogSem = new SemaphoreSlim(catalogConcurrency);

            var catalogTasks = languagesToFetch.Select(async t =>
            {
                var (langLabel, languageId) = t;
                await catalogSem.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    logger.LogInformation("Enumerating catalog via GraphQL for {Platform}/{LangLabel}",
                        configuration.Platform, langLabel);

                    var page = 1;
                    var langWrapIds = new List<string>();
                    var totalPages = 0;
                    var totalCount = 0;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var pageResult = await catalogClient
                            .GetCatalogPageAsync(configuration.Platform, languageId, page, PageSize, cancellationToken)
                            .ConfigureAwait(false);

                        if (page == 1)
                        {
                            totalPages = pageResult.TotalPages;
                            totalCount = pageResult.TotalCount;
                            totalPagesByLang[langLabel] = totalPages;
                            totalCountByLang[langLabel] = totalCount;
                        }

                        pagesParsedByL[langLabel] = page;

                        if (pageResult.WrapIds.Count == 0)
                            break;

                        langWrapIds.AddRange(pageResult.WrapIds);

                        logger.LogInformation(
                            "Catalog page {Page}/{TotalPages} ({LangLabel}): got {PageCount}; total so far {Total} / {Expected}",
                            page,
                            totalPages,
                            langLabel,
                            pageResult.WrapIds.Count,
                            langWrapIds.Count,
                            totalCount);

                        if (totalPages > 0 && page >= totalPages)
                            break;

                        page++;
                    }

                    var distinct = langWrapIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    wrapIdsByLang[langLabel] = distinct;
                    wrapIdsFoundByL[langLabel] = distinct.Count;
                }
                finally
                {
                    catalogSem.Release();
                }
            });

            await Task.WhenAll(catalogTasks).ConfigureAwait(false);

            var allWrapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ids in wrapIdsByLang.Values)
            foreach (var id in ids)
                allWrapIds.Add(id);

            if (exportLimit.HasValue)
                allWrapIds = allWrapIds.Take(exportLimit.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            logger.LogInformation("Resolving game info for {Count} WrapIDs (jobs={Jobs})", allWrapIds.Count, jobs);

            using var sem = new SemaphoreSlim(jobs);
            var tasks = new List<Task>(allWrapIds.Count);

            foreach (var wrapId in allWrapIds)
            {
                await sem.WaitAsync(cancellationToken).ConfigureAwait(false);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var game = await apiClient.GetGameInfoAsync(wrapId, cancellationToken).ConfigureAwait(false);

                        if (game.Segments.Count == 0)
                            return;

                        var langLabel = GetWrapIdLanguageLabel(wrapId);

                        lock (writerByL)
                        {
                            if (!writerByL.TryGetValue(langLabel, out var writer))
                            {
                                var filePath = Path.Combine(outputRoot,
                                    $"installers_{configuration.Platform}_" + langLabel + ".json");
                                var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read,
                                    1024 * 64);
                                var newWriter = new Utf8JsonWriter(stream, jsonOptions);

                                newWriter.WriteStartArray();

                                streamByL[langLabel] = stream;
                                writerByL[langLabel] = newWriter;
                                writer = newWriter;
                            }

                            WriteGame(writer, game);
                            gamesExportedByL.AddOrUpdate(langLabel, 1, (_, v) => v + 1);
                            Interlocked.Add(ref totalSegmentsExported, game.Segments.Count);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // propagate
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failedGames);
                        failures.Add(new InstallerListExportFailure
                        {
                            WrapId = wrapId,
                            ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                            Message = ex.Message
                        });
                        logger.LogWarning(ex, "Failed to export WrapID {WrapId}", wrapId);
                    }
                    finally
                    {
                        sem.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var (lang, writer) in writerByL)
            {
                writer.WriteEndArray();
                writer.Flush();
                writer.Dispose();

                if (streamByL.TryGetValue(lang, out var s))
                    s.Dispose();
            }

            var meta = new InstallerListExportMetadata
            {
                Platform = configuration.Platform.ToString(),
                ExportFormat = exportFormat,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                DurationSeconds = sw.Elapsed.TotalSeconds,
                PageSize = PageSize,
                PagesParsedByLanguageL = pagesParsedByL.ToDictionary(k => k.Key, v => v.Value,
                    StringComparer.OrdinalIgnoreCase),
                WrapIdsFoundByLanguageL = wrapIdsFoundByL.ToDictionary(k => k.Key, v => v.Value,
                    StringComparer.OrdinalIgnoreCase),
                GamesExportedByLanguageL = gamesExportedByL.ToDictionary(k => k.Key, v => v.Value,
                    StringComparer.OrdinalIgnoreCase),
                CatalogTotalPagesByLanguageL = totalPagesByLang,
                CatalogTotalCountByLanguageL = totalCountByLang,
                TotalWrapIdsFound = allWrapIds.Count,
                TotalGamesExported = gamesExportedByL.Values.Sum(),
                TotalSegmentsExported = totalSegmentsExported,
                FailedGames = failedGames,
                Failures = failures.IsEmpty
                    ? null
                    : failures.OrderBy(f => f.WrapId, StringComparer.OrdinalIgnoreCase).ToList(),
                Jobs = jobs,
                ExportLimit = exportLimit
            };

            var metaPath = Path.Combine(outputRoot, $"installers_{configuration.Platform}_meta.json");

            await using var metaStream = new FileStream(metaPath, FileMode.Create, FileAccess.Write, FileShare.Read,
                1024 * 64, true);
            await JsonSerializer.SerializeAsync(metaStream, meta, PrettyJsonSerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Installer JSON export complete. Total games: {TotalGames}; failed: {FailedGames}; duration: {Duration}.",
                meta.TotalGamesExported,
                failedGames,
                sw.Elapsed);
        }
        finally
        {
            foreach (var writer in writerByL.Values)
                try
                {
                    writer.Dispose();
                }
                catch
                {
                    /* ignored */
                }

            foreach (var stream in streamByL.Values)
                try
                {
                    stream.Dispose();
                }
                catch
                {
                    /* ignored */
                }
        }
    }

    private static void WriteGame(Utf8JsonWriter writer, GameInfo game)
    {
        writer.WriteStartObject();

        writer.WriteString("wrapId", game.WrapId);
        writer.WriteString("gameId", game.Id);
        writer.WriteString("name", game.Name);
        writer.WriteNumber("segmentCount", game.Segments.Count);

        writer.WriteStartArray("segments");
        foreach (var s in game.Segments)
        {
            writer.WriteStartObject();
            writer.WriteString("url", s.FullUrl);
            writer.WriteString("fileName", s.FileName);
            writer.WriteString("urlName", s.UrlName);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string GetWrapIdLanguageLabel(string wrapId)
    {
        var m = LanguageFromWrapIdRegex().Match(wrapId);
        if (!m.Success) return "L?";
        return "L" + m.Groups[1].Value;
    }

    [GeneratedRegex(@"T\d+L(\d+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex LanguageFromWrapIdRegex();
}