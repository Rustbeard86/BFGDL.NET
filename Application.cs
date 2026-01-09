using BFGDL.NET.Models;
using BFGDL.NET.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET;

public sealed class Application(
    IServiceProvider serviceProvider,
    AppConfiguration appConfiguration,
    IBigFishGamesClient apiClient,
    IDownloadService downloadService,
    ILogger<Application> logger)
{
    public async Task RunAsync(CommandLineOptions options)
    {
        Console.WriteLine("Big Fish Games Downloader - .NET");

        if (options.ExportInstallersJson is not null)
        {
            var jobs = options.MaxConcurrentDownloads;
            var exporter = serviceProvider.GetRequiredService<InstallerListExporter>();

            Console.WriteLine(
                $"Exporting installer lists to JSON ({options.ExportInstallersJson}) for {appConfiguration.Platform}...");

            await exporter.ExportAsync(options.ExportInstallersJson, jobs, options.ExportLimit, CancellationToken.None);

            Console.WriteLine("[OK] Installer JSON export completed.");
            return;
        }

        // Get WrapIDs
        var wrapIds = await GetWrapIdsAsync(options);

        if (wrapIds.Count == 0)
        {
            Console.Error.WriteLine("No WrapIDs found. Use -h for help.");
            return;
        }

        Console.WriteLine($"Processing {wrapIds.Count} game(s)...");

        // Fetch game information
        var games = new List<GameInfo>();
        foreach (var wrapId in wrapIds)
            try
            {
                var game = await apiClient.GetGameInfoAsync(wrapId);
                games.Add(game);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch game info for WrapID: {WrapId}", wrapId);
                Console.Error.WriteLine($"Failed to fetch info for {wrapId}: {ex.Message}");
            }

        if (games.Count == 0)
        {
            Console.Error.WriteLine("No valid games found.");
            return;
        }

        // Download or generate list
        if (options.Download)
            await DownloadGamesAsync(games);
        else
            await GenerateDownloadListAsync(games);

        Console.WriteLine("[OK] Operation completed!");
    }

    private async Task<List<string>> GetWrapIdsAsync(CommandLineOptions options)
    {
        // Priority 1: Fetch from installers
        if (options.FetchFromInstallers)
        {
            Console.WriteLine("Scanning for installers...");
            var installerFetcher = serviceProvider.GetRequiredService<InstallerWrapIdFetcher>();
            var wrapIds = await installerFetcher.FetchWrapIdsAsync(int.MaxValue);
            return [.. wrapIds];
        }

        // Priority 2: Use provided WrapIDs
        if (options.WrapIds.Count > 0) return options.WrapIds;

        Console.Error.WriteLine(
            "No WrapIDs specified. Use -e to scan installers or provide WrapIDs as arguments.");
        return [];
    }

    private async Task DownloadGamesAsync(List<GameInfo> games)
    {
        Console.WriteLine($"Starting downloads with {games.Sum(g => g.Segments.Count)} segments...");

        foreach (var game in games)
            try
            {
                await downloadService.DownloadGameAsync(game);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to download game: {GameName}", game.Name);
                Console.Error.WriteLine($"Failed to download {game.Name}: {ex.Message}");
            }
    }

    private async Task GenerateDownloadListAsync(List<GameInfo> games)
    {
        var downloadList = await downloadService.GenerateDownloadListAsync(games);

        var outputFile = Path.Combine(AppContext.BaseDirectory, "download-list.txt");
        await File.WriteAllTextAsync(outputFile, downloadList);

        Console.WriteLine($"[OK] Download list saved to: {outputFile}");
        Console.WriteLine($"Use with: BFGDL.NET -d $(cat {outputFile})");
    }
}