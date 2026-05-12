using System.Reactive;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using BFGDL.NET.Models;
using BFGDL.NET.Services;

namespace BFGDL.NET.ViewModels;

public partial class DownloadQueueItemViewModel : ReactiveObject
{
    private readonly GameInfo _gameInfo;
    private readonly IDownloadService _downloadService;
    private readonly Action<DownloadQueueItemViewModel> _removeCallback;
    private CancellationTokenSource? _cts;

    [Reactive] private string _gameName = string.Empty;
    [Reactive] private double _progress;
    [Reactive] private string _segmentsText = string.Empty;
    [Reactive] private string _statusText = "Queued";
    [Reactive] private bool _isComplete;
    [Reactive] private bool _isFailed;

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissCommand { get; }

    public DownloadQueueItemViewModel(
        string gameName,
        GameInfo gameInfo,
        IDownloadService downloadService,
        Action<DownloadQueueItemViewModel> removeCallback)
    {
        _gameName = gameName;
        _gameInfo = gameInfo;
        _downloadService = downloadService;
        _removeCallback = removeCallback;

        var canCancel = this.WhenAnyValue(x => x.IsComplete, x => x.IsFailed, (c, f) => !c && !f);
        CancelCommand = ReactiveCommand.Create(Cancel, canCancel);
        DismissCommand = ReactiveCommand.Create(() => removeCallback(this));
    }

    public void StartDownload()
    {
        _cts = new CancellationTokenSource();
        StatusText = "Starting…";

        var progress = new Progress<DownloadSegmentProgress>(OnProgress);
        _ = Task.Run(() => RunDownloadAsync(progress, _cts.Token));
    }

    private void OnProgress(DownloadSegmentProgress p)
    {
        GameName = p.GameName;
        SegmentsText = $"{p.SegmentsCompleted}/{p.SegmentsTotal} segments";
        Progress = p.SegmentsTotal > 0 ? (double)p.SegmentsCompleted / p.SegmentsTotal : 0;
        StatusText = p.Status switch
        {
            DownloadSegmentStatus.Starting => "Downloading…",
            DownloadSegmentStatus.Completed => p.SegmentsCompleted == p.SegmentsTotal ? "Completing…" : "Downloading…",
            DownloadSegmentStatus.Failed => "Failed",
            _ => StatusText
        };
    }

    private async Task RunDownloadAsync(IProgress<DownloadSegmentProgress> progress, CancellationToken ct)
    {
        try
        {
            await _downloadService.DownloadGameAsync(_gameInfo, progress, ct);
            IsComplete = true;
            Progress = 1.0;
            StatusText = "Complete";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            IsFailed = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            IsFailed = true;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling…";
    }
}
