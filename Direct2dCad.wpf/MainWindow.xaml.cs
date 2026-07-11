using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using AvalonDock;
using AvalonDock.DependencyInjection;
using AvalonDock.Themes;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
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
        Closing += OnWindowClosing;

    }
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true; // 临时取消关闭

        // 不能直接 await，所以用 async void 包装异步处理
        HandleExitConfirmationAsync(e);
    }

    private async void HandleExitConfirmationAsync(CancelEventArgs e)
    {
        var dialog =Ioc.Default.GetRequiredService<IDialogService>();

        bool confirm = await dialog.ShowExitConfirmation();

        if (confirm)
        {
            // 手动移除关闭事件，避免递归触发
            Closing -= OnWindowClosing;

            Close();  // 程序关闭
        }
    }

    private void IconClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/yoi102/Direct2dCad") { UseShellExecute = true });
    }
}
