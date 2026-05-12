namespace BFGDL.NET.Services;

/// <summary>
/// Default implementation — paths relative to the application's base directory.
/// Used by the CLI.
/// </summary>
public sealed class DefaultAppPaths : IAppPaths
{
    private static readonly string Base = AppContext.BaseDirectory;

    public string OutputRoot => Base;
    public string GamesDirectory => Path.Combine(Base, "games");
    public string InstallersDirectory => Path.Combine(Base, "installers");
    public string CacheDirectory => Path.Combine(Base, "cache");
}
