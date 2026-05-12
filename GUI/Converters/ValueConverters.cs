using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace BFGDL.NET.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a URL string to a BitmapImage. Caches by URL+size so virtualized list
/// scrolling never re-downloads. Does not freeze so async HTTP downloads complete normally.
/// </summary>
[ValueConversion(typeof(string), typeof(BitmapImage))]
public sealed class StringToImageSourceConverter : IValueConverter
{
    // Strong-ref cache: key = "url" or "url@px". Keeps images alive across scroll.
    private static readonly ConcurrentDictionary<string, BitmapImage?> _cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        int decodeWidth = parameter switch
        {
            int i    => i,
            string s => int.TryParse(s, out int p) ? p : 0,
            _        => 0
        };

        string key = decodeWidth > 0 ? $"{url}@{decodeWidth}" : url;
        return _cache.GetOrAdd(key, _ => CreateBitmap(url, decodeWidth));
    }

    private static BitmapImage? CreateBitmap(string url, int decodeWidth)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(url, UriKind.Absolute);
            // Default cache option: WPF downloads async and retains the data.
            // Do NOT use OnLoad + Freeze() for HTTP URIs — Freeze() throws while
            // the download is still in flight, causing the catch to return null.
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
