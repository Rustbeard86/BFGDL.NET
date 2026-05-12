using System.Windows;
using BFGDL.NET.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BFGDL.NET;

public partial class MainWindow : Window
{
    public MainWindow(BrowserViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
