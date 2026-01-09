using BFGDL.NET.Configuration;
using BFGDL.NET.Models;
using BFGDL.NET.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await MainAsync(args);
    }

    private static async Task<int> MainAsync(string[] args)
    {
        try
        {
            var options = CommandLineOptions.Parse(args);

            if (options.ShowHelp)
            {
                CommandLineOptions.PrintHelp();
                return 0;
            }

            if (options.ShowVersion)
            {
                CommandLineOptions.PrintVersion();
                return 0;
            }

            // Setup dependency injection
            var services = new ServiceCollection();
            await ConfigureServices(services, options);

            var serviceProvider = services.BuildServiceProvider();
            var app = serviceProvider.GetRequiredService<Application>();

            await app.RunAsync(options);

            return 0;
        }
        catch (Exception ex)
        {
            // ReSharper disable once MethodHasAsyncOverload
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null) Console.Error.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
            return 1;
        }
    }

    private static async Task ConfigureServices(IServiceCollection services, CommandLineOptions options)
    {
        // Load configuration
        var configLoader = new ConfigurationLoader();
        var appConfig = await configLoader.LoadConfigurationAsync(options.ConfigFilePath);

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", true, false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json",
                true, false)
            .AddEnvironmentVariables("BFGDL__")
            .Build();

        services.AddSingleton<IConfiguration>(config);

        // Override config with command-line options if provided
        if (options.Platform.HasValue || options.Language.HasValue)
            appConfig = appConfig with
            {
                Platform = options.Platform.GetValueOrDefault(appConfig.Platform),
                Language = options.Language.GetValueOrDefault(appConfig.Language)
            };

        services.AddSingleton(appConfig);
        services.AddSingleton(new DownloadOptions
        {
            Download = options.Download,
            MaxConcurrentDownloads = options.MaxConcurrentDownloads,
            FetchFromInstallers = options.FetchFromInstallers
        });

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();

            // Apply appsettings.json -> Logging:* rules first
            builder.AddConfiguration(config.GetSection("Logging"));

            // Still keep the app's config.ini switch, but allow config overrides to win.
            if (appConfig.EnableDebugLogging)
                builder.SetMinimumLevel(LogLevel.Debug);
        });

        // HTTP Client
        services.AddHttpClient<IBigFishGamesClient, BigFishGamesClient>()
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BFGDL.NET/1.0");
                client.Timeout = TimeSpan.FromMinutes(30);
            });

        services.AddHttpClient<IDownloadService, DownloadService>()
            .ConfigureHttpClient(client => { client.Timeout = TimeSpan.FromHours(2); });

        // HTTP
        services.AddHttpClient();

        // Services
        services.AddTransient<InstallerWrapIdFetcher>();
        services.AddTransient<BigFishCatalogClient>();
        services.AddTransient<InstallerListExporter>();
        services.AddTransient<Application>();
    }
}