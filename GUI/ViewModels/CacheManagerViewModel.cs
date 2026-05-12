using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows;
using System.Windows.Threading;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using BFGDL.NET.Models;
using BFGDL.NET.Services;

namespace BFGDL.NET.ViewModels;

// ── Per-language progress row ─────────────────────────────────────────────────

public partial class LanguageFetchStatus : ReactiveObject
{
    public Language Language { get; }

    [Reactive] private int _page;
    [Reactive] private int _totalPages;
    [Reactive] private int _gamesNew;
    [Reactive] private int _gamesSkipped;
    [Reactive] private int _gamesFailed;
    [Reactive] private int _imagesNew;
    [Reactive] private int _imagesOnDisk;
    [Reactive] private int _imagesFailed;
    [Reactive] private bool _isActive;
    [Reactive] private bool _isComplete;

    public LanguageFetchStatus(Language language) => Language = language;

    public void Apply(CatalogFetchProgress p)
    {
        Page         = p.Page;
        TotalPages   = p.TotalPages;
        GamesNew     = p.GamesNew;
        GamesSkipped = p.GamesSkipped;
        GamesFailed  = p.GamesFailed;
        ImagesNew    = p.ImagesNew;
        ImagesOnDisk = p.ImagesOnDisk;
        ImagesFailed = p.ImagesFailed;
        IsComplete   = p.IsComplete;
        IsActive     = !p.IsComplete;
    }
}

// ── Per-language selection toggle ─────────────────────────────────────────────

public partial class LanguageToggle : ReactiveObject
{
    public Language Language { get; }
    [Reactive] private bool _isSelected;

    public LanguageToggle(Language language, bool selected = false)
    {
        Language   = language;
        IsSelected = selected;
    }
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

public partial class CacheManagerViewModel : ReactiveObject
{
    private readonly CatalogFetchService _fetchService;
    private readonly Dispatcher         _dispatcher;

    private CancellationTokenSource? _cts;

    [Reactive] private bool   _isVisible;
    [Reactive] private bool   _isRunning;
    [Reactive] private int    _concurrencyLevel = 4;
    [Reactive] private string _statusText = string.Empty;

    public IReadOnlyList<LanguageToggle> AvailableLanguages { get; }
    public ObservableCollection<LanguageFetchStatus> LanguageStatuses { get; } = [];

    public ReactiveCommand<Unit, Unit> ToggleCommand  { get; }
    public ReactiveCommand<Unit, Unit> StartCommand   { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand  { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand   { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand  { get; }

    public CacheManagerViewModel(CatalogFetchService fetchService)
    {
        _fetchService = fetchService;
        _dispatcher   = Dispatcher.CurrentDispatcher;

        AvailableLanguages = CatalogFetchService.SupportedLanguages
            .Select(l => new LanguageToggle(l, l == Language.English))
            .ToList();

        ToggleCommand = ReactiveCommand.Create(() => { IsVisible = !IsVisible; });

        var canStart = this.WhenAnyValue(
            x => x.IsRunning,
            running => !running);

        StartCommand  = ReactiveCommand.CreateFromTask(StartAsync, canStart);
        var canCancel = this.WhenAnyValue(x => x.IsRunning);
        CancelCommand = ReactiveCommand.Create(() => Cancel(), canCancel);

        SelectAllCommand  = ReactiveCommand.Create(() =>
        {
            foreach (var t in AvailableLanguages) t.IsSelected = true;
        });
        SelectNoneCommand = ReactiveCommand.Create(() =>
        {
            foreach (var t in AvailableLanguages) t.IsSelected = false;
        });
    }

    private async Task StartAsync(CancellationToken ct)
    {
        var selected = AvailableLanguages
            .Where(t => t.IsSelected)
            .Select(t => t.Language)
            .ToList();

        if (selected.Count == 0)
        {
            StatusText = "Select at least one language.";
            return;
        }

        // Reset status rows for selected languages
        LanguageStatuses.Clear();
        foreach (var lang in selected)
            LanguageStatuses.Add(new LanguageFetchStatus(lang));

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning  = true;
        StatusText = "Starting…";

        var progress = new Progress<CatalogFetchProgress>(report =>
        {
            _dispatcher.BeginInvoke(() =>
            {
                var row = LanguageStatuses.FirstOrDefault(r => r.Language == report.Language);
                row?.Apply(report);
                if (!report.IsComplete)
                    StatusText = $"{report.Language}: page {report.Page}/{report.TotalPages} " +
                                 $"— {report.GamesNew + report.GamesSkipped} games processed";
                else
                    StatusText = $"{report.Language} complete " +
                                 $"({report.GamesNew} new, {report.GamesSkipped} skipped)";
            });
        });

        try
        {
            await _fetchService.FetchAllAsync(
                Platform.Windows,
                selected,
                ConcurrencyLevel,
                progress,
                _cts.Token);

            var totalNew = LanguageStatuses.Sum(r => r.GamesNew);
            var totalImg = LanguageStatuses.Sum(r => r.ImagesNew);
            StatusText = $"Done — {totalNew} games, {totalImg} images downloaded.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling…";
    }
}
