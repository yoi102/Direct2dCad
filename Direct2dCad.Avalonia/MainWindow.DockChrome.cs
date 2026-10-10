using Avalonia.Controls;
using AvalonDock.Core;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.ViewModels.Toolboxes;
namespace Direct2dCad.Avalonia;
public partial class MainWindow
{
    private NativeDockShell? _nativeDock;
    private readonly HashSet<IToolbox> _autoHidden=[];
    private IToolbox? _autoShownTool;
    private readonly global::Avalonia.Threading.DispatcherTimer _autoHideTimer=new();
    internal NativeDockShell NativeDock=>_nativeDock!;
    internal Control? ToolView(IToolbox tool)=>_nativeDock is not null?_nativeDock.Tools[tool].View:_toolTabs[tool].Content as Control;
    internal bool IsAutoHidden(IToolbox tool)=>_nativeDock!.IsPinned(tool);
    internal bool IsAutoHideVisible=>_nativeDock!.Root.PinnedDock is not null;
    private void InitializeDockChrome()
    {
        var tools=_toolTabs.Select(pair=>(pair.Key,(Control)pair.Value.Content!)).ToArray();
        foreach(var tab in _toolTabs.Values)tab.Content=null;
        ((Grid)Documents.Parent!).Children.Remove(Documents);Documents.DataContext=_model;
        _nativeDock=new NativeDockShell(Documents,tools,ConfigureFloatingShortcuts);
        var reset = new MenuItem { Header=Direct2dCad.Lang.CadUiText.Get("Reset")+" · "+Direct2dCad.Lang.CadUiText.Get("Layout") };
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(reset,"ResetDockLayout");
        reset.Click+=(_,_)=> {_nativeDock.ResetLayout();_autoShownTool=null;_autoHidden.Clear();UpdateToolPanels();SaveDockWindows();};
        PanelsMenu.Items.Add(new Separator());PanelsMenu.Items.Add(reset);
        var saved=SettingsPath.Read("dock-native.json",SettingsJsonContext.Default.NativeDockState);
        if(saved is {Version:>=2})
        {
            try {_nativeDock.Restore(saved);}
            catch(InvalidDataException) { /* Invalid layouts use the default six-zone layout. */ }
        }
        else if(saved is not null && !File.Exists(SettingsPath.Get("dock-native.pre-vs.json")))
            SettingsPath.Write("dock-native.pre-vs.json",saved,SettingsJsonContext.Default.NativeDockState);
        var host=(DockPanel)Workspace.Parent!;host.Children.Remove(Workspace);host.Children.Add(_nativeDock.Control);
        // Native auto-hide tabs are the sole edge affordance. Closed tools are
        // reopened through View or their shortcut, as in an IDE workspace.
        ((Control)LeftPanelBar.Parent!).IsVisible=false;
        ((Control)RightPanelBar.Parent!).IsVisible=false;
        Opened+=(_,_)=>_nativeDock.Root.ShowWindows.Execute(null);
    }
    private void RefreshDockChrome(){if(_nativeDock is not null)foreach(var (tool,model) in _nativeDock.Tools)model.Title=_toolHeaders[tool].Text??tool.Title??model.Id;}
    internal void SetAutoHidden(IToolbox tool,bool hidden){_nativeDock!.Pin(tool,hidden);if(hidden)_autoHidden.Add(tool);else _autoHidden.Remove(tool);}
    internal void ShowAutoTool(IToolbox tool){_autoShownTool=tool;_nativeDock!.Preview(tool);}
    internal void HideAutoTool(){_nativeDock?.HidePreview();_autoShownTool=null;}
    internal void ConfigureFloatingShortcuts(Window window)
    {
        window.AddHandler(KeyDownEvent,HandleShortcut,global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        window.AddHandler(KeyDownEvent,ConfirmPanel,global::Avalonia.Interactivity.RoutingStrategies.Bubble);
        window.AddHandler(KeyDownEvent,CancelPanel,global::Avalonia.Interactivity.RoutingStrategies.Bubble);
        window.AddHandler(KeyUpEvent,(_,e)=>{if(e.Key==global::Avalonia.Input.Key.Enter)_enterHeld=false;},global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        window.Deactivated+=(_,_)=>_enterHeld=false;
    }
}
