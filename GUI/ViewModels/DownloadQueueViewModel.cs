using System.Reactive;
using System.Reactive.Linq;
using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using BFGDL.NET.Models;
using BFGDL.NET.Services;

namespace BFGDL.NET.ViewModels;

public partial class DownloadQueueViewModel : ReactiveObject
{
    private readonly IDownloadService _downloadService;
    private readonly SourceList<DownloadQueueItemViewModel> _items = new();
    private readonly ReadOnlyObservableCollection<DownloadQueueItemViewModel> _visibleItems;

    [Reactive] private bool _isDrawerExpanded;
    [Reactive] private string _summaryText = "Downloads";

    public ReadOnlyObservableCollection<DownloadQueueItemViewModel> Items => _visibleItems;

    public DownloadQueueViewModel(IDownloadService downloadService)
    {
        _downloadService = downloadService;

        _items.Connect()
            .Bind(out _visibleItems)
            .Subscribe();

        _items.CountChanged
            .Select(count => count == 0 ? "Downloads" : $"Downloads ({count})")
            .Subscribe(s => SummaryText = s);
    }

    public void EnqueueGame(CatalogGameSummary summary, GameInfo gameInfo)
    {
        IsDrawerExpanded = true;
        var item = new DownloadQueueItemViewModel(summary.Name, gameInfo, _downloadService, RemoveItem);
        _items.Add(item);
        item.StartDownload();
    }

    private void RemoveItem(DownloadQueueItemViewModel item)
    {
        _items.Remove(item);
    }
}
