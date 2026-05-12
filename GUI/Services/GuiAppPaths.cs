using System.IO;

namespace BFGDL.NET.Services;

public sealed class GuiAppPaths : IAppPaths
{
    private string _outputRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "BFGDL.NET");

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
    public string CacheDirectory => Path.Combine(OutputRoot, "cache");
}
