namespace BFGDL.NET.Models;

public sealed record AppConfiguration
{
    public required Platform Platform { get; init; }
    public required Language Language { get; init; }
    public required bool GenerateScript { get; init; }

    public bool EnableDebugLogging { get; init; }

    public static AppConfiguration Default => new()
    {
        Platform = Platform.Windows,
        Language = Language.English,
        GenerateScript = true,
        EnableDebugLogging = false
    };
}

public enum Platform
{
    Windows,
    Mac
}

public enum Language
{
    English,
    German,
    Spanish,
    French,
    Italian,
    Japanese,
    Dutch,
    Swedish,
    Danish,
    Portuguese
}

public sealed record DownloadOptions
{
    public bool Download { get; init; }

    public int MaxConcurrentDownloads
    {
        get => field;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 64);
            field = value;
        }
    } = 8;

    public bool FetchFromInstallers { get; init; }

    public string DownloadUrl
    {
        get => field;
        init => field = value.TrimEnd('/') + "/";
    } = "http://binscentral.bigfishgames.com/downloads/";
}