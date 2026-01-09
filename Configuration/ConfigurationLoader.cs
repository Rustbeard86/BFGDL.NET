using BFGDL.NET.Models;
using JetBrains.Annotations;

namespace BFGDL.NET.Configuration;

public interface IConfigurationLoader
{
    [UsedImplicitly]
    Task<AppConfiguration> LoadConfigurationAsync(string? configPath = null);
}

public sealed class ConfigurationLoader : IConfigurationLoader
{
    private const string DefaultConfigFileName = "config.ini";

    public Task<AppConfiguration> LoadConfigurationAsync(string? configPath = null)
    {
        var filePath = configPath ?? DefaultConfigFileName;

        if (!File.Exists(filePath)) return Task.FromResult(AppConfiguration.Default);

        var config = ParseConfigFile(filePath);
        return Task.FromResult(config);
    }

    private static AppConfiguration ParseConfigFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2) settings[parts[0].Trim()] = parts[1].Trim();
        }

        return new AppConfiguration
        {
            Platform = ParsePlatform(settings.GetValueOrDefault("platform", "win")),
            Language = ParseLanguage(settings.GetValueOrDefault("language", "eng")),
            GenerateScript = ParseBool(settings.GetValueOrDefault("gen_script", "true")),
            EnableDebugLogging = ParseBool(settings.GetValueOrDefault("enable_debug_logging", "false"))
        };
    }

    private static Platform ParsePlatform(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "win" or "windows" => Platform.Windows,
            "mac" or "macos" => Platform.Mac,
            _ => Platform.Windows
        };
    }

    private static Language ParseLanguage(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "eng" or "english" => Language.English,
            "ger" or "german" => Language.German,
            "spa" or "spanish" => Language.Spanish,
            "fre" or "french" => Language.French,
            "ita" or "italian" => Language.Italian,
            "jap" or "japanese" => Language.Japanese,
            "dut" or "dutch" => Language.Dutch,
            "swe" or "swedish" => Language.Swedish,
            "dan" or "danish" => Language.Danish,
            "por" or "portuguese" => Language.Portuguese,
            _ => Language.English
        };
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var result) ? result : 50;
    }
}