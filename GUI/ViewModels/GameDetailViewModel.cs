using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using BFGDL.NET.Models;
using BFGDL.NET.Services;

namespace BFGDL.NET.ViewModels;

public partial class GameDetailViewModel : ReactiveObject
{
    private readonly BigFishCatalogClient _catalog;
    private readonly CatalogCache _cache;
    private readonly ImagePreloader _preloader;
    private readonly IDiskImageStore _diskImageStore;
    private readonly IBigFishGamesClient _bfgClient;
    private readonly DownloadQueueViewModel _downloadQueue;

    [Reactive] private bool _isLoading;
    [Reactive] private bool _isQueueing;
    [Reactive] private string _name = string.Empty;
    [Reactive] private string _heroImageUrl = string.Empty;
    [Reactive] private string _featureImageUrl = string.Empty;
    [Reactive] private string _fullDescriptionHtml = string.Empty;
    [Reactive] private string _shortDescription = string.Empty;
    [Reactive] private IReadOnlyList<string> _screenshotUrls = [];
    [Reactive] private IReadOnlyList<string> _bulletPoints = [];
    [Reactive] private GameSystemRequirements? _systemRequirements;
    [Reactive] private string? _previewVideoUrl;
    [Reactive] private CatalogGameSummary? _currentSummary;
    [Reactive] private CatalogGameDetail? _detail;

    public ReactiveCommand<Unit, Unit> QueueDownloadCommand { get; }

    public GameDetailViewModel(BigFishCatalogClient catalog, CatalogCache cache,
        ImagePreloader preloader, IDiskImageStore diskImageStore,
        IBigFishGamesClient bfgClient, DownloadQueueViewModel downloadQueue)
    {
        _catalog = catalog;
        _cache = cache;
        _preloader = preloader;
        _diskImageStore = diskImageStore;
        _bfgClient = bfgClient;
        _downloadQueue = downloadQueue;

        // Enabled only when a game is loaded and not already queuing
        var canQueue = this.WhenAnyValue(
            x => x.CurrentSummary,
            x => x.IsQueueing,
            (s, q) => s is not null && !q);

        QueueDownloadCommand = ReactiveCommand.CreateFromTask(QueueDownloadAsync, canQueue);
    }

    private async Task QueueDownloadAsync(CancellationToken ct)
    {
        var summary = CurrentSummary;
        if (summary is null) return;

        IsQueueing = true;
        try
        {
            var gameInfo = await _bfgClient.GetGameInfoAsync(summary.WrapId, ct);
            _downloadQueue.EnqueueGame(summary, gameInfo);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Surface the error as a failed queue item so the queue drawer shows it
            System.Diagnostics.Debug.WriteLine($"QueueDownload failed: {ex.Message}");
        }
        finally
        {
            IsQueueing = false;
        }
    }

    public void LoadGame(CatalogGameSummary summary)
    {
        CurrentSummary = summary;
        Name = summary.Name;
        HeroImageUrl = summary.ThumbnailUrl;  // placeholder until detail loads
        ShortDescription = summary.ShortDescription;
        ScreenshotUrls = [];
        BulletPoints = [];
        SystemRequirements = null;
        PreviewVideoUrl = null;
        Detail = null;

        // Kick off detail load (fire and forget — exceptions surface via IsLoading reset)
        LoadDetailAsync(summary).ConfigureAwait(false);
    }

    private async Task LoadDetailAsync(CatalogGameSummary summary)
    {
        IsLoading = true;
        try
        {
            // Serve from disk cache if fresh
            var cached = await _cache.TryGetDetailAsync(summary.WrapId, summary.Language, CancellationToken.None);
            if (cached is not null && CurrentSummary == summary)
            {
                ApplyDetail(cached);
                return;
            }

            var detail = await _catalog.GetProductDetailAsync(
                summary.WrapId, summary.Platform, summary.Language,
                CancellationToken.None);

            if (detail is null || CurrentSummary != summary) return;

            ApplyDetail(detail);

            // Fire-and-forget cache write
            _ = _cache.SaveDetailAsync(detail, CancellationToken.None);
        }
        catch
        {
            // Network failed — try stale cache as offline fallback
            var stale = await _cache.TryGetDetailStaleAsync(
                summary.WrapId, summary.Language, CancellationToken.None);
            if (stale is not null && CurrentSummary == summary)
                ApplyDetail(stale);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyDetail(CatalogGameDetail detail)
    {
        Detail = detail;
        Name = detail.Name;
        HeroImageUrl = !string.IsNullOrWhiteSpace(detail.HeroImageUrl)
            ? detail.HeroImageUrl
            : CurrentSummary?.ThumbnailUrl ?? string.Empty;
        FeatureImageUrl = detail.FeatureImageUrl;
        FullDescriptionHtml = detail.FullDescriptionHtml;
        ScreenshotUrls = detail.ScreenshotUrls;
        BulletPoints = detail.BulletPoints;
        SystemRequirements = detail.SystemRequirements;
        PreviewVideoUrl = detail.PreviewVideoUrl;

        // Preload hero + all screenshots at full resolution so lightbox opens instantly
        var toPreload = detail.ScreenshotUrls.ToList();
        if (!string.IsNullOrWhiteSpace(detail.HeroImageUrl))
            toPreload.Add(detail.HeroImageUrl);
        _preloader.Enqueue(toPreload);

        // Download all images to disk in the background so subsequent loads are instant
        _ = Task.Run(async () =>
        {
            var wrapId = detail.WrapId;
            var urls = new List<string>(toPreload);
            if (!string.IsNullOrWhiteSpace(detail.FeatureImageUrl))
                urls.Add(detail.FeatureImageUrl);
            foreach (var url in urls)
                await _diskImageStore.DownloadAsync(url, wrapId, CancellationToken.None)
                    .ConfigureAwait(false);
        });
    }
}
