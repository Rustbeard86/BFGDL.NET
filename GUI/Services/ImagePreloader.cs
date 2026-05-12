using System.Windows.Threading;
using System.Windows;

namespace BFGDL.NET.Services;

/// <summary>
/// Schedules speculative BitmapImage preloads on the UI Dispatcher at
/// <see cref="DispatcherPriority.Background"/> so visible rendering is never starved.
///
/// Usage pattern:
///   _preloader.Enqueue(pageItems.Select(g => g.ThumbnailUrl), decodeWidth: 80);
/// The enqueue returns immediately; the actual BitmapImage creation is spread across
/// idle dispatcher cycles a few URLs at a time.
/// </summary>
public sealed class ImagePreloader
{
    private readonly Dispatcher _dispatcher;
    // Number of images created per dispatcher operation — keeps each slice under ~1 ms.
    private const int BatchSize = 5;

    public ImagePreloader()
    {
        _dispatcher = Application.Current.Dispatcher;
    }

    /// <summary>
    /// Enqueue <paramref name="urls"/> for background preloading at the given decode width.
    /// Already-cached URLs are skipped immediately (cheap ConcurrentDictionary lookup).
    /// </summary>
    public void Enqueue(IEnumerable<string> urls, int decodeWidth = 0)
    {
        // Filter out blank and already-cached URLs before dispatching anything.
        var pending = urls
            .Where(u => !string.IsNullOrWhiteSpace(u) && !ImageCache.IsCached(u, decodeWidth))
            .Distinct()
            .ToList();

        if (pending.Count == 0) return;

        for (int i = 0; i < pending.Count; i += BatchSize)
        {
            var batch = pending.Skip(i).Take(BatchSize).ToList();
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                foreach (var url in batch)
                    ImageCache.GetOrCreate(url, decodeWidth);
            });
        }
    }
}
