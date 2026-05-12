using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

public interface IBigFishGamesClient
{
    Task<GameInfo> GetGameInfoAsync(string wrapId, CancellationToken cancellationToken = default);
}