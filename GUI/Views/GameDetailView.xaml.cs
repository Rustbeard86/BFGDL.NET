using System.Windows;
using System.Windows.Controls;
using BFGDL.NET.ViewModels;

namespace BFGDL.NET.Views;

public partial class GameDetailView : UserControl
{
    public GameDetailView() => InitializeComponent();

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string url &&
            DataContext is GameDetailViewModel vm && vm.ScreenshotUrls.Count > 0)
        {
            var urls = vm.ScreenshotUrls.ToList();
            var idx = urls.IndexOf(url);
            var lb = new LightboxWindow(urls, idx < 0 ? 0 : idx)
            {
                Owner = Window.GetWindow(this)
            };
            lb.ShowDialog();
        }
    }
}
