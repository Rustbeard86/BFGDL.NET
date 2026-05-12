using System.Text.RegularExpressions;
using BFGDL.NET.Models;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET.Services;

public sealed partial class InstallerWrapIdFetcher(ILogger<InstallerWrapIdFetcher> logger) : IWrapIdFetcher
{
    private static readonly string InstallersDirectory = Path.Combine(AppContext.BaseDirectory, "installers");

    public Task<IReadOnlyList<string>> FetchWrapIdsAsync(int count, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InstallersDirectory);
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Scanning directory for installers: {Directory}", InstallersDirectory);

        var wrapIds = Directory.GetFiles(InstallersDirectory, "*l1_gF*")
            .Select(Path.GetFileName)
            .Select(fileName => InstallerPattern().Match(fileName!))
            .Where(match => match.Success)
            .Select(match => WrapId.TryParse("F" + match.Groups[1].Value))
            .Where(wrapId => wrapId is not null)
            .Select(wrapId =>
            {
                var id = wrapId!.Value;
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Found WrapID {WrapId} from installer", id);
                return id;
            })
            .ToList();

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Found {Count} WrapIDs from installers", wrapIds.Count);

        return Task.FromResult<IReadOnlyList<string>>(wrapIds);
    }

    [GeneratedRegex("l1_gF([^_]+)_", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InstallerPattern();
}