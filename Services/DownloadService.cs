using System.Net.Http.Headers;
using System.Text;
using BFGDL.NET.Models;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET.Services;

public sealed class DownloadService(
    HttpClient httpClient,
    DownloadOptions options,
    ILogger<DownloadService> logger) : IDownloadService
{
    private static readonly string AppOutputRoot = AppContext.BaseDirectory;
    private static readonly string GamesRoot = Path.Combine(AppOutputRoot, "games");

    public async Task DownloadGameAsync(GameInfo gameInfo, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(GamesRoot);

        var gameDirectory = Path.Combine(GamesRoot, gameInfo.SanitizedDisplayName);
        Directory.CreateDirectory(gameDirectory);

        logger.LogInformation("Downloading game {GameName} to {Directory}", gameInfo.Name, gameDirectory);
        Console.WriteLine(gameInfo.SanitizedDisplayName);

        using var semaphore = new SemaphoreSlim(options.MaxConcurrentDownloads);
        var tasks = new List<Task>(gameInfo.Segments.Count);

        foreach (var segment in gameInfo.Segments)
        {
            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"  downloading: {segment.FileName}");
                    await DownloadSegmentAsync(segment, gameDirectory, cancellationToken);
                    Console.WriteLine($"  completed:   {segment.FileName}");
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        Console.WriteLine($"[OK] Completed download: {gameInfo.SanitizedDisplayName}");
    }

    public Task<string> GenerateDownloadListAsync(IEnumerable<GameInfo> games,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        foreach (var game in games)
        {
            sb.AppendLine($"# {game.SanitizedDisplayName}");

            foreach (var segment in game.Segments)
            {
                sb.AppendLine(segment.FullUrl);
                sb.AppendLine($" out={segment.FileName}");
            }

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    private async Task DownloadSegmentAsync(
        DownloadSegment segment,
        string directory,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(directory, segment.FileName);

        // Check if file already exists and resume if possible
        long startPosition = 0;
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            startPosition = fileInfo.Length;
            logger.LogDebug("Resuming download of {FileName} from position {Position}", segment.FileName,
                startPosition);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, segment.FullUrl);

            if (startPosition > 0) request.Headers.Range = new RangeHeaderValue(startPosition, null);

            using var response =
                await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                filePath,
                startPosition > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);

            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

            logger.LogDebug("Completed download of {FileName}", segment.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download {FileName}", segment.FileName);
            throw;
        }
    }
}