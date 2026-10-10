using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AvalonDock.Core;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.Avalonia;

public partial class MainWindow
{
    private static readonly DataFormat<IToolbox> ToolboxFormat = DataFormat.CreateInProcessFormat<IToolbox>("Direct2dCad.Toolbox");
    private readonly Dictionary<IToolbox, Window> _floatingTools = [];
    private readonly Dictionary<IToolbox, TextBlock> _toolHeaders = [];
    private readonly Dictionary<IToolbox, global::Avalonia.Controls.Primitives.ToggleButton> _toolToggles = [];
    private double _leftSplit = .5, _rightSplit = .5, _bottomSplit = .5;
    private IEnumerable<(TabControl Panel, DockZone Zone)> ToolPanels() => new[]
    {
        (LeftTools, DockZone.LeftTop), (LeftBottomTools, DockZone.LeftBottom), (RightTools, DockZone.RightTop),
        (RightBottomTools, DockZone.RightBottom), (BottomLeftTools, DockZone.BottomLeft), (BottomTools, DockZone.BottomRight)
    };
    internal TabControl ToolPanel(DockZone zone) => ToolPanels().Single(pair => pair.Zone == zone).Panel;
    private static void UpdateSplit(Grid grid, TabControl first, TabControl second, bool vertical, ref double ratio)
    {
        var both = first.Items.Count > 0 && second.Items.Count > 0;
        var lengths = vertical ? grid.RowDefinitions.Select(row => row.ActualHeight).ToArray() : grid.ColumnDefinitions.Select(column => column.ActualWidth).ToArray();
        var wasSplit = vertical ? grid.RowDefinitions[1].Height.Value > 0 : grid.ColumnDefinitions[1].Width.Value > 0;
        if (wasSplit && lengths[0] > 0 && lengths[2] > 0) ratio = Math.Clamp(lengths[0] / (lengths[0] + lengths[2]), .15, .85);
        var firstSize = new GridLength(first.Items.Count > 0 ? (both ? ratio : 1) : 0, GridUnitType.Star);
        var secondSize = new GridLength(second.Items.Count > 0 ? (both ? 1 - ratio : 1) : 0, GridUnitType.Star);
        if (vertical) { grid.RowDefinitions[0].Height = firstSize; grid.RowDefinitions[1].Height = new GridLength(both ? 5 : 0); grid.RowDefinitions[2].Height = secondSize; }
        else { grid.ColumnDefinitions[0].Width = firstSize; grid.ColumnDefinitions[1].Width = new GridLength(both ? 5 : 0); grid.ColumnDefinitions[2].Width = secondSize; }
        first.IsVisible = first.Items.Count > 0; second.IsVisible = second.Items.Count > 0;
    }
    private void ConfigureDockable(IToolbox tool, TabItem tab)
    {
        var icon = tool switch { DocumentExplorerToolboxViewModel => "FolderOpenOutline", LayersToolboxViewModel => "Layers", BlocksToolboxViewModel => "VectorPolyline", EntityPropertiesToolboxViewModel => "FormatListBulleted", DrawingRecoveryToolboxViewModel => "History", EntitySearchToolboxViewModel => "Magnify", SelectionFilterToolboxViewModel => "FilterVariant", CommandLineToolboxViewModel => "ConsoleLine", MessageToolboxViewModel => "ChatOutline", _ => "RobotOutline" };
        var toggle = new global::Avalonia.Controls.Primitives.ToggleButton { Content = new Controls.CadIcon { Kind = icon }, Width = 28, Height = 28, Padding = new Thickness(4), Margin = new Thickness(1), IsChecked = tool.IsOpen, Tag = tool };
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(toggle, "PanelToggle." + (tool as CadToolboxViewModelBase)?.ContentId);
        toggle.Click += (_, _) =>
        {
            if(_nativeDock?.IsPinned(tool)==true) { if(ReferenceEquals(tool,_autoShownTool)) HideAutoTool(); else ShowAutoTool(tool); return; }
            tool.IsOpen = !tool.IsOpen;
            UpdateToolPanels();
            if (tool.IsOpen && _nativeDock is not null) _nativeDock.Select(tool); else if(tool.IsOpen && !_floatingTools.ContainsKey(tool)) ToolPanel(tool.Zone).SelectedItem=tab;
        };
        _toolToggles.Add(tool, toggle);
        var header = new TextBlock { Text = tool.Title, Margin = new Thickness(6, 3) }; _toolHeaders[tool] = header;
        var menu = new ContextMenu();
        foreach (var zone in ToolPanels().Select(pair => pair.Zone)) { var item = new MenuItem { Header = DockLabel(zone), Tag = zone }; item.Click += (_, _) => DockTool(tool, zone); menu.Items.Add(item); }
        var floating = new MenuItem { Header = "▣ " + tool.Title, Tag = "float" }; floating.Click += (_, _) => FloatTool(tool); menu.Items.Add(floating);
        var close = new MenuItem { Header = "×" }; close.Click += (_, _) => tool.IsOpen = false; menu.Items.Add(close); toggle.ContextMenu = menu;
        PointerPressedEventArgs? pressed = null; Point origin = default;
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) { pressed = e; origin = e.GetPosition(header); } };
        header.PointerReleased += (_, _) => pressed = null;
        header.PointerMoved += async (_, e) => { if (pressed is null || !e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return; var delta = e.GetPosition(header) - origin; if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 6) return; var start = pressed; pressed = null; var data = new DataTransfer(); var transfer = new DataTransferItem(); transfer.Set(ToolboxFormat, tool); data.Add(transfer); await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move); };
        var actions = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };

        var floatButton = new Button { Content = new Controls.CadIcon { Kind = "DotsVertical", Width = 12, Height = 12 }, Padding = new Thickness(3, 0), Background = global::Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0) };
        floatButton.Click += (_, _) => menu.Open(floatButton);
        var closeButton = new Button { Content = new Controls.CadIcon { Kind = "Minus", Width = 12, Height = 12 }, Padding = new Thickness(3, 0), Background = global::Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0) };
        closeButton.Click += (_, _) => tool.IsOpen = false;
        tab.Header = header;
    }
    private static string DockLabel(DockZone zone)
    {
        string T(string key) => Direct2dCad.Lang.CadUiText.Get(key);
        return zone switch { DockZone.LeftTop => "↖ " + T("Left") + " · " + T("Top"), DockZone.LeftBottom => "↙ " + T("Left") + " · " + T("Bottom"), DockZone.RightTop => "↗ " + T("Right") + " · " + T("Top"), DockZone.RightBottom => "↘ " + T("Right") + " · " + T("Bottom"), DockZone.BottomLeft => "↓← " + T("Bottom") + " · " + T("Left"), _ => "↓→ " + T("Bottom") + " · " + T("Right") };
    }
    private void ConfigureDockDropTargets()
    {
        foreach (var (panel, zone) in ToolPanels())
        {
            DragDrop.SetAllowDrop(panel, true);
            panel.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(ToolboxFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; });
            panel.AddHandler(DragDrop.DropEvent, (_, e) => { if (e.DataTransfer.TryGetValue(ToolboxFormat) is { } tool) DockTool(tool, zone); e.Handled = true; });
        }
        var saved = SettingsPath.Read("dock-windows.json", SettingsJsonContext.Default.DockUiState);
        if(_nativeDock is not null) return;
        if (saved is null) return;
        foreach(var id in saved.AutoHidden) if(_toolTabs.Keys.OfType<CadToolboxViewModelBase>().FirstOrDefault(t=>t.ContentId==id) is {} tool) _autoHidden.Add(tool);
        _leftSplit = Math.Clamp(saved.LeftTopRatio, .15, .85); _rightSplit = Math.Clamp(saved.RightTopRatio, .15, .85); _bottomSplit = Math.Clamp(saved.BottomLeftRatio, .15, .85);
        // Apply saved proportions before the first native layout pass.
        LeftDock.RowDefinitions[0].Height = new GridLength(_leftSplit, GridUnitType.Star); LeftDock.RowDefinitions[2].Height = new GridLength(1 - _leftSplit, GridUnitType.Star);
        RightDock.RowDefinitions[0].Height = new GridLength(_rightSplit, GridUnitType.Star); RightDock.RowDefinitions[2].Height = new GridLength(1 - _rightSplit, GridUnitType.Star);
        BottomDock.ColumnDefinitions[0].Width = new GridLength(_bottomSplit, GridUnitType.Star); BottomDock.ColumnDefinitions[2].Width = new GridLength(1 - _bottomSplit, GridUnitType.Star);
        UpdateToolPanels();
        if (saved.LeftWidth is >= 150 and <= 700) Workspace.ColumnDefinitions[0].Width = new GridLength(saved.LeftWidth);
        if (saved.RightWidth is >= 150 and <= 700) Workspace.ColumnDefinitions[4].Width = new GridLength(saved.RightWidth);
        if (saved.BottomHeight is >= 100 and <= 600) Workspace.RowDefinitions[2].Height = new GridLength(saved.BottomHeight);
        Opened += (_, _) => { foreach (var tool in _toolTabs.Keys.OfType<CadToolboxViewModelBase>()) if (tool.IsOpen && saved.Floating.TryGetValue(tool.ContentId, out var state)) { FloatTool(tool); var window = _floatingTools[tool]; window.Width = Math.Clamp(state.Width, 250, 900); window.Height = Math.Clamp(state.Height, 200, 900); } };
    }
    internal void DockTool(IToolbox tool, DockZone zone)
    {
        if(_nativeDock is not null) { _nativeDock.Dock(tool,zone); return; }
        HideAutoTool(); _autoHidden.Remove(tool);
        if (_floatingTools.Remove(tool, out var window))
        {
            var content = window.Tag as Control;
            if (window.Tag is Control && content?.Parent is DockPanel container) container.Children.Remove(content);
            window.Content = null; window.Close(); _toolTabs[tool].Content = content;
        }
        tool.Zone = zone; tool.IsOpen = true; UpdateToolPanels();
    }
    internal void FloatTool(IToolbox tool)
    {
        if(_nativeDock is not null) { _nativeDock.Float(tool); return; }
        HideAutoTool(); _autoHidden.Remove(tool);
        if (_floatingTools.TryGetValue(tool, out var existing)) { existing.Activate(); return; }
        var tab = _toolTabs[tool]; var content = tab.Content as Control; tab.Content = null;
        var dock = new Button { Content = "↙", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
        dock.Click += (_, _) => DockTool(tool, tool.Zone);
        var container = new DockPanel(); DockPanel.SetDock(dock, global::Avalonia.Controls.Dock.Top); container.Children.Add(dock); if (content is not null) container.Children.Add(content);
        var window = new Window { Title = _toolHeaders.TryGetValue(tool, out var localizedHeader) ? localizedHeader.Text : tool.Title ?? "Direct2dCad", Width = 380, Height = 550, Content = container, Tag = content };
        Controls.CadWindowChrome.ApplyFloatingDock(window);
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"), Height = 28 };
        header.Children.Add(new TextBlock { Text = window.Title, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
        container.Children.Remove(dock); Grid.SetColumn(dock, 1); header.Children.Add(dock);
        var close = new Button { Content = "×", Padding = new global::Avalonia.Thickness(6, 0), MinHeight = 24 };
        close.Click += (_, _) => window.Close(); Grid.SetColumn(close, 2); header.Children.Add(close);
        header.PointerPressed += (_, e) => { if (e.Source is not Button && e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) window.BeginMoveDrag(e); };
        DockPanel.SetDock(header, global::Avalonia.Controls.Dock.Top); container.Children.Insert(0, header);
        _floatingTools[tool] = window; UpdateToolPanels();
        window.Closed += (_, _) => { if (!_floatingTools.Remove(tool)) return; if (content is not null) container.Children.Remove(content); tab.Content = content; tool.IsOpen = false; UpdateToolPanels(); };
        window.Show(this);
    }
    internal void RefreshLocalizedPanels()
    {
        foreach (var (tool, header) in _toolHeaders)
        {
            var key = tool switch { DocumentExplorerToolboxViewModel => "Documents", LayersToolboxViewModel => "Layers", BlocksToolboxViewModel => "Blocks", EntityPropertiesToolboxViewModel => "Property", DrawingRecoveryToolboxViewModel => "DrawingRecovery", EntitySearchToolboxViewModel => "EntitySearch", SelectionFilterToolboxViewModel => "SelectionFilter", CommandLineToolboxViewModel => "Terminal", MessageToolboxViewModel => "Messages", _ => "AiAssistant" };
            header.Text = Direct2dCad.Lang.CadUiText.Get(key);
            if (_toolToggles.TryGetValue(tool, out var toggle)) { ToolTip.SetTip(toggle, ToolboxGesture(tool) is { } gesture ? $"{header.Text} ({gesture})" : header.Text); global::Avalonia.Automation.AutomationProperties.SetName(toggle, header.Text); }
            foreach (var dockItem in _toolToggles[tool].ContextMenu!.Items.OfType<MenuItem>())
                if (dockItem.Tag is DockZone zone) dockItem.Header = DockLabel(zone); else if (dockItem.Tag is "float") dockItem.Header = "▣ " + header.Text;
            if (_floatingTools.TryGetValue(tool, out var floating)) floating.Title = header.Text;
            var index = _toolTabs.Keys.ToList().IndexOf(tool);
            if (index >= 0 && PanelsMenu.Items[index] is MenuItem item) item.Header = header.Text;
        }
        RefreshDockChrome();
        RefreshStatus();
        foreach(var item in PanelsMenu.Items.OfType<MenuItem>())
            if(global::Avalonia.Automation.AutomationProperties.GetAutomationId(item)=="ResetDockLayout")item.Header=Direct2dCad.Lang.CadUiText.Get("Reset")+" · "+Direct2dCad.Lang.CadUiText.Get("Layout");
    }
    internal DockUiState CaptureDockState()
    {
        var state = new DockUiState { LeftWidth = Workspace.ColumnDefinitions[0].ActualWidth, RightWidth = Workspace.ColumnDefinitions[4].ActualWidth, BottomHeight = Workspace.RowDefinitions[2].ActualHeight };
        state.LeftTopRatio = SplitRatio(LeftDock.RowDefinitions[0].ActualHeight, LeftDock.RowDefinitions[2].ActualHeight, _leftSplit);
        state.RightTopRatio = SplitRatio(RightDock.RowDefinitions[0].ActualHeight, RightDock.RowDefinitions[2].ActualHeight, _rightSplit);
        state.BottomLeftRatio = SplitRatio(BottomDock.ColumnDefinitions[0].ActualWidth, BottomDock.ColumnDefinitions[2].ActualWidth, _bottomSplit);
        state.AutoHidden = _autoHidden.OfType<CadToolboxViewModelBase>().Select(t=>t.ContentId).ToArray();
        foreach (var (tool, window) in _floatingTools) if (tool is CadToolboxViewModelBase cad) state.Floating[cad.ContentId] = new() { Width = window.Width, Height = window.Height };
        return state;
    }
    private static double SplitRatio(double first, double second, double fallback) => first > 0 && second > 0 ? Math.Clamp(first / (first + second), .15, .85) : fallback;
    private void SaveDockWindows()
    {
        if(_nativeDock is not null) {SettingsPath.Write("dock-native.json",_nativeDock.Capture(),SettingsJsonContext.Default.NativeDockState);return;}
        SettingsPath.Write("dock-windows.json", CaptureDockState(), SettingsJsonContext.Default.DockUiState);
    }
}
