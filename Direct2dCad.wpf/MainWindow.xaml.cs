using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _toolboxLayoutSaveTimer;
    private bool _isExitConfirmationRunning;
    private bool _allowWindowClose;
    private bool _isToolboxLayoutPersistenceActive;

    public MainWindow(MainViewModel viewModel, ISubscriber<ThemeChangedEvent> subscriber, IApplicationThemeService applicationThemeService,
        ToggleDockOptions dockOptions,
        ToolboxLayoutPersistenceService toolboxLayoutPersistence)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toolboxLayoutPersistence = toolboxLayoutPersistence;
        DataContext = _viewModel;

        _toolboxLayoutSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _toolboxLayoutSaveTimer.Tick += OnToolboxLayoutSaveTimerTick;
        _viewModel.LayoutService.AnchorableStateChanged += OnAnchorableStateChanged;
        dockManager.LayoutChanged += OnDockLayoutChanged;
        dockManager.ContentDocked += OnDockLayoutChanged;
        dockManager.ContentFloated += OnDockLayoutChanged;

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
        Loaded += (_, _) => OnWindowLoaded(applicationThemeService);
        Deactivated += OnWindowDeactivated;
        Closed += OnWindowClosed;
    }

    private void OnWindowLoaded(IApplicationThemeService applicationThemeService)
    {
        if (!applicationThemeService.IsDarkTheme)
            dockManager.Theme = new ArcLightTheme();

        _toolboxLayoutPersistence.Restore(
            dockManager,
            _viewModel.LayoutService.Anchorables);
        _isToolboxLayoutPersistenceActive = true;
    }

    private void OnAnchorableStateChanged(object? sender, EventArgs e)
        => ScheduleToolboxLayoutSave();

    private void OnToolboxLayoutSaveTimerTick(object? sender, EventArgs e)
    {
        _toolboxLayoutSaveTimer.Stop();
        PersistToolboxLayout();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_isToolboxLayoutPersistenceActive && !_allowWindowClose)
            PersistToolboxLayout();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _isToolboxLayoutPersistenceActive = false;
        _toolboxLayoutSaveTimer.Stop();
        _toolboxLayoutSaveTimer.Tick -= OnToolboxLayoutSaveTimerTick;
        _viewModel.LayoutService.AnchorableStateChanged -= OnAnchorableStateChanged;
        dockManager.LayoutChanged -= OnDockLayoutChanged;
        dockManager.ContentDocked -= OnDockLayoutChanged;
        dockManager.ContentFloated -= OnDockLayoutChanged;
        Deactivated -= OnWindowDeactivated;
        Closed -= OnWindowClosed;
    }

    private void OnDockLayoutChanged(object? sender, EventArgs e) =>
        ScheduleToolboxLayoutSave();

    private void ScheduleToolboxLayoutSave()
    {
        if (!_isToolboxLayoutPersistenceActive || _allowWindowClose)
            return;

        _toolboxLayoutSaveTimer.Stop();
        _toolboxLayoutSaveTimer.Start();
    }

    private void PersistToolboxLayout()
    {
        _toolboxLayoutPersistence.Save(
            dockManager,
            _viewModel.LayoutService.Anchorables);
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

            _toolboxLayoutSaveTimer.Stop();
            PersistToolboxLayout();

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
