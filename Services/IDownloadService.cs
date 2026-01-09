using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

public interface IDownloadService
{
    Task DownloadGameAsync(GameInfo gameInfo, CancellationToken cancellationToken = default);
    Task<string> GenerateDownloadListAsync(IEnumerable<GameInfo> games, CancellationToken cancellationToken = default);
}