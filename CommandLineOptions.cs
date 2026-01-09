using BFGDL.NET.Models;

namespace BFGDL.NET;

public sealed record CommandLineOptions
{
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public bool Download { get; init; }
    public bool FetchFromInstallers { get; init; }

    public string? ExportInstallersJson
    {
        get;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                field = null;
                return;
            }

            var normalized = value.Trim().ToLowerInvariant();
            field = normalized switch
            {
                "pretty" => "pretty",
                "min" => "min",
                _ => throw new ArgumentException("Invalid value for --export-installers-json. Use 'pretty' or 'min'.")
            };
        }
    }

    public int? ExportLimit
    {
        get;
        init
        {
            if (value.HasValue)
                ArgumentOutOfRangeException.ThrowIfLessThan(value.Value, 1);
            field = value;
        }
    }

    public int MaxConcurrentDownloads
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 64);
            field = value;
        }
    } = 8;

    public string? ConfigFilePath
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public Platform? Platform { get; init; }
    public Language? Language { get; init; }

    public List<string> WrapIds
    {
        get;
        init => field =
            [.. value.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim().ToUpperInvariant())];
    } = [];

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        var wrapIds = new List<string>();
        var download = false;
        var fetchFromInstallers = false;
        var showHelp = false;
        var showVersion = false;
        var maxConcurrent = 8;
        string? configFilePath = null;
        Platform? platform = null;
        Language? language = null;
        string? exportInstallersJson = null;
        int? exportLimit = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;

                case "-v" or "--version":
                    showVersion = true;
                    break;

                case "-d" or "--download":
                    download = true;
                    break;

                case "-e" or "--extract":
                    fetchFromInstallers = true;
                    break;

                case "-j" or "--jobs":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var jobs))
                    {
                        maxConcurrent = jobs;
                        i++; // Skip next arg
                    }
                    else
                    {
                        throw new ArgumentException("Invalid value for -j/--jobs flag");
                    }

                    break;

                case "-c" or "--config":
                    if (i + 1 < args.Length)
                    {
                        configFilePath = args[i + 1];
                        i++; // Skip next arg
                    }
                    else
                    {
                        throw new ArgumentException("Missing value for -c/--config flag");
                    }

                    break;

                case "-p" or "--platform":
                    if (i + 1 < args.Length)
                    {
                        platform = ParsePlatform(args[i + 1]);
                        i++; // Skip next arg
                    }
                    else
                    {
                        throw new ArgumentException("Missing value for -p/--platform flag");
                    }

                    break;

                case "-l" or "--language":
                    if (i + 1 < args.Length)
                    {
                        language = ParseLanguage(args[i + 1]);
                        i++; // Skip next arg
                    }
                    else
                    {
                        throw new ArgumentException("Missing value for -l/--language flag");
                    }

                    break;

                default:
                    if (arg.StartsWith("--export-installers-json=", StringComparison.OrdinalIgnoreCase))
                    {
                        exportInstallersJson = arg.Split('=', 2)[1];
                        break;
                    }

                    if (arg.StartsWith("--export-limit=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = arg.Split('=', 2)[1];
                        if (!int.TryParse(value, out var limit) || limit < 1)
                            throw new ArgumentException(
                                "Invalid value for --export-limit. Must be a positive integer.");
                        exportLimit = limit;
                        break;
                    }

                    if (!arg.StartsWith('-'))
                        wrapIds.Add(arg);
                    else
                        throw new ArgumentException($"Unknown argument: {arg}");
                    break;
            }
        }

        return options with
        {
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            Download = download,
            FetchFromInstallers = fetchFromInstallers,
            MaxConcurrentDownloads = maxConcurrent,
            ConfigFilePath = configFilePath,
            Platform = platform,
            Language = language,
            WrapIds = wrapIds,
            ExportInstallersJson = exportInstallersJson,
            ExportLimit = exportLimit
        };
    }

    private static Platform ParsePlatform(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "win" or "windows" => Models.Platform.Windows,
            "mac" or "macos" => Models.Platform.Mac,
            _ => throw new ArgumentException($"Invalid platform: {value}. Use 'win' or 'mac'.")
        };
    }

    private static Language ParseLanguage(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "eng" or "english" => Models.Language.English,
            "ger" or "german" => Models.Language.German,
            "spa" or "spanish" => Models.Language.Spanish,
            "fre" or "french" => Models.Language.French,
            "ita" or "italian" => Models.Language.Italian,
            "jap" or "japanese" => Models.Language.Japanese,
            "dut" or "dutch" => Models.Language.Dutch,
            "swe" or "swedish" => Models.Language.Swedish,
            "dan" or "danish" => Models.Language.Danish,
            "por" or "portuguese" => Models.Language.Portuguese,
            _ => throw new ArgumentException($"Invalid language: {value}")
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
                          BFGDL.NET - Big Fish Games Downloader

                          Usage: BFGDL.NET [OPTIONS] [WrapID...]

                          With no flags, BFGDL.NET will output a download list of links.

                          Options:
                            -h, --help                       Display this help message
                            -v, --version                    Display version information
                            -e, --extract                    Fetch links using installers in current directory
                            -d, --download                   Download files after fetching
                            -j, --jobs N                     Set number of concurrent downloads (default: 8, max: 64)
                            -c, --config FILE                Load configuration from FILE (default: config.ini)
                            -p, --platform PLATFORM          Set platform: win, mac (overrides config)
                            -l, --language LANG              Set language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por
                            --export-installers-json=pretty|min  Export full (non-demo) installer segment lists grouped by WrapID language (L#)
                            --export-limit=N                 Limit number of games exported (for testing)

                          Examples:
                            Fetch links for specific games:
                              BFGDL.NET F15533T1L2 F7028T1L1 F1T1L1

                            Download one game with 4 concurrent downloads:
                              BFGDL.NET -d -j 4 F5260T1L1

                            Download games using installers in current directory:
                              BFGDL.NET -e -d

                            Export Windows installer lists to JSON (pretty):
                              BFGDL.NET --export-installers-json=pretty -p win

                            Export limited sample (minified):
                              BFGDL.NET --export-installers-json=min --export-limit=50 -p win
                          """);
    }

    public static void PrintVersion()
    {
        Console.WriteLine("BFGDL.NET v1.0.0 - C# .NET 10 Implementation");
    }
}