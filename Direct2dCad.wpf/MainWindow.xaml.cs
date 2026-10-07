using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.DependencyInjection;
using AvalonDock.Themes;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.wpf.Services.Application;
using Direct2dCad.wpf.Services.Input;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;
using MessagePipe;

namespace Direct2dCad.wpf;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ToolboxLayoutPersistenceService _toolboxLayoutPersistence;
    private readonly CadWindowShortcutRouter _keyboardShortcuts;
    private readonly DispatcherTimer _toolboxLayoutSaveTimer;
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private bool _windowClosed;
    private bool _isExitConfirmationRunning;
    private bool _allowWindowClose;
    private bool _isToolboxLayoutPersistenceActive;

    public MainWindow(MainViewModel viewModel, ISubscriber<ThemeChangedEvent> subscriber, IApplicationThemeService applicationThemeService,
        ToggleDockOptions dockOptions,
        ToolboxLayoutPersistenceService toolboxLayoutPersistence)
    {
        InitializeComponent();
        var dockTheme = new CadDockTheme(applicationThemeService.IsDarkTheme);
        dockTheme.InstallStableStyles(dockManager);
        dockManager.Theme = dockTheme;
        _viewModel = viewModel;
        _toolboxLayoutPersistence = toolboxLayoutPersistence;
        ApplyToolboxShortcuts();
        DataContext = _viewModel;
        _keyboardShortcuts = new(this, ResolveShortcutCommand,
            () => RootDialogHost.IsOpen || _isExitConfirmationRunning, TryCommitFocusedPropertyEdit,
            focused => CadEscapeHandler.Process(this, _viewModel.CurrentEditorTabViewModel?.CadDocumentViewModel, focused),
            focused => CadEnterHandler.Process(this, _viewModel.CurrentEditorTabViewModel?.CadDocumentViewModel, focused));

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
                if (dockManager.Theme is not CadDockTheme { IsDark: true })
                    dockManager.Theme = new CadDockTheme(true);

            }
            else
            {
                if (dockManager.Theme is not CadDockTheme { IsDark: false })
                    dockManager.Theme = new CadDockTheme(false);
            }
        });
        Closing += OnWindowClosing;
        Loaded += (_, _) => OnWindowLoaded(applicationThemeService);
        Deactivated += OnWindowDeactivated;
        Closed += OnWindowClosed;
    }

    private void OnWindowLoaded(IApplicationThemeService applicationThemeService)
    {
        if (dockManager.Theme is not CadDockTheme theme || theme.IsDark != applicationThemeService.IsDarkTheme)
            dockManager.Theme = new CadDockTheme(applicationThemeService.IsDarkTheme);

        _toolboxLayoutPersistence.Restore(
            dockManager,
            _viewModel.LayoutService.Anchorables);
        _isToolboxLayoutPersistenceActive = true;
        _viewModel.RefreshRecoveryEntries();
        // Numeric drawing input lives on the canvas; only recovery entries need this dock at startup.
        _viewModel.DrawingRecovery.IsOpenByDefault = _viewModel.HasRecoveryEntries;
        _viewModel.DrawingRecovery.IsOpen = _viewModel.HasRecoveryEntries;
        _recoveryTimer.Tick += OnRecoveryTick;
        _recoveryTimer.Start();
    }

    private void ApplyToolboxShortcuts()
    {
        foreach (var toolbox in _viewModel.LayoutService.Anchorables.OfType<CadToolboxViewModelBase>())
        {
            var shortcut = CadShortcutCatalog.All.FirstOrDefault(s => s.ToolboxId == toolbox.ContentId);
            if (shortcut is not null)
                toolbox.Shortcut = new KeyGesture(shortcut.Key, shortcut.Modifiers)
                    .GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private ICommand? ResolveShortcutCommand(CadShortcut shortcut)
    {
        if (shortcut.Action == CadShortcutAction.ToggleToolbox)
        {
            // Forward the framework's real toggle command, including auto-hide and
            // floating state, instead of implementing a second toolbox lifecycle.
            var primary = CadShortcutCatalog.All.First(s => s.ToolboxId == shortcut.ToolboxId);
            return InputBindings.OfType<KeyBinding>().FirstOrDefault(b =>
                b.Key == primary.Key && b.Modifiers == primary.Modifiers)?.Command;
        }
        var tab = _viewModel.CurrentEditorTabViewModel;
        return shortcut.Action switch
        {
            CadShortcutAction.New => _viewModel.NewCommand,
            CadShortcutAction.Open => _viewModel.OpenFileCommand,
            CadShortcutAction.Save => tab?.SaveFileCommand,
            CadShortcutAction.SaveAs => tab?.SaveAsFileCommand,
            CadShortcutAction.Print => tab?.PrintCommand,
            _ => null
        };
    }

    internal static bool TryCommitFocusedPropertyEdit(DependencyObject? focusedElement)
    {
        if (focusedElement is not TextBox { IsReadOnly: false } textBox)
            return true;

        var ancestors = new List<DependencyObject>();
        for (DependencyObject? current = textBox; current is not null;)
        {
            ancestors.Add(current);
            if (current is UserControl &&
                current.GetType().Namespace == typeof(EntityHeaderPropertySection).Namespace)
            {
                var expression = textBox.GetBindingExpression(TextBox.TextProperty);
                if (expression is not null &&
                    expression.ParentBinding.Mode is not (BindingMode.OneWay or BindingMode.OneTime))
                {
                    expression.UpdateSource();
                    if (expression.HasError)
                        return false;
                }
                return !ancestors.Any(Validation.GetHasError);
            }
            current = LogicalTreeHelper.GetParent(current) ??
                (current is Visual visual ? VisualTreeHelper.GetParent(visual) : null);
        }

        // Terminal drafts and on-canvas numeric input have their own submission
        // contracts, and are deliberately left untouched by document saving.
        return true;
    }

    private async void OnRecoveryTick(object? sender, EventArgs e)
    {
        _recoveryTimer.Stop();
        try { await _viewModel.CaptureRecoveryCopiesAsync(); }
        finally { if (IsLoaded && !_windowClosed) _recoveryTimer.Start(); }
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
        _windowClosed=true;
        _keyboardShortcuts.Dispose();
        _recoveryTimer.Stop();
        _recoveryTimer.Tick -= OnRecoveryTick;
        _viewModel.OpenOperation.Dispose();
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
