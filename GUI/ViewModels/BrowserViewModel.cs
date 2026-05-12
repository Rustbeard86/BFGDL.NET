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

    public BrowserViewModel(BigFishCatalogClient catalog, CatalogCache cache, GameDetailViewModel detail, DownloadQueueViewModel downloadQueue)
    {
        _catalog = catalog;
        _cache = cache;
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
        await LoadPageAsync(1, ct);
    }

    private async Task LoadNextPageAsync(CancellationToken ct)
    {
        await LoadPageAsync(_currentPage + 1, ct);
    }

    private async Task LoadPageAsync(int page, CancellationToken ct)
    {
        IsLoading = true;
        StatusText = $"Loading page {page}…";
        try
        {
            var languageId = LanguageIdForEnum(SelectedLanguage);
            var result = await _catalog.GetCatalogPageWithSummaryAsync(
                SelectedPlatform, SelectedLanguage, languageId, page, 48, ct);

            _games.AddRange(result.Items);
            _currentPage = page;
            TotalPages = result.TotalPages;
            TotalCount = result.TotalCount;
            StatusText = $"{_games.Count} of {result.TotalCount} games";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
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
