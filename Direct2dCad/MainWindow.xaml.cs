using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.ViewModels;

namespace Direct2dCad.wpf;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        cadDocumentView.Dispose();
    }
}
