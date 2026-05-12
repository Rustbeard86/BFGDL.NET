using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using BFGDL.NET.Models;
using BFGDL.NET.Services;

namespace BFGDL.NET.ViewModels;

public partial class BrowserViewModel : ReactiveObject
{
    private readonly BigFishCatalogClient _catalog;
    private readonly CatalogCache _cache;
    private readonly ImagePreloader _preloader;
    private readonly IDiskImageStore _diskImageStore;
    private readonly SourceList<CatalogGameSummary> _games = new();
    private readonly ReadOnlyObservableCollection<CatalogGameSummary> _filteredGames;

    [Reactive] private string _searchText = string.Empty;
    [Reactive] private Platform _selectedPlatform = Platform.Windows;
    [Reactive] private Language _selectedLanguage = Language.English;
    [Reactive] private string? _selectedGenre;
    [Reactive] private CatalogGameSummary? _selectedGame;
    [Reactive] private bool _isLoading;
    [Reactive] private int _currentPage = 1;
    [Reactive] private int _totalPages;
    [Reactive] private int _totalCount;
    [Reactive] private string _statusText = string.Empty;

    public ReadOnlyObservableCollection<CatalogGameSummary> FilteredGames => _filteredGames;

    // All genres seen so far
    private readonly SourceList<string> _genres = new();
    private readonly ReadOnlyObservableCollection<string> _allGenres;
    public ReadOnlyObservableCollection<string> AllGenres => _allGenres;

    public IEnumerable<Platform> Platforms => Enum.GetValues<Platform>();
    public IEnumerable<Language> Languages => Enum.GetValues<Language>();

    public ReactiveCommand<Unit, Unit> LoadNextPageCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public GameDetailViewModel Detail { get; }
    public DownloadQueueViewModel DownloadQueue { get; }

    public BrowserViewModel(BigFishCatalogClient catalog, CatalogCache cache,
        ImagePreloader preloader, IDiskImageStore diskImageStore,
        GameDetailViewModel detail, DownloadQueueViewModel downloadQueue)
    {
        _catalog = catalog;
        _cache = cache;
        _preloader = preloader;
        _diskImageStore = diskImageStore;
        Detail = detail;
        DownloadQueue = downloadQueue;

        // Filter pipeline
        var filterChanged = this.WhenAnyValue(
            x => x.SearchText,
            x => x.SelectedGenre,
            (search, genre) => (search, genre));

        _games.Connect()
            .Filter(filterChanged.Select(f => BuildFilter(f.search, f.genre)))
            .Sort(SortExpressionComparer<CatalogGameSummary>.Descending(g => g.ReleaseDate))
            .Bind(out _filteredGames)
            .Subscribe();

        // Genre list — rebuild whenever the game list changes
        _games.Connect()
            .ToCollection()
            .Subscribe(items =>
            {
                var newGenres = items.SelectMany(g => g.Genres)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s)
                    .ToList();
                _genres.Edit(list => { list.Clear(); list.AddRange(newGenres); });
            });

        _genres.Connect()
            .Bind(out _allGenres)
            .Subscribe();

        // Load detail when selection changes
        this.WhenAnyValue(x => x.SelectedGame)
            .Where(g => g is not null)
            .Select(g => g!)
            .Subscribe(g => Detail.LoadGame(g));

        var canLoadNext = this.WhenAnyValue(
            x => x.CurrentPage, x => x.TotalPages, x => x.IsLoading,
            (cur, total, loading) => !loading && cur < total);

        LoadNextPageCommand = ReactiveCommand.CreateFromTask(LoadNextPageAsync, canLoadNext);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

        // Initial load
        RefreshCommand.Execute().Subscribe();
    }

    private static Func<CatalogGameSummary, bool> BuildFilter(string search, string? genre)
    {
        return g =>
        {
            if (!string.IsNullOrWhiteSpace(search) &&
                !g.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !g.ShortDescription.Contains(search, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrWhiteSpace(genre) &&
                !g.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                return false;
            return true;
        };
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        _games.Clear();
        _currentPage = 1;
        await LoadPageAsync(1, ct, bypassCache: true);
    }

    private async Task LoadNextPageAsync(CancellationToken ct)
    {
        await LoadPageAsync(_currentPage + 1, ct, bypassCache: false);
    }

    private async Task LoadPageAsync(int page, CancellationToken ct, bool bypassCache = false)
    {
        IsLoading = true;
        StatusText = $"Loading page {page}…";
        try
        {
            if (!bypassCache)
            {
                var hit = await _cache.TryGetPageAsync(SelectedPlatform, SelectedLanguage, page, 48, ct);
                if (hit is not null)
                {
                    _games.AddRange(hit.Items);
                    _currentPage = page;
                    TotalPages = hit.TotalPages;
                    TotalCount = hit.TotalCount;
                    StatusText = $"{_games.Count} of {hit.TotalCount} games";
                    _preloader.Enqueue(hit.Items.Select(g => g.ThumbnailUrl), decodeWidth: 80);
                    EnqueueThumbnailDownloads(hit.Items);
                    _ = PreloadPagesAheadAsync(page, CancellationToken.None);
                    return;
                }
            }

            var languageId = LanguageIdForEnum(SelectedLanguage);
            var result = await _catalog.GetCatalogPageWithSummaryAsync(
                SelectedPlatform, SelectedLanguage, languageId, page, 48, ct);

            _games.AddRange(result.Items);
            _currentPage = page;
            TotalPages = result.TotalPages;
            TotalCount = result.TotalCount;
            StatusText = $"{_games.Count} of {result.TotalCount} games";

            _ = _cache.SavePageAsync(SelectedPlatform, SelectedLanguage, page, 48,
                new CachedPageData(result.Items, result.TotalCount, result.TotalPages),
                CancellationToken.None);
            _preloader.Enqueue(result.Items.Select(g => g.ThumbnailUrl), decodeWidth: 80);
            EnqueueThumbnailDownloads(result.Items);
            _ = PreloadPagesAheadAsync(page, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Network failed — try stale cache as offline fallback
            var stale = await _cache.TryGetPageStaleAsync(
                SelectedPlatform, SelectedLanguage, page, 48, CancellationToken.None);
            if (stale is not null)
            {
                _games.AddRange(stale.Items);
                _currentPage = page;
                TotalPages = stale.TotalPages;
                TotalCount = stale.TotalCount;
                StatusText = $"Offline — {_games.Count} of {stale.TotalCount} games (cached)";
                _preloader.Enqueue(stale.Items.Select(g => g.ThumbnailUrl), decodeWidth: 80);
            }
            else
            {
                StatusText = $"Error: {ex.Message}";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Silently fetches and caches up to 2 pages ahead without touching UI state.
    /// Thumbnails for those pages are enqueued for background image preloading.
    /// </summary>
    private async Task PreloadPagesAheadAsync(int fromPage, CancellationToken ct)
    {
        for (int ahead = 1; ahead <= 2; ahead++)
        {
            var nextPage = fromPage + ahead;
            if (TotalPages > 0 && nextPage > TotalPages) break;

            var hit = await _cache.TryGetPageAsync(SelectedPlatform, SelectedLanguage, nextPage, 48, ct);
            if (hit is not null)
            {
                _preloader.Enqueue(hit.Items.Select(g => g.ThumbnailUrl), decodeWidth: 80);
                continue;
            }

            try
            {
                var languageId = LanguageIdForEnum(SelectedLanguage);
                var result = await _catalog.GetCatalogPageWithSummaryAsync(
                    SelectedPlatform, SelectedLanguage, languageId, nextPage, 48, ct);

                _ = _cache.SavePageAsync(SelectedPlatform, SelectedLanguage, nextPage, 48,
                    new CachedPageData(result.Items, result.TotalCount, result.TotalPages),
                    CancellationToken.None);
                _preloader.Enqueue(result.Items.Select(g => g.ThumbnailUrl), decodeWidth: 80);
            }
            catch { /* preload failures are silent */ }

            // Yield between fetches to be polite to the server
            await Task.Delay(600, ct);
        }
    }

    /// <summary>
    /// Fire-and-forget: downloads all thumbnail images for a page to disk so subsequent
    /// loads are instant (served from <see cref="DiskImageStore"/> instead of HTTP).
    /// </summary>
    private void EnqueueThumbnailDownloads(IEnumerable<CatalogGameSummary> items)
    {
        var list = items.ToList();
        _ = Task.Run(async () =>
        {
            foreach (var game in list)
                if (!string.IsNullOrWhiteSpace(game.ThumbnailUrl))
                    await _diskImageStore.DownloadAsync(
                        game.ThumbnailUrl, game.WrapId, CancellationToken.None)
                        .ConfigureAwait(false);
        });
    }

    private static string LanguageIdForEnum(Language lang) => lang switch
    {
        Language.English => "114",
        Language.German => "117",
        Language.Spanish => "120",
        Language.French => "123",
        Language.Italian => "126",
        Language.Japanese => "129",
        Language.Dutch => "135",
        Language.Swedish => "138",
        Language.Danish => "141",
        Language.Portuguese => "144",
        _ => "114"
    };
}
