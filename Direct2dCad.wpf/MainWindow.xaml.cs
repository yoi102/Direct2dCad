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
using Direct2dCad.wpf.Services.Application;
using MessagePipe;

namespace Direct2dCad.wpf;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ToolboxLayoutPersistenceService _toolboxLayoutPersistence;
    private bool _isExitConfirmationRunning;
    private bool _allowWindowClose;

    public MainWindow(MainViewModel viewModel, ISubscriber<ThemeChangedEvent> subscriber, IApplicationThemeService applicationThemeService,
        ToggleDockOptions dockOptions,
        ToolboxLayoutPersistenceService toolboxLayoutPersistence)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toolboxLayoutPersistence = toolboxLayoutPersistence;
        DataContext = _viewModel;

        dockManager.ButtonSize = dockOptions.ButtonSize;
        dockManager.DefaultDockWidth = dockOptions.DefaultDockWidth;
        dockManager.DefaultDockHeight = dockOptions.DefaultDockHeight;
        dockManager.ShowHeaderMinimizeButton = dockOptions.ShowHeaderMinimizeButton;
        dockManager.ShowHeaderOptionsButton = dockOptions.ShowHeaderOptionsButton;

        if (Enum.TryParse<DockLayoutPriority>(dockOptions.LayoutPriority, out DockLayoutPriority priority))
        {
            dockManager.LayoutPriority = priority;
        }

        subscriber.Subscribe((h) =>
        {
            if (h.IsDark)
            {
                if (dockManager.Theme is not ArcDarkTheme)
                    dockManager.Theme = new ArcDarkTheme();

            }
            else
            {
                if (dockManager.Theme is not ArcLightTheme)
                    dockManager.Theme = new ArcLightTheme();
            }
        });
        Closing += OnWindowClosing;
        Loaded += (s, e) =>
        {
            if (!applicationThemeService.IsDarkTheme)
            {
                dockManager.Theme = new ArcLightTheme();
            }
        };
    }


    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose)
            return;

        e.Cancel = true;
        if (_isExitConfirmationRunning)
            return;

        _isExitConfirmationRunning = true;
        HandleExitConfirmationAsync();
    }

    private async void HandleExitConfirmationAsync()
    {
        try
        {

            var dialog = Ioc.Default.GetRequiredService<IDialogService>();
            bool confirm = await dialog.ShowExitConfirmation();

            if (!confirm)
                return;


            if (!await _viewModel.ConfirmCloseApplicationAsync())
                return;

            _toolboxLayoutPersistence.Save(
                dockManager,
                _viewModel.LayoutService.Anchorables);

            _allowWindowClose = true;
            Closing -= OnWindowClosing;
            Close();
        }
        finally
        {
            _isExitConfirmationRunning = false;
        }
    }

    private void IconClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/yoi102/Direct2dCad") { UseShellExecute = true });
    }

    private async void dockManager_DocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        if (e.Document.Content is not EditorTabViewModel editorTabViewModel)
            return;

        e.Cancel = true;
        var confirmed = await editorTabViewModel.ConfirmCloseAsync();
        if (!confirmed)
            return;
        dockManager.DocumentClosing -= dockManager_DocumentClosing;
        e.Document.Close();
        dockManager.DocumentClosing += dockManager_DocumentClosing;
    }


}
