namespace BFGDL.NET.Services;

public interface IWrapIdFetcher
{
    Task<IReadOnlyList<string>> FetchWrapIdsAsync(int count, CancellationToken cancellationToken = default);
}