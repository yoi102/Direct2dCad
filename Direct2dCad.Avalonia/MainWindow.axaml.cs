using System.Collections.Specialized;
using System.ComponentModel;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using AvalonDock.Core;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.Avalonia;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = null!;
    private readonly IToolboxLayoutSettingsStore _settings = null!;
    private readonly IDialogService _dialogs = null!;
    private readonly Dictionary<EditorTabViewModel, TabItem> _tabs = [];
    private readonly Dictionary<IToolbox, TabItem> _toolTabs = [];
    private bool _closing;
    private bool _closed;
    private bool _enterHeld;
    private object? _retainedDocumentTab;
    private bool _documentHostReattaching;
    private int _documentHostGeneration;
    private readonly DispatcherTimer _notificationTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    internal void ShowNotification(string text)
    {
        Notification.Text = text; NotificationToast.IsVisible = !string.IsNullOrWhiteSpace(text);
        _notificationTimer.Stop(); _notificationTimer.Start();
    }
    internal void FocusCanvas() { if (Documents.SelectedItem is TabItem { Content: Views.EditorView editor }) { (TopLevel.GetTopLevel(editor.Canvas) as Window)?.Activate(); editor.Canvas.Focus(); } }
    private CadDocumentViewModel? _statusDocument;
    internal MainViewModel Model => _model;
    internal Task? PendingExitConfirmation { get; private set; }
    internal void CloseForSmoke(int exitCode)
    {
        _closed = true;
        foreach (var tab in _tabs.Values) if (tab.Content is IDisposable disposable) disposable.Dispose();
        _nativeDock?.Dispose();
        foreach (var floating in _floatingTools.Values.ToArray()) floating.Close();
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime) lifetime.Shutdown(exitCode);
    }
    public MainWindow() { ConfigureIntegratedTitleBar(); InitializeComponent(); ExtendIntegratedTitleBar(); }
    internal MainWindow(MainViewModel model, SnackbarService messages, IToolboxLayoutSettingsStore settings, IDialogService dialogs)
    {
        ConfigureIntegratedTitleBar(); InitializeComponent(); ExtendIntegratedTitleBar(); _model = model; _settings = settings; _dialogs = dialogs; DataContext = model;
        if(Services.CanvasInputTrace.Enabled)
        {
            Title="Direct2dCad · "+(Environment.GetEnvironmentVariable("DIRECT2DCAD_CANVAS_PROBE_TITLE")??"右键诊断");
            if(CommandBar.Children.OfType<TextBlock>().FirstOrDefault(text=>text.Text=="Direct2dCad") is {} caption)caption.Text=Title;
        }
        Documents.DetachedFromVisualTree+=(_,_)=> {if(!_documentHostReattaching)_retainedDocumentTab=Documents.SelectedItem;_documentHostReattaching=true;_documentHostGeneration++;};
        Documents.AttachedToVisualTree+=(_,_)=>
        {
            var generation=++_documentHostGeneration;
            Dispatcher.UIThread.Post(()=>
            {
                if(_closed || generation!=_documentHostGeneration || TopLevel.GetTopLevel(Documents) is null)return;
                var selected=_model.CurrentEditorTabViewModel is {} active && _tabs.TryGetValue(active,out var activeTab)?activeTab:_retainedDocumentTab;
                if(selected is not null && Documents.Items.Contains(selected))Documents.SelectedItem=selected;
                _documentHostReattaching=false;ApplyDocumentSelection();
            },DispatcherPriority.Loaded);
        };
        if (model.LayoutService.Documents is INotifyCollectionChanged documents) documents.CollectionChanged += DocumentsChanged;
        if (model.DockLayout is INotifyPropertyChanged root) root.PropertyChanged += LayoutChanged;
        model.PropertyChanged += ModelChanged;
        foreach (var toolbox in model.LayoutService.Anchorables.OfType<IToolbox>())
        {
            var view = toolbox is EntityPropertiesToolboxViewModel properties ? new Views.PropertyInspector(properties) : KnownViews.Create(toolbox);
            var tab = new TabItem { Header = toolbox.Title, Content = view };
            _toolTabs[toolbox] = tab;
            ConfigureDockable(toolbox, tab);
            if (toolbox is INotifyPropertyChanged changed) changed.PropertyChanged += (_, e) => { if (e.PropertyName is "IsOpen" or "Zone") UpdateToolPanels(); };
            var menu = new MenuItem { Header = toolbox.Title, ToggleType = MenuItemToggleType.CheckBox, IsChecked = toolbox.IsOpen, InputGesture = ToolboxGesture(toolbox) };
            menu.Click += (_, _) => { toolbox.IsOpen = !toolbox.IsOpen; menu.IsChecked = toolbox.IsOpen; UpdateToolPanels(); };
            PanelsMenu.Items.Add(menu);
            toolbox.IsOpen = toolbox.IsOpenByDefault;
        }
        InitializeDockChrome();
        UpdateToolPanels();
        ConfigureDockDropTargets();
        RefreshLocalizedPanels();
        _notificationTimer.Tick += (_, _) => { _notificationTimer.Stop(); NotificationToast.IsVisible = false; };
        messages.Message += text => Dispatcher.UIThread.Post(() => ShowNotification(text));
        AddHandler(KeyDownEvent, HandleShortcut, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, ConfirmPanel, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, CancelPanel, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Enter) _enterHeld = false; }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => _enterHeld = false;
        Closing += OnClosing;
        Closed += (_, _) => {RootDialog.CurrentSession?.Close();_nativeDock?.Dispose();};
        
    }
    private void ConfigureIntegratedTitleBar()
    {
        // Suppress the extra Win32 caption painting behind the extended title bar.
        // Keep WS_BORDER, WS_THICKFRAME, WS_SYSMENU and the minimize/maximize styles.
        // Avalonia's caption buttons expose native non-client roles (including HTMAXBUTTON).
        Win32Properties.AddWindowStylesCallback(this, static (style, exStyle) => (style & ~0x00400000u, exStyle)); // WS_DLGFRAME
    }
    private void ExtendIntegratedTitleBar()
    {
        // Install the decoration template before creating extended decoration layers.
        WindowDecorationsTheme = (global::Avalonia.Styling.ControlTheme)Resources["CadMainWindowDecorations"]!;
        ExtendClientAreaToDecorationsHint = true;
    }
    private void DocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in _tabs.Keys.Except(_model.LayoutService.Documents.OfType<EditorTabViewModel>()).ToArray()) { Documents.Items.Remove(_tabs[removed]); if (_tabs[removed].Content is IDisposable disposable) disposable.Dispose(); _tabs.Remove(removed); }
        foreach (var document in _model.LayoutService.Documents.OfType<EditorTabViewModel>())
        {
            if (_tabs.ContainsKey(document)) continue;
            var title = new TextBlock { Text = document.Title, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
            document.PropertyChanged += (_, change) => { if (change.PropertyName == "Title") title.Text = document.Title; };
            var close = new Button { Content = "×", Padding = new global::Avalonia.Thickness(4, 0), Margin = new global::Avalonia.Thickness(8, 0, 0, 0) };
            close.Click += (_, _) => _model.CloseEditorDocumentCommand.Execute(document);
            var header = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal }; header.Children.Add(title); header.Children.Add(close);
            var tab = new TabItem { Header = header, Content = new Views.EditorView(document), Tag = document };
            tab.ContextMenu = CreateDocumentMenu(document);
            _tabs.Add(document, tab); Documents.Items.Add(tab);
        }
        SynchronizeActiveDocument();
    }
    private void LayoutChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == "ActiveDockable") { _model.ActiveDockContent = _model.LayoutService.ActiveDockable; SynchronizeActiveDocument(); } }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(MainViewModel.CurrentEditorTabViewModel)) { SynchronizeActiveDocument(); AttachStatus(); } }
    private void SynchronizeActiveDocument() { if (_model.CurrentEditorTabViewModel is { } active && _tabs.TryGetValue(active, out var tab)) Documents.SelectedItem = tab; }
    private void DocumentSelectionChanged(object? sender, SelectionChangedEventArgs e) {if(e.Source==Documents && _model is not null && !_documentHostReattaching)ApplyDocumentSelection();}
    private void ApplyDocumentSelection() { if (Documents.SelectedItem == WelcomeTab) { _model.ActiveDockContent = WelcomeTab; AttachStatus(); return; } if (Documents.SelectedItem is TabItem { Tag: EditorTabViewModel document }) { _model.LayoutService.ActiveDockable = document; _model.ActiveDockContent = document; AttachStatus(); } }
    private void AttachStatus() { if (_statusDocument != null) _statusDocument.PropertyChanged -= StatusChanged; _statusDocument = _model.CurrentEditorTabViewModel?.CadDocumentViewModel; if (_statusDocument != null) _statusDocument.PropertyChanged += StatusChanged; RefreshStatus(); }
    private void StatusChanged(object? sender, PropertyChangedEventArgs e) => RefreshStatus();
    private void RefreshStatus() { Prompt.Text = _statusDocument?.CurrentToolNameDisplay ?? ""; ToolTip.SetTip(Prompt, _statusDocument?.CurrentStepPrompt); Coordinates.Text = _statusDocument is { } d ? $"X {d.CurrentPointerWorldXDisplay}   Y {d.CurrentPointerWorldYDisplay}   {_model.CurrentEditorTabViewModel?.DocumentUnitSymbol}" : ""; }
    private void UpdateToolPanels()
    {
        if(_nativeDock is not null) _nativeDock.Sync();
        if(_autoShownTool is {} shown && !shown.IsOpen) HideAutoTool();
        LeftPanelBar.Children.Clear(); RightPanelBar.Children.Clear(); BottomPanelBar.Children.Clear(); BottomRightPanelBar.Children.Clear();
        foreach (var (tool, toggle) in _toolToggles)
        {
            toggle.IsChecked = tool.IsOpen;
            var bar = tool.Zone is AvalonDock.Core.DockZone.LeftTop or AvalonDock.Core.DockZone.LeftBottom ? LeftPanelBar : tool.Zone is AvalonDock.Core.DockZone.RightTop or AvalonDock.Core.DockZone.RightBottom ? RightPanelBar : tool.Zone == AvalonDock.Core.DockZone.BottomLeft ? BottomPanelBar : BottomRightPanelBar;
            bar.Children.Add(toggle);
        }
        var menuIndex = 0;
        foreach (var tool in _toolTabs.Keys)
        {
            if (menuIndex < PanelsMenu.Items.Count && PanelsMenu.Items[menuIndex] is MenuItem menu) menu.IsChecked = tool.IsOpen;
            menuIndex++;
        }
        if(_nativeDock is not null) {RefreshDockChrome();return;}
        foreach (var (tool, floating) in _floatingTools) { if (!tool.IsOpen && floating.IsVisible) floating.Hide(); else if (tool.IsOpen && !floating.IsVisible && !_closed) floating.Show(this); }
        var panels = ToolPanels().Select(pair => pair.Panel).ToArray();
        var selections = panels.Select(panel => (Panel: panel, Selected: panel.SelectedItem)).ToArray();
        foreach (var panel in panels) panel.Items.Clear();
        foreach (var (tool, tab) in _toolTabs) if (tool.IsOpen && !_floatingTools.ContainsKey(tool) && !_autoHidden.Contains(tool)) ToolPanel(tool.Zone).Items.Add(tab);
        foreach (var (panel, selected) in selections) if (selected is not null && panel.Items.Contains(selected)) panel.SelectedItem = selected;
        RefreshDockChrome();
        UpdateSplit(LeftDock, LeftTools, LeftBottomTools, true, ref _leftSplit);
        UpdateSplit(RightDock, RightTools, RightBottomTools, true, ref _rightSplit);
        UpdateSplit(BottomDock, BottomLeftTools, BottomTools, false, ref _bottomSplit);
        if (LeftTools.Items.Count + LeftBottomTools.Items.Count == 0) Workspace.ColumnDefinitions[0].Width = new GridLength(0); else if (Workspace.ColumnDefinitions[0].Width.Value < 100) Workspace.ColumnDefinitions[0].Width = new GridLength(280);
        if (RightTools.Items.Count + RightBottomTools.Items.Count == 0) Workspace.ColumnDefinitions[4].Width = new GridLength(0); else if (Workspace.ColumnDefinitions[4].Width.Value < 100) Workspace.ColumnDefinitions[4].Width = new GridLength(300);
        if (BottomTools.Items.Count + BottomLeftTools.Items.Count == 0) Workspace.RowDefinitions[2].Height = new GridLength(0); else if (Workspace.RowDefinitions[2].Height.Value < 100) Workspace.RowDefinitions[2].Height = new GridLength(220);
    }
    private void HandleShortcut(object? sender, KeyEventArgs e)
    {
        if(e.Key==Key.Escape && e.KeyModifiers==KeyModifiers.None && _nativeDock?.CancelDockDrag()==true){e.Handled=true;return;}
        if (e.Handled || RootDialog.IsOpen) return;
        if ((e.Source is not TextBox || e.Source is Controls.CadPropertyNumberBox) &&
            (e.Key == Key.Z && e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift) || e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control))
        {
            var history = e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _model.CurrentEditorTabViewModel?.RedoCommand : _model.CurrentEditorTabViewModel?.UndoCommand;
            if (history?.CanExecute(null) == true) history.Execute(null);
            e.Handled = true; return;
        }
        var toolboxId = e.KeyModifiers == KeyModifiers.Control ? e.Key switch { Key.J or Key.Oem3 => "toolbox.command-line", _ => null } :
            e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) ? e.Key switch { Key.E => "toolbox.documents", Key.L => "toolbox.layers", Key.B => "toolbox.blocks", Key.G => "toolbox.entity-properties", Key.D => "toolbox.drawing-assistant", Key.T => "toolbox.entity-search", Key.F => "toolbox.selection-filter", Key.M => "toolbox.messages", Key.A => "toolbox.ai-assistant", _ => null } : null;
        if (toolboxId is not null && _toolTabs.Keys.OfType<CadToolboxViewModelBase>().FirstOrDefault(t => t.ContentId == toolboxId) is { } toolbox)
        {
            if(_nativeDock?.IsPinned(toolbox)==true || _autoHidden.Contains(toolbox)) { toolbox.IsOpen=true; ShowAutoTool(toolbox); e.Handled=true; return; }
            toolbox.IsOpen = !toolbox.IsOpen; UpdateToolPanels();
            if (toolbox.IsOpen && _toolTabs.TryGetValue(toolbox, out var toolTab))
            {
                if(_nativeDock is not null) _nativeDock.Select(toolbox); else ToolPanel(toolbox.Zone).SelectedItem = toolTab;
                var view = ToolView(toolbox) ?? (_floatingTools.TryGetValue(toolbox, out var floating) ? floating.Tag as Control : null);
                if (toolboxId == "toolbox.command-line") Dispatcher.UIThread.Post(() => view?.FindControl<TextBox>("Input")?.Focus());
                if (toolboxId == "toolbox.ai-assistant") Dispatcher.UIThread.Post(() => view?.FindControl<TextBox>("Prompt")?.Focus());
            }
            e.Handled = true; return;
        }
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            var command = e.Key switch { Key.N => _model.NewCommand, Key.O => _model.OpenFileCommand, Key.S => _model.CurrentEditorTabViewModel?.SaveFileCommand, Key.P => _model.CurrentEditorTabViewModel?.PrintCommand, _ => null };
            if (command is not null)
            {
                e.Handled = true;
                if (command.CanExecute(null) && (e.Key is not (Key.S or Key.P) || CadPanelKeyboard.TryCommitProperty(e.Source as Control))) command.Execute(null);
            }
        }
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S)
        {
            e.Handled = true;
            var saveAs = _model.CurrentEditorTabViewModel?.SaveAsFileCommand;
            if (saveAs?.CanExecute(null) == true && CadPanelKeyboard.TryCommitProperty(e.Source as Control)) saveAs.Execute(null);
        }
        if (e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Tab && Documents.Items.Count > 0) { Documents.SelectedIndex = (Documents.SelectedIndex + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Documents.Items.Count - 1 : 1)) % Documents.Items.Count; e.Handled = true; }
    }
    private static KeyGesture? ToolboxGesture(IToolbox toolbox) => toolbox is CadToolboxViewModelBase model ? model.ContentId switch
    {
        "toolbox.command-line" => new(Key.J, KeyModifiers.Control),
        "toolbox.documents" => new(Key.E, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.layers" => new(Key.L, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.blocks" => new(Key.B, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.entity-properties" => new(Key.G, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.entity-search" => new(Key.T, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.selection-filter" => new(Key.F, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.messages" => new(Key.M, KeyModifiers.Control | KeyModifiers.Shift),
        "toolbox.ai-assistant" => new(Key.A, KeyModifiers.Control | KeyModifiers.Shift),
        _ => null
    } : null;
    private void ConfirmPanel(object? sender, KeyEventArgs e)
    {
        if (RootDialog.IsOpen || e.Handled || e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _model.CurrentEditorTabViewModel?.CadDocumentViewModel.CanEditDocument != true) return;
        var control = e.Source as Control;
        var property = CadPanelKeyboard.IsPropertyEditor(control);
        if (!property && !CadPanelKeyboard.CanConfirm(control)) return;
        e.Handled = true; if (_enterHeld) return; _enterHeld = true;
        if (property && !CadPanelKeyboard.TryCommitProperty(control)) return;
        FocusCanvas();
        if (!property) _model.CurrentEditorTabViewModel?.CadDocumentViewModel.CompleteCurrentDrawing();
    }
    private void CancelPanel(object? sender, KeyEventArgs e)
    {
        if (RootDialog.IsOpen || e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || Documents.SelectedItem is not TabItem { Content: Views.EditorView editor }) return;
        if (CadPanelKeyboard.CancelProperty(e.Source as Control)) FocusCanvas();
        else editor.Cancel();
        e.Handled = true;
    }
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed) return; e.Cancel = true; if (_closing) return; _closing = true;
        PendingExitConfirmation = ConfirmExitAndCloseAsync();
    }
    internal async Task<bool> ConfirmApplicationExitAsync()
    {
        // Match WPF: always confirm exiting first, then resolve unsaved drawings.
        if (!await _dialogs.ShowExitConfirmation()) return false;
        return await _model.ConfirmCloseApplicationAsync();
    }
    private async Task ConfirmExitAndCloseAsync()
    {
        try
        {
            if (!await ConfirmApplicationExitAsync()) return;
            SaveDockWindows();
            _settings.Save(_toolTabs.Keys.OfType<CadToolboxViewModelBase>().Select(t => new KeyValuePair<string, CadToolboxState>(t.ContentId, new() { Zone = t.Zone.ToString(), IsOpen = t.IsOpen })));
            foreach (var tab in _tabs.Values) if (tab.Content is IDisposable disposable) disposable.Dispose();
            _closed = true;
            Close();
        }
        catch (Exception ex)
        {
            AppCrashLog.Write(ex, "Exit confirmation");
            ShowNotification(ex.Message);
        }
        finally { _closing = false; }
    }
}


