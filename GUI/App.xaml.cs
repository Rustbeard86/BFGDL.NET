using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Splat;
using BFGDL.NET.Models;
using BFGDL.NET.Services;
using BFGDL.NET.ViewModels;

namespace BFGDL.NET;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();   // merges Dark.xaml into Application.Resources
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface any unhandled exceptions instead of silently exiting
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        Locator.CurrentMutable.InitializeReactiveUI();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Warm up disk image store — scans index.json files so disk-first loading works
        // immediately when the first game list is rendered.
        var diskStore = Services.GetRequiredService<IDiskImageStore>();
        await diskStore.InitializeAsync();
        ImageCache.DiskStore = diskStore;

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var logProvider = new ObservableLogProvider();
        services.AddSingleton(logProvider);
        services.AddLogging(b => b.AddProvider(logProvider).SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));

        // Paths
        services.AddSingleton<IAppPaths, GuiAppPaths>();

        // Download options — GUI always downloads, no CLI flags involved
        services.AddSingleton(new DownloadOptions { Download = true });

        // HTTP
        services.AddHttpClient<IBigFishGamesClient, BigFishGamesClient>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30));
        services.AddHttpClient<IDownloadService, DownloadService>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromHours(2));
        services.AddHttpClient<BigFishCatalogClient>();
        services.AddHttpClient<DiskImageStore>();

        // Services
        services.AddSingleton<IDiskImageStore, DiskImageStore>();
        services.AddSingleton<ImagePreloader>();
        services.AddSingleton<CatalogCache>();
        services.AddTransient<InstallerWrapIdFetcher>();
        services.AddTransient<CatalogFetchService>();

        // ViewModels
        services.AddSingleton<GameDetailViewModel>();
        services.AddSingleton<DownloadQueueViewModel>();
        services.AddSingleton<CacheManagerViewModel>();
        services.AddSingleton<BrowserViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }
}
