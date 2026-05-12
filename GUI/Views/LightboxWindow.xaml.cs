using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using BFGDL.NET.Services;

namespace BFGDL.NET.Views;

public partial class LightboxWindow : Window
{
    private readonly IReadOnlyList<string> _urls;
    private int _index;

    public LightboxWindow(IReadOnlyList<string> urls, int startIndex)
    {
        InitializeComponent();
        _urls = urls;
        _index = Clamp(startIndex);
        UpdateDisplay();
    }

    private int Clamp(int i) => _urls.Count == 0 ? 0 : ((i % _urls.Count) + _urls.Count) % _urls.Count;

    private void UpdateDisplay()
    {
        if (_urls.Count == 0) { Close(); return; }

        var url = _urls[_index];
        // Use shared ImageCache — if the thumbnail was already downloaded for the
        // gallery strip, the full-res load is the same BitmapImage (no second download).
        MainImage.Source = string.IsNullOrWhiteSpace(url) ? null : ImageCache.GetOrCreate(url);

        CounterText.Text = _urls.Count > 1 ? $"{_index + 1} / {_urls.Count}" : string.Empty;
        PrevButton.Visibility = NextButton.Visibility =
            _urls.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPrev() { _index = Clamp(_index - 1); UpdateDisplay(); }
    private void ShowNext() { _index = Clamp(_index + 1); UpdateDisplay(); }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => ShowPrev();
    private void NextButton_Click(object sender, RoutedEventArgs e) => ShowNext();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only close when clicking directly on the backdrop, not on buttons/image
        if (e.Source is Rectangle) Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left:   ShowPrev(); break;
            case Key.Right:  ShowNext(); break;
        }
    }
}
