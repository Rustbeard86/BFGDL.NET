using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Maximize on the same screen as the owner window using P/Invoke —
        // avoids a WinForms dependency. OnSourceInitialized fires before
        // first render so there is no visible flicker.
        var ownerWindow = Owner;
        int left = 0, top = 0, width = (int)SystemParameters.PrimaryScreenWidth, height = (int)SystemParameters.PrimaryScreenHeight;

        if (ownerWindow is not null)
        {
            var ownerHandle = new WindowInteropHelper(ownerWindow).Handle;
            var hMonitor = NativeMethods.MonitorFromWindow(ownerHandle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                left   = mi.rcMonitor.Left;
                top    = mi.rcMonitor.Top;
                width  = mi.rcMonitor.Right  - mi.rcMonitor.Left;
                height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            }
        }

        // Convert physical pixels to WPF logical units via the DPI scale matrix.
        var src = ownerWindow is not null
            ? PresentationSource.FromVisual(ownerWindow)
            : PresentationSource.FromVisual(this);
        var sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        Left   = left   * sx;
        Top    = top    * sy;
        Width  = width  * sx;
        Height = height * sy;

        WindowState = WindowState.Maximized;
    }

    private static class NativeMethods
    {
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
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
