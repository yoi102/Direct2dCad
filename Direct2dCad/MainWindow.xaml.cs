using System.Diagnostics;
using System.Windows;
using AvalonDock;
using AvalonDock.DependencyInjection;
using AvalonDock.Themes;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Events;
using MessagePipe;

namespace Direct2dCad.wpf;

public partial class MainWindow
{
    private MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, ISubscriber<ThemeChangedEvent> subscriber,
        ToggleDockOptions dockOptions)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        dockManager.ButtonSize = dockOptions.ButtonSize;
        dockManager.DefaultDockWidth = dockOptions.DefaultDockWidth;
        dockManager.DefaultDockHeight = dockOptions.DefaultDockHeight;
        dockManager.ShowHeaderMinimizeButton = dockOptions.ShowHeaderMinimizeButton;
        dockManager.ShowHeaderOptionsButton = dockOptions.ShowHeaderOptionsButton;

        if (Enum.TryParse<DockLayoutPriority>(dockOptions.LayoutPriority, out var priority))
        {
            dockManager.LayoutPriority = priority;
        }

        subscriber.Subscribe((h) =>
        {
            if (h.IsDark)
            {
                dockManager.Theme = new ArcDarkTheme();
            }
            else
            {
                dockManager.Theme = new ArcLightTheme();
            }
        });
    }

    private void IconClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/yoi102/Direct2dCad") { UseShellExecute = true });
    }
}
