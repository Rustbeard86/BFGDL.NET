using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace BFGDL.NET.Services;

/// <summary>
/// Session-level BitmapImage cache shared by the value converter, lightbox, and preloader.
/// All methods that create BitmapImage objects MUST be called from the WPF UI Dispatcher thread
/// because BitmapImage is a DispatcherObject.
/// </summary>
public static class ImageCache
{
    // Keyed by "url" or "url@decodeWidth" — strong reference so images survive scroll.
    private static readonly ConcurrentDictionary<string, BitmapImage?> _store = new();

    /// <summary>
    /// Set by App.OnStartup after the DiskImageStore has been initialised.
    /// When set, images already on disk are loaded from disk rather than the network.
    /// </summary>
    internal static IDiskImageStore? DiskStore { get; set; }

    public static string MakeKey(string url, int decodeWidth = 0) =>
        decodeWidth > 0 ? $"{url}@{decodeWidth}" : url;

    /// <summary>
    /// Return the cached BitmapImage for this URL/width, creating and caching it if needed.
    /// Call from the UI Dispatcher thread.
    /// </summary>
    public static BitmapImage? GetOrCreate(string url, int decodeWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return _store.GetOrAdd(MakeKey(url, decodeWidth), _ => Create(url, decodeWidth));
    }

    /// <summary>Returns true if the URL is already in the cache (any thread safe).</summary>
    public static bool IsCached(string url, int decodeWidth = 0) =>
        _store.ContainsKey(MakeKey(url, decodeWidth));

    private static BitmapImage? Create(string url, int decodeWidth)
    {
        try
        {
            // Prefer local disk file over network — instant load, no HTTP request
            var localPath = DiskStore?.GetLocalPath(url);
            var source = localPath is not null
                ? new Uri(localPath, UriKind.Absolute)
                : new Uri(url, UriKind.Absolute);

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = source;
            // BitmapCacheOption.Default: async download for HTTP sources, does not block EndInit.
            // For local files it resolves synchronously. Do NOT use OnLoad+Freeze() —
            // Freeze() throws before the download finishes for HTTP sources.
            img.CacheOption = BitmapCacheOption.Default;
            if (decodeWidth > 0)
                img.DecodePixelWidth = decodeWidth;
            img.EndInit();
            return img;
        }
        catch
        {
            return null;
        }
    }
}
