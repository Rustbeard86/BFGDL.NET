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

    [Reactive] private bool _isLoading;
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

    public GameDetailViewModel(BigFishCatalogClient catalog, CatalogCache cache)
    {
        _catalog = catalog;
        _cache = cache;
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
            var cached = await _cache.TryGetDetailAsync(summary.WrapId, CancellationToken.None);
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
        catch { /* silently ignore — placeholder values remain */ }
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
    }
}
