using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Direct2dCad.ViewModels.Toolboxes;
using Zone = AvalonDock.Core.DockZone;
using Toolbox = AvalonDock.Core.IToolbox;

namespace Direct2dCad.Avalonia.Services;

internal sealed class CadDockTool : Tool
{
    public Toolbox Model { get; }
    public Control View { get; }
    public CadDockTool(Toolbox model,Control view) {Model=model;View=view;Id=((CadToolboxViewModelBase)model).ContentId;Title=model.Title??Id;CanClose=true;CanPin=true;}
}
internal sealed class CadDockDocument : Document
{
    public Control View { get; }
    public CadDockDocument(Control view) {View=view;Id="cad.documents";Title=Direct2dCad.Lang.CadUiText.Get("Documents");CanClose=false;CanDrag=false;CanPin=false;}
}
internal static class CadDockPolicy
{
    internal static void Apply(global::Dock.Model.Mvvm.Core.DockableBase target,double width,double height,bool document)
    {
        // Dock validates Fill before creating its local compass, even when the
        // pointer will choose a split. Keep native tab docking available on CAD
        // too; the original CAD document remains an accessible, nonclosable tab.
        var mask=DockOperationMask.Window | DockOperationMask.Fill;
        if(double.IsFinite(width) && width>=2*(document?320:240)+4)mask|=DockOperationMask.Left|DockOperationMask.Right;
        if(double.IsFinite(height) && height>=2*(document?240:140)+4)mask|=DockOperationMask.Top|DockOperationMask.Bottom;
        target.AllowedDropOperations=mask;
    }
}
internal sealed class CadToolDock : ToolDock
{
    public CadToolDock(){MinWidth=240;MinHeight=140;AllowedDropOperations=DockOperationMask.Fill|DockOperationMask.Window;EnableGlobalDocking=false;}
    internal void UpdateDropTargets(double width,double height)
    {
        CadDockPolicy.Apply(this,width,height,false);
        foreach(var tool in (VisibleDockables??[]).OfType<CadDockTool>())CadDockPolicy.Apply(tool,width,height,false);
    }
}
internal sealed class CadDocumentDock : DocumentDock
{
    public CadDocumentDock(){MinWidth=320;MinHeight=240;AllowedDropOperations=DockOperationMask.Fill|DockOperationMask.Window;EnableGlobalDocking=false;}
    internal void UpdateDropTargets(double width,double height)=>CadDockPolicy.Apply(this,width,height,true);
}
internal sealed class CadDockFactory : Factory
{
    public Action? LayoutChanged { get; set; }
    public Func<CadDockTool,IDock?,IDock>? ResolveReturnOwner { get; set; }
    public Func<CadDockTool,bool>? IsFloatingTool { get; set; }
    public Func<IDock,bool>? IsWorkspaceDock { get; set; }
    public override IToolDock CreateToolDock() => new CadToolDock();
    public override IDocumentDock CreateDocumentDock() => new CadDocumentDock();
    public override IRootDock CreateRootDock() => new RootDock {EnableGlobalDocking=false};
    public override void SplitToWindow(IDock dock,IDockable dockable,double x,double y,double width,double height,DockWindowOptions? options)
    {
        var origins=NativeDockShell.Find(dockable).OfType<CadDockTool>().Select(tool=>(Tool:tool,Owner:(tool.OriginalOwner??tool.Owner) as IDock)).ToArray();
        base.SplitToWindow(dock,dockable,x,y,width,height,options);
        foreach(var (tool,owner) in origins)
            if(IsFloatingTool?.Invoke(tool)==true)tool.OriginalOwner=ResolveReturnOwner?.Invoke(tool,owner)??owner;
        LayoutChanged?.Invoke();
    }
    public override void PinDockable(IDockable dockable)
    {
        // The native Dock menu is bound to PinDockable. For a floating tool it
        // should return to its previous pane, rather than auto-hide in the float.
        if(dockable is CadDockTool tool && IsFloatingTool?.Invoke(tool)==true && !IsDockablePinned(tool) && tool.Owner is IDock source && tool.OriginalOwner is IDock destination)
        {
            destination=ResolveReturnOwner?.Invoke(tool,destination)??destination;
            MoveDockable(source,destination,tool,null);tool.OriginalOwner=null;
            SetActiveDockable(tool);LayoutChanged?.Invoke();return;
        }
        base.PinDockable(dockable);
    }
    public override void SplitToDock(IDock dock,IDockable dockable,DockOperation operation)
    {
        base.SplitToDock(dock,dockable,operation);
        LayoutChanged?.Invoke();
    }
    public override void CollapseDock(IDock dock)
    {
        // Keep stable destinations addressable while the native panel hides empty slots.
        if (IsWorkspaceDock?.Invoke(dock)==true && (dock.Id.StartsWith("zone.") || dock.Id.StartsWith("pair.") || dock.Id is "middle" or "workspace")) return;
        base.CollapseDock(dock);
    }
}
internal sealed class NativeDockShell : IDisposable
{
    public Factory Factory { get; } = new CadDockFactory {HideToolsOnClose=true};
    public RootDock Root { get; private set; }
    public DockControl Control { get; }
    public Dictionary<Toolbox,CadDockTool> Tools { get; } = [];
    public Dictionary<Zone,ToolDock> Groups { get; } = [];
    private bool _sync;
    private bool _disposed;
    private bool _layoutUpdatePending;
    private IPointer? _dockPointer;
    private DockControl? _dragControl;
    private readonly Dictionary<Control,Border> _hosts = [];
    private readonly List<IDataTemplate> _globalTemplates = [];
    private NativeDockState _defaultLayout = null!;
    public NativeDockShell(Control documents,IEnumerable<(Toolbox Model,Control View)> tools,Action<Window>? prepareFloatingWindow=null,bool initializeDefaults=true)
    {
        foreach(var (model,view) in tools) Tools.Add(model,new(model,view));
        HostWindow CreateHost()
        {
            var host=new HostWindow {ToolChromeControlsWholeWindow=true,DocumentChromeControlsWholeWindow=true};
            Controls.CadWindowChrome.ApplyFloatingDock(host);
            AttachDragHandlers(host);prepareFloatingWindow?.Invoke(host);return host;
        }
        Factory.HostWindowLocator=new Dictionary<string,Func<IHostWindow?>> { [nameof(IDockWindow)]=CreateHost };
        Factory.DefaultHostWindowLocator=CreateHost;
        Factory.ContextLocator=new Dictionary<string,Func<object?>>();
        foreach(var zone in Enum.GetValues<Zone>()) Groups[zone]=new CadToolDock {Id="zone."+zone,CanCloseLastDockable=true,Alignment=zone is Zone.LeftTop or Zone.LeftBottom?Alignment.Left:zone is Zone.RightTop or Zone.RightBottom?Alignment.Right:Alignment.Bottom,IsCollapsable=true,MinWidth=240,MinHeight=140,VisibleDockables=Factory.CreateList<IDockable>()};
        // Reset always restores this compact workspace, independently of old
        // WPF preferences and previous drag sessions.
        if(initializeDefaults)foreach(var tool in Tools.Values)
        {
            tool.Model.Zone=tool.Model switch
            {
                EntityPropertiesToolboxViewModel=>Zone.RightTop,
                CommandLineToolboxViewModel or MessageToolboxViewModel=>Zone.BottomRight,
                EntitySearchToolboxViewModel or SelectionFilterToolboxViewModel or AiAssistantToolboxViewModel=>Zone.RightBottom,
                _=>Zone.LeftTop
            };
            tool.Model.IsOpen=tool.Model is DocumentExplorerToolboxViewModel or LayersToolboxViewModel or EntityPropertiesToolboxViewModel or CommandLineToolboxViewModel;
        }
        foreach(var tool in Tools.Values) if(tool.Model.IsOpen) Groups[tool.Model.Zone].VisibleDockables!.Add(tool);
        foreach(var group in Groups.Values) group.ActiveDockable=group.VisibleDockables!.FirstOrDefault();
        var center=new CadDockDocument(documents);
        var documentDock=new CadDocumentDock {Id="documents",IsCollapsable=false,CanCreateDocument=false,VisibleDockables=Factory.CreateList<IDockable>(center),ActiveDockable=center};
        ProportionalDock Pair(Zone first,Zone second,Orientation orientation,double proportion)=>new() {Id="pair."+first,Orientation=orientation,Proportion=proportion,VisibleDockables=Factory.CreateList<IDockable>(Groups[first],new ProportionalDockSplitter(),Groups[second])};
        Groups[Zone.LeftTop].Proportion=.34;Groups[Zone.LeftBottom].Proportion=.66;
        Groups[Zone.RightTop].Proportion=.6;Groups[Zone.RightBottom].Proportion=.4;
        var left=Pair(Zone.LeftTop,Zone.LeftBottom,Orientation.Vertical,.22);var right=Pair(Zone.RightTop,Zone.RightBottom,Orientation.Vertical,.22);var bottom=Pair(Zone.BottomLeft,Zone.BottomRight,Orientation.Horizontal,.22);
        documentDock.Proportion=.56;
        var middle=new ProportionalDock {Id="middle",Proportion=.78,Orientation=Orientation.Horizontal,VisibleDockables=Factory.CreateList<IDockable>(left,new ProportionalDockSplitter(),documentDock,new ProportionalDockSplitter(),right)};
        var layout=new ProportionalDock {Id="workspace",Orientation=Orientation.Vertical,VisibleDockables=Factory.CreateList<IDockable>(middle,new ProportionalDockSplitter(),bottom)};
        Root=new RootDock {EnableGlobalDocking=false,Id="cad.root",IsCollapsable=false,VisibleDockables=Factory.CreateList<IDockable>(layout),ActiveDockable=layout,DefaultDockable=layout,HiddenDockables=Factory.CreateList<IDockable>(),LeftPinnedDockables=Factory.CreateList<IDockable>(),RightPinnedDockables=Factory.CreateList<IDockable>(),TopPinnedDockables=Factory.CreateList<IDockable>(),BottomPinnedDockables=Factory.CreateList<IDockable>()};
        Control=new DockControl {Layout=Root,InitializeLayout=false,InitializeFactory=false};
        Control.SizeChanged+=(_,_)=>QueueLayoutUpdate();
        Control.LayoutUpdated+=RefreshDropTargets;
        AttachDragHandlers(Control);
        foreach(var tool in Tools.Values)tool.View.SizeChanged+=ViewSizeChanged;
        Control.DataTemplates.Add(new FuncDataTemplate<CadDockTool>((model,_)=>HostView(model?.View),false));Control.DataTemplates.Add(new FuncDataTemplate<CadDockDocument>((model,_)=>HostView(model?.View),false));
        // Floating hosts use the same application-level typed templates.
        _globalTemplates.Add(new FuncDataTemplate<CadDockTool>((model,_)=>HostView(model?.View),false));_globalTemplates.Add(new FuncDataTemplate<CadDockDocument>((model,_)=>HostView(model?.View),false));
        foreach(var template in _globalTemplates)global::Avalonia.Application.Current!.DataTemplates.Add(template);
        Factory.DockableClosed+=(_,e)=> {if(!_sync && e.Dockable is CadDockTool tool)tool.Model.IsOpen=false;};
        Factory.DockableMoved+=(_,_)=>QueueLayoutUpdate();
        Factory.DockableDocked+=(_,e)=>
        {
            foreach(var tool in (e.Dockable is null?[]:Find(e.Dockable)).OfType<CadDockTool>())
                if(!IsFloating(tool) && !IsPinned(tool.Model))tool.OriginalOwner=null;
            QueueLayoutUpdate();
        };
        ((CadDockFactory)Factory).LayoutChanged=QueueLayoutUpdate;
        ((CadDockFactory)Factory).ResolveReturnOwner=(tool,owner)=>owner is not null && Find(Root).Contains(owner)?owner:EnsureGroup(tool.Model.Zone);
        ((CadDockFactory)Factory).IsFloatingTool=IsFloating;
        ((CadDockFactory)Factory).IsWorkspaceDock=dock=>Find(Root).Contains(dock);
        Factory.InitLayout(Root);
        UpdateMinimums();
        _defaultLayout=Capture();
    }
    private void QueueLayoutUpdate()
    {
        if(_disposed || _layoutUpdatePending)return;
        _layoutUpdatePending=true;
        Dispatcher.UIThread.Post(()=>
        {
            _layoutUpdatePending=false;
            if(_disposed)return;
            foreach(var tool in Tools.Values)tool.Model.Zone=ResolveZone(tool);
            UpdateMinimums();
            RefreshDropTargets(null,EventArgs.Empty);
        },DispatcherPriority.Normal);
    }
    private void ViewSizeChanged(object? sender,SizeChangedEventArgs e)=>QueueLayoutUpdate();
    private void AttachDragHandlers(InputElement surface)
    {
        ToolChromeControl? pendingChrome=null;
        ToolTabStripItem? pendingTab=null;
        IPointer? contextPointer=null;
        Point contextStart=default;
        surface.AddHandler(global::Avalonia.Controls.Control.ContextRequestedEvent,(_,e)=>
        {
            if(e.Handled || e.Source is not Visual source)return;
            var ancestors=source.GetVisualAncestors().Prepend(source).ToArray();
            ToolChromeControl? chrome=null;
            if((ancestors.OfType<ToolTabStripItem>().FirstOrDefault()??FindToolTab(surface,e)) is {DataContext: CadDockTool tool})
            {
                Factory.SetActiveDockable(tool);
                chrome=surface.GetVisualDescendants().OfType<ToolChromeControl>().FirstOrDefault(control=>ReferenceEquals(control.DataContext,tool.Owner));
            }
            // ToolChrome wraps the entire pane, including business content. Only
            // its actual grip/title rectangle belongs to the docking menu.
            chrome??=FindTitleChrome(surface, e);
            if(chrome?.ToolFlyout is MenuFlyout menu) {menu.ShowAt(chrome,true);e.Handled=true;}
        },RoutingStrategies.Bubble);
        surface.AddHandler(InputElement.PointerPressedEvent,(_,e)=>
        {
            if(e.GetCurrentPoint(surface).Properties.PointerUpdateKind==PointerUpdateKind.RightButtonPressed)
            {
                // Dock's draggable header consumes right presses too. Reserve
                // this gesture for the title/tab context menu, not a dock drag.
                contextPointer=null;pendingChrome=FindTitleChrome(surface,e);pendingTab=FindToolTab(surface,e);
                if(pendingChrome is not null || pendingTab is not null)
                {
                    contextPointer=e.Pointer;contextStart=e.GetPosition(surface);e.Handled=true;
                }
                return;
            }
            _dockPointer=e.Pointer;
            _dragControl=(e.Source as Visual)?.GetVisualAncestors().OfType<DockControl>().FirstOrDefault()??Control;
            PrepareDockTargets();
        },RoutingStrategies.Tunnel,true);
        surface.AddHandler(InputElement.PointerMovedEvent,(_,e)=>
        {
            var delta=e.GetPosition(surface)-contextStart;
            if(contextPointer is not null && delta.X*delta.X+delta.Y*delta.Y>16){pendingChrome=null;pendingTab=null;contextPointer=null;}
            if(_dragControl?.IsDraggingDock==true)PrepareDockTargets();
        },RoutingStrategies.Tunnel,true);
        surface.AddHandler(InputElement.PointerReleasedEvent,(_,e)=>
        {
            if(e.InitialPressMouseButton!=MouseButton.Right || !ReferenceEquals(e.Pointer,contextPointer))return;
            var chrome=pendingChrome;var tab=pendingTab;
            pendingChrome=null;pendingTab=null;contextPointer=null;
            if(tab is not null && ReferenceEquals(FindToolTab(surface,e),tab) && tab.DataContext is CadDockTool tool)
            {
                Factory.SetActiveDockable(tool);
                chrome=surface.GetVisualDescendants().OfType<ToolChromeControl>().FirstOrDefault(control=>ReferenceEquals(control.DataContext,tool.Owner));
            }
            else if(chrome is null || !ReferenceEquals(FindTitleChrome(surface,e),chrome))return;
            if(chrome?.ToolFlyout is MenuFlyout menu){menu.ShowAt(chrome,true);e.Handled=true;}
        },RoutingStrategies.Tunnel,true);
    }
    private static ToolChromeControl? FindTitleChrome(InputElement surface, RoutedEventArgs e)
    {
        foreach(var chrome in surface.GetVisualDescendants().OfType<ToolChromeControl>().Where(control=>control.IsEffectivelyVisible))
        {
            var grip=chrome.GetVisualDescendants().OfType<Control>().FirstOrDefault(control=>control.Name=="PART_Grip");
            if(grip is null)continue;
            if(e is PointerEventArgs pointer && new Rect(grip.Bounds.Size).Contains(pointer.GetPosition(grip)))return chrome;
            if(e is ContextRequestedEventArgs context && context.TryGetPosition(grip,out var position) && new Rect(grip.Bounds.Size).Contains(position))return chrome;
        }
        return null;
    }
    private static ToolTabStripItem? FindToolTab(InputElement surface,RoutedEventArgs e)=>surface.GetVisualDescendants().OfType<ToolTabStripItem>().FirstOrDefault(tab=>
        tab.IsEffectivelyVisible && (e is PointerEventArgs pointer && new Rect(tab.Bounds.Size).Contains(pointer.GetPosition(tab)) ||
            e is ContextRequestedEventArgs context && context.TryGetPosition(tab,out var position) && new Rect(tab.Bounds.Size).Contains(position)));
    internal void PrepareDockTargets()=>RefreshDropTargets(null,EventArgs.Empty);
    internal bool CancelDockDrag()
    {
        if(_dragControl?.IsDraggingDock!=true || _dockPointer is null)return false;
        _dockPointer.Capture(null);
        return true;
    }
    private void RefreshDropTargets(object? sender,EventArgs e)
    {
        if(_disposed)return;
        var surfaces=Tools.Values.Select(tool=>TopLevel.GetTopLevel(tool.View)).Append(TopLevel.GetTopLevel(Control)).OfType<Visual>().Distinct();
        foreach(var surface in surfaces)
        {
            foreach(var control in surface.GetVisualDescendants().OfType<ToolControl>().Where(control=>control.IsEffectivelyVisible))
                if(control.DataContext is CadToolDock group)group.UpdateDropTargets(control.Bounds.Width,control.Bounds.Height);
            foreach(var control in surface.GetVisualDescendants().OfType<DocumentControl>().Where(control=>control.IsEffectivelyVisible))
                if(control.DataContext is CadDocumentDock group)
                {
                    group.UpdateDropTargets(control.Bounds.Width,control.Bounds.Height);
                    foreach(var document in (group.VisibleDockables??[]).OfType<global::Dock.Model.Mvvm.Core.DockableBase>())CadDockPolicy.Apply(document,control.Bounds.Width,control.Bounds.Height,true);
                }
        }
    }
    private Zone ResolveZone(CadDockTool tool)
    {
        IDockable child=tool;
        for(var owner=tool.Owner;owner is not null;owner=owner.Owner)
        {
            foreach(var pair in Groups)if(ReferenceEquals(pair.Value,owner))return pair.Key;
            if(owner.Id=="pair.LeftTop")return Zone.LeftTop;
            if(owner.Id=="pair.RightTop")return Zone.RightTop;
            if(owner.Id=="pair.BottomLeft")return Zone.BottomRight;
            if(owner.Id=="middle" && owner is IDock middle && middle.VisibleDockables is { } siblings)
            {
                var documents=siblings.FirstOrDefault(item=>Find(item).Any(d=>d is CadDockDocument));
                if(documents is not null && !ReferenceEquals(child,documents))return siblings.IndexOf(child)<siblings.IndexOf(documents)?Zone.LeftTop:Zone.RightTop;
            }
            if(owner.Id=="workspace" && child.Id!="middle")return Zone.BottomRight;
            child=owner;
        }
        return tool.Model.Zone;
    }
    private void UpdateMinimums()
    {
        Size Minimum(IDockable item)
        {
            if(item is ISplitter)return new Size(4,4);
            if(item is ToolDock tools)
            {
                tools.MinWidth=240;tools.MinHeight=140;
                return tools.VisibleDockables?.Count>0?new Size(240,140):default;
            }
            if(item is DocumentDock documents) {documents.MinWidth=320;documents.MinHeight=240;return new Size(320,240);}
            if(item is IDock dock)
            {
                var sizes=(dock.VisibleDockables??[]).Where(child=>child is not ISplitter).Select(Minimum).Where(size=>size.Width>0).ToArray();
                var horizontal=item is IProportionalDock {Orientation:Orientation.Horizontal};
                var width=sizes.Length==0?0:horizontal?sizes.Sum(size=>size.Width)+4*(sizes.Length-1):sizes.Max(size=>size.Width);
                var height=sizes.Length==0?0:horizontal?sizes.Max(size=>size.Height):sizes.Sum(size=>size.Height)+4*(sizes.Length-1);
                item.MinWidth=width;item.MinHeight=height;return new Size(width,height);
            }
            return default;
        }
        Minimum(Root);
        foreach(var window in Root.Windows??[])if(window.Layout is not null)Minimum(window.Layout);
        // A global edge drop inserts a sibling beside the entire side region.
        // Dock clamps each child independently; old proportions can then exceed
        // the available width and let the CAD surface overlap a toolbox.
        void Balance(IDockable item,Size available)
        {
            if(item is not IDock dock)return;
            var children=(dock.VisibleDockables??[]).Where(child=>child is not ISplitter && (child is not IDock childDock || !childDock.IsCollapsable || !childDock.IsEmpty)).ToArray();
            if(children.Length==0)return;
            if(item is not IProportionalDock split) {foreach(var child in children)Balance(child,available);return;}
            var horizontal=split.Orientation==Orientation.Horizontal;
            var extent=Math.Max(1,(horizontal?available.Width:available.Height)-4*(children.Length-1));
            var minimums=children.Select(child=>Math.Max(0,horizontal?child.MinWidth:child.MinHeight)).ToArray();
            extent=Math.Max(extent,minimums.Sum());
            var weights=children.Select(child=>double.IsFinite(child.Proportion) && child.Proportion>0?child.Proportion:1d/children.Length).ToArray();
            var lengths=new double[children.Length];var pending=Enumerable.Range(0,children.Length).ToList();var remaining=extent;
            while(pending.Count>0)
            {
                var total=pending.Sum(index=>weights[index]);
                var constrained=pending.Where(index=>remaining*weights[index]/total<minimums[index]).ToArray();
                if(constrained.Length==0) {foreach(var index in pending)lengths[index]=remaining*weights[index]/total;break;}
                foreach(var index in constrained){lengths[index]=minimums[index];remaining-=lengths[index];pending.Remove(index);}
            }
            for(var index=0;index<children.Length;index++)
            {
                children[index].Proportion=lengths[index]/extent;
                children[index].CollapsedProportion=children[index].Proportion;
                Balance(children[index],horizontal?new Size(lengths[index],available.Height):new Size(available.Width,lengths[index]));
            }
        }
        if(Control.Bounds.Width>0 && Control.Bounds.Height>0)
        {
            var available=Control.Bounds.Size;
            if(TopLevel.GetTopLevel(Control) is { } root && Control.TranslatePoint(default,root) is { } position)
            {
                // The native panel can temporarily arrange beyond its viewport
                // after enforcing a new minimum. Balance against client pixels,
                // including the shell's symmetric side-button strips.
                available=new Size(Math.Min(available.Width,Math.Max(1,root.ClientSize.Width-2*position.X)),available.Height);
            }
            Balance(Root,available);
        }
        foreach(var window in Root.Windows??[])if(window.Layout is not null && window.Width>0 && window.Height>0)Balance(window.Layout,new Size(window.Width,window.Height));
    }
    private Control? HostView(Control? view)
    {
        if(view is null)return null;
        // Dock's deferred host owns the logical parent of template output. Its
        // recycler cannot detach a persistent output control from that host.
        // Recreate this lightweight wrapper while retaining the business view.
        var host=new Border();_hosts[view]=host;
        if(view.Parent is null && TopLevel.GetTopLevel(view) is null) {host.Child=view;return host;}
        // Templates can be built in the middle of a layout pass. Moving an invalid
        // control to another root then leaves it in the old manager's arrange queue.
        // Drain that queue while detached, and only then attach the persistent view.
        Dispatcher.UIThread.Post(()=>
        {
            if(_disposed || !_hosts.TryGetValue(view,out var current) || current!=host)return;
            var previousRoot=TopLevel.GetTopLevel(view);
            if(view.Parent is Border previous && ReferenceEquals(previous.Child,view))previous.Child=null;
            if(previousRoot is not null)previousRoot.UpdateLayout();
            Dispatcher.UIThread.Post(()=>
            {
                if(!_disposed && _hosts.TryGetValue(view,out var latest) && latest==host && view.Parent is null)host.Child=view;
            },DispatcherPriority.Normal);
        },DispatcherPriority.Normal);
        return host;
    }
    public void Sync()
    {
        if(_sync)return;_sync=true;
        try {foreach(var tool in Tools.Values)
        {
            var visible=tool.Owner is IDock owner && owner.VisibleDockables?.Contains(tool)==true;
            if(!tool.Model.IsOpen && (visible || IsPinned(tool.Model)))Factory.CloseDockable(tool);
            else if(tool.Model.IsOpen && !visible && !IsPinned(tool.Model)) {if(Root.HiddenDockables?.Contains(tool)==true)Factory.RestoreDockable(tool);else Factory.AddDockable(Groups[tool.Model.Zone],tool);Factory.SetActiveDockable(tool);}
        }} finally {_sync=false;}
    }
    public bool IsPinned(Toolbox model)=>new[]{Root.LeftPinnedDockables,Root.RightPinnedDockables,Root.TopPinnedDockables,Root.BottomPinnedDockables}.Any(list=>list?.Contains(Tools[model])==true);
    private bool IsFloating(CadDockTool tool)=>Root.Windows?.Any(window=>window.Layout is not null && Find(window.Layout).Contains(tool))==true;
    public void Pin(Toolbox model,bool hidden) {if(IsPinned(model)!=hidden)Factory.PinDockable(Tools[model]);}
    public void Preview(Toolbox model)=>Factory.PreviewPinnedDockable(Tools[model]);
    public void HidePreview()=>Factory.HidePreviewingDockables(Root);
    public void Float(Toolbox model)=>Factory.FloatDockable(Tools[model]);
    public void Dock(Toolbox model,Zone zone)
    {
        var tool=Tools[model];if(IsPinned(model))Factory.UnpinDockable(tool);
        _sync=true;
        try {var destination=EnsureGroup(zone);if(tool.Owner is IDock source)Factory.MoveDockable(source,destination,tool,null);else Factory.AddDockable(destination,tool);tool.OriginalOwner=null;model.Zone=zone;model.IsOpen=true;Factory.SetActiveDockable(tool);}finally{_sync=false;}
    }
    public void Select(Toolbox model) {if(!model.IsOpen){model.IsOpen=true;Sync();}if(IsPinned(model))Preview(model);else Factory.SetActiveDockable(Tools[model]);}
    public NativeDockState Capture()
    {
        // Factory-created splitters/groups may have empty or repeated IDs. Assign stable
        // structural IDs before recording active items and original hidden/pinned owners.
        var all=Find(Root).Concat(Root.Windows?.Where(w=>w.Layout is not null).SelectMany(w=>Find(w.Layout!))??[]).Distinct().ToArray();
        var ids=new HashSet<string>();
        foreach(var item in all)
            if(string.IsNullOrWhiteSpace(item.Id)||!ids.Add(item.Id)) {item.Id="layout."+Guid.NewGuid().ToString("N");ids.Add(item.Id);}
        var listed=all.OfType<CadDockTool>().Select(t=>t.Id).Concat(new[]{Root.LeftPinnedDockables,Root.RightPinnedDockables,Root.TopPinnedDockables,Root.BottomPinnedDockables}.SelectMany(list=>list??[]).Select(d=>d.Id)).ToHashSet();
        return new NativeDockState
        {
            Version=2,Layout=Node(Root),Hidden=Tools.Values.Where(t=>!listed.Contains(t.Id)).Select(t=>t.Id).ToArray(),
            Left=Root.LeftPinnedDockables?.Select(d=>d.Id).ToArray()??[],Right=Root.RightPinnedDockables?.Select(d=>d.Id).ToArray()??[],Top=Root.TopPinnedDockables?.Select(d=>d.Id).ToArray()??[],Bottom=Root.BottomPinnedDockables?.Select(d=>d.Id).ToArray()??[],
            Owners=Tools.Values.ToDictionary(t=>t.Id,t=>(t.OriginalOwner??t.Owner)?.Id??"zone."+t.Model.Zone),
            Windows=Root.Windows?.Where(w=>w.Layout is not null).Select(w=>new NativeDockWindowState {Layout=Node(w.Layout!),Title=w.Title,X=w.X,Y=w.Y,Width=w.Width,Height=w.Height}).ToList()??[]
        };
    }
    private static NativeDockNode Node(IDockable model)=>new() {Kind=model switch {IRootDock=>"root",IToolDock=>"tools",IDocumentDock=>"documents",IProportionalDock=>"split",ISplitter=>"separator",CadDockTool=>"tool",CadDockDocument=>"document",_=>throw new InvalidOperationException("Unsupported dock type")},Id=model.Id,Proportion=double.IsFinite(model.Proportion)?model.Proportion:-1,Orientation=model is IProportionalDock split?(int)split.Orientation:0,Alignment=model is IToolDock tool?(int)tool.Alignment:0,Active=model is IDock dock?dock.ActiveDockable?.Id:null,Children=model is IDock children?children.VisibleDockables?.Select(Node).ToList()??[]:[]};
    public void Restore(NativeDockState saved)
    {
        var models=Tools.Values.Cast<IDockable>().ToDictionary(d=>d.Id);var document=Find(Root).OfType<CadDockDocument>().Single();models[document.Id]=document;
        var seen=new HashSet<string>();var nodes=0;
        IDockable Build(NativeDockNode node,int depth)
        {
            if(depth>32 || ++nodes>256 || string.IsNullOrWhiteSpace(node.Id) || !seen.Add(node.Id))throw new InvalidDataException("Invalid or duplicate dock node.");
            if(node.Kind is "tool" or "document")return models.TryGetValue(node.Id,out var leaf)?leaf:throw new InvalidDataException("Unknown dock content.");
            IDockable result=node.Kind switch {"root"=>new RootDock {EnableGlobalDocking=false,IsCollapsable=false},"tools"=>new CadToolDock {IsCollapsable=true,CanCloseLastDockable=true,MinWidth=240,MinHeight=140,Alignment=(Alignment)node.Alignment},"documents"=>new CadDocumentDock {IsCollapsable=false,CanCreateDocument=false},"split"=>new ProportionalDock {Orientation=(Orientation)node.Orientation},"separator"=>new ProportionalDockSplitter(),_=>throw new InvalidDataException("Unknown dock node.")};
            result.Id=node.Id;result.Proportion=node.Proportion<0?double.NaN:Math.Clamp(node.Proportion,0,1);
            if(result is IDock dock){dock.VisibleDockables=Factory.CreateList<IDockable>(node.Children.Select(child=>Build(child,depth+1)).ToArray());dock.ActiveDockable=dock.VisibleDockables.FirstOrDefault(d=>d.Id==node.Active)??dock.VisibleDockables.FirstOrDefault(d=>d is not ISplitter);}
            return result;
        }
        var root=Build(saved.Layout,0) as RootDock??throw new InvalidDataException("Missing root dock.");
        IList<IDockable> List(string[] ids)=>Factory.CreateList<IDockable>(ids.Select(id=>models.TryGetValue(id,out var model)&&seen.Add(id)?model:throw new InvalidDataException("Unknown or duplicated hidden dock content.")).ToArray());
        root.HiddenDockables=List(saved.Hidden);root.LeftPinnedDockables=List(saved.Left);root.RightPinnedDockables=List(saved.Right);root.TopPinnedDockables=List(saved.Top);root.BottomPinnedDockables=List(saved.Bottom);
        root.Windows=Factory.CreateList<IDockWindow>();foreach(var window in saved.Windows){var layout=Build(window.Layout,0) as RootDock??throw new InvalidDataException("Missing floating root.");layout.IsCollapsable=true;foreach(var group in Find(layout).OfType<ToolDock>())group.IsCollapsable=true;if(!double.IsFinite(window.Width)||!double.IsFinite(window.Height)||!double.IsFinite(window.X)||!double.IsFinite(window.Y))throw new InvalidDataException("Invalid floating bounds.");var dockWindow=new Dock.Model.Mvvm.Core.DockWindow {Id="window."+layout.Id,Title=string.IsNullOrWhiteSpace(window.Title)?Find(layout).OfType<CadDockTool>().FirstOrDefault()?.Title??"Direct2dCad":window.Title,Layout=layout,Width=Math.Clamp(window.Width,250,1800),Height=Math.Clamp(window.Height,200,1200),X=window.X,Y=window.Y,Owner=root};layout.Window=dockWindow;root.Windows.Add(dockWindow);}
        if(!seen.Contains(document.Id))throw new InvalidDataException("The document host is missing.");
        foreach(var tool in Tools.Values)if(!seen.Contains(tool.Id))root.HiddenDockables.Add(tool);
        root.DefaultDockable=root.ActiveDockable;
        _sync=true;
        try
        {
            Root=root;Groups.Clear();
            foreach(var group in Find(Root).OfType<ToolDock>())if(group.Id.StartsWith("zone.")&&Enum.TryParse<Zone>(group.Id[5..],out var zone))Groups[zone]=group;
            foreach(var zone in Enum.GetValues<Zone>())EnsureGroup(zone);
            Factory.InitDockable(Root,null);
            var docks=Find(Root).Concat(Root.Windows.SelectMany(w=>Find(w.Layout!))).OfType<IDock>().ToDictionary(d=>d.Id);
            foreach(var tool in Tools.Values)
            {
                if(IsPinned(tool.Model)||Root.HiddenDockables.Contains(tool)||IsFloating(tool))
                    tool.OriginalOwner=saved.Owners.TryGetValue(tool.Id,out var id)&&docks.TryGetValue(id,out var owner)?owner:Groups[tool.Model.Zone];
                tool.Model.IsOpen=!Root.HiddenDockables.Contains(tool);
                if(tool.Owner is ToolDock dock && Groups.FirstOrDefault(p=>ReferenceEquals(p.Value,dock)) is var pair && pair.Value is not null)tool.Model.Zone=pair.Key;
            }
            Control.Layout=Root;
            UpdateMinimums();
        }
        finally {_sync=false;}
    }
    private ToolDock EnsureGroup(Zone zone)
    {
        if(Groups.TryGetValue(zone,out var existing))
        {
            if(Find(Root).Contains(existing))return existing;
            // A title drag can float an entire canonical group. Keep a main
            // workspace destination for reopening and the floating Dock command.
            existing.Id="layout."+Guid.NewGuid().ToString("N");Groups.Remove(zone);
        }
        var group=new CadToolDock {Id="zone."+zone,IsCollapsable=true,CanCloseLastDockable=true,MinWidth=240,MinHeight=140,Alignment=zone is Zone.LeftTop or Zone.LeftBottom?Alignment.Left:zone is Zone.RightTop or Zone.RightBottom?Alignment.Right:Alignment.Bottom,VisibleDockables=Factory.CreateList<IDockable>()};
        var pairId=zone is Zone.LeftTop or Zone.LeftBottom?"pair.LeftTop":zone is Zone.RightTop or Zone.RightBottom?"pair.RightTop":"pair.BottomLeft";
        var parent=Find(Root).OfType<ProportionalDock>().FirstOrDefault(d=>d.Id==pairId)??Find(Root).OfType<ProportionalDock>().First();
        if(parent.VisibleDockables?.Count>0)Factory.AddDockable(parent,new ProportionalDockSplitter {Id="layout."+Guid.NewGuid().ToString("N")});
        Factory.AddDockable(parent,group);Groups[zone]=group;return group;
    }
    internal static IEnumerable<IDockable> Find(IDockable model){yield return model;if(model is IDock dock && dock.VisibleDockables is not null)foreach(var child in dock.VisibleDockables)foreach(var nested in Find(child))yield return nested;}
    public void ResetLayout()
    {
        _sync=true;
        try {foreach(var window in Factory.HostWindows.ToArray())window.Exit();Restore(_defaultLayout);}
        finally {_sync=false;}
    }
    public void Dispose() {if(_disposed)return;_disposed=true;foreach(var template in _globalTemplates)global::Avalonia.Application.Current!.DataTemplates.Remove(template);foreach(var window in Factory.HostWindows.ToArray())window.Exit();Control.LayoutUpdated-=RefreshDropTargets;foreach(var tool in Tools.Values)tool.View.SizeChanged-=ViewSizeChanged;foreach(var view in Tools.Values.Select(t=>t.View).OfType<IDisposable>())view.Dispose();_hosts.Clear();}
}
internal sealed class NativeDockState {public int Version {get;set;} public NativeDockNode Layout {get;set;}=new();public string[] Hidden {get;set;}=[];public string[] Left {get;set;}=[];public string[] Right {get;set;}=[];public string[] Top {get;set;}=[];public string[] Bottom {get;set;}=[];public Dictionary<string,string> Owners {get;set;}=[];public List<NativeDockWindowState> Windows {get;set;}=[];}
internal sealed class NativeDockNode {public string Kind {get;set;}="root";public string Id {get;set;}="";public double Proportion {get;set;}=-1;public int Orientation {get;set;}public int Alignment {get;set;}public string? Active {get;set;}public List<NativeDockNode> Children {get;set;}=[];}
internal sealed class NativeDockWindowState {public string Title {get;set;}="";public NativeDockNode Layout {get;set;}=new();public double X {get;set;}public double Y {get;set;}public double Width {get;set;}public double Height {get;set;}}


