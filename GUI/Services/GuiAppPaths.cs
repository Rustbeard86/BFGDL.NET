using System.IO;

namespace BFGDL.NET.Services;

public sealed class GuiAppPaths : IAppPaths
{
    private string _outputRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "BFGDL.NET");

    public string OutputRoot
    {
        get => _outputRoot;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _outputRoot = value;
        }
    }

    public string GamesDirectory => Path.Combine(OutputRoot, "games");
    public string InstallersDirectory => Path.Combine(OutputRoot, "installers");

    // Cache lives adjacent to the binary so that publish cache backup/restore works
    // regardless of where the user's My Documents folder is.
    public string CacheDirectory => Path.Combine(AppContext.BaseDirectory, "cache");
}
