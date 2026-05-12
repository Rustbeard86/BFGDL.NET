namespace BFGDL.NET.Services;

/// <summary>
/// Abstracts filesystem paths so both the CLI and GUI can control where files are stored.
/// </summary>
public interface IAppPaths
{
    /// <summary>Root directory under which all application output is written.</summary>
    string OutputRoot { get; }

    /// <summary>Directory where downloaded game files are saved.</summary>
    string GamesDirectory { get; }

    /// <summary>Directory scanned for local installer files when using -e.</summary>
    string InstallersDirectory { get; }

    /// <summary>Directory where catalog/detail JSON cache files are stored.</summary>
    string CacheDirectory { get; }
}
