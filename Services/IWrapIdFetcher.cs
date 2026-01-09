using JetBrains.Annotations;

namespace BFGDL.NET.Services;

public interface IWrapIdFetcher
{
    [UsedImplicitly]
    Task<IReadOnlyList<string>> FetchWrapIdsAsync(int count, CancellationToken cancellationToken = default);
}