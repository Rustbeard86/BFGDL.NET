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
            var (langLabel, languageId) = GetLanguageInfo(configuration.Language);

            var wrapIds = new List<string>();
            var totalPages = 0;
            var totalCount = 0;
            var pagesParsed = 0;

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Enumerating catalog via GraphQL for {Platform}/{LangLabel}",
                    configuration.Platform, langLabel);

            var page = 1;
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
                }

                pagesParsed = page;

                if (pageResult.WrapIds.Count == 0)
                    break;

                wrapIds.AddRange(pageResult.WrapIds);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation(
                        "Catalog page {Page}/{TotalPages} ({LangLabel}): got {PageCount}; total so far {Total} / {Expected}",
                        page,
                        totalPages,
                        langLabel,
                        pageResult.WrapIds.Count,
                        wrapIds.Count,
                        totalCount);

                if (totalPages > 0 && page >= totalPages)
                    break;

                page++;
            }

            var distinctWrapIds = wrapIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var allWrapIds = new HashSet<string>(distinctWrapIds, StringComparer.OrdinalIgnoreCase);

            if (exportLimit.HasValue)
                allWrapIds = allWrapIds.Take(exportLimit.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (logger.IsEnabled(LogLevel.Information))
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
                        if (logger.IsEnabled(LogLevel.Warning))
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
                PagesParsedByLanguageL = new Dictionary<string, int> { [langLabel] = pagesParsed },
                WrapIdsFoundByLanguageL = new Dictionary<string, int> { [langLabel] = distinctWrapIds.Count },
                GamesExportedByLanguageL = gamesExportedByL.ToDictionary(k => k.Key, v => v.Value,
                    StringComparer.OrdinalIgnoreCase),
                CatalogTotalPagesByLanguageL = new Dictionary<string, int> { [langLabel] = totalPages },
                CatalogTotalCountByLanguageL = new Dictionary<string, int> { [langLabel] = totalCount },
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

            if (logger.IsEnabled(LogLevel.Information))
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

    private static (string LangLabel, string LanguageId) GetLanguageInfo(Language language)
    {
        return language switch
        {
            Language.English => ("L1", "114"),
            Language.German => ("L2", "117"),
            Language.Spanish => ("L3", "120"),
            Language.French => ("L4", "123"),
            Language.Italian => ("L7", "126"),
            Language.Japanese => ("L8", "129"),
            Language.Dutch => ("L10", "135"),
            Language.Swedish => ("L11", "138"),
            Language.Danish => ("L12", "141"),
            Language.Portuguese => ("L13", "144"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    [GeneratedRegex(@"T\d+L(\d+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex LanguageFromWrapIdRegex();
}