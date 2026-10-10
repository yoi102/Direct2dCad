using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Direct2dCad.Avalonia.Services;
using AvalonDock.Core;
using Direct2dCad.ViewModels;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckDockTargetsAsync(MainWindow window,Views.EditorView editor,NativeSmokeReport report,string output,Action<bool,string> assert)
    {
        var shell=window.NativeDock;
        for(var attempt=0;attempt<100 && !window.GetVisualDescendants().OfType<DocumentControl>().Any(control=>ReferenceEquals(control.DataContext,NativeDockShell.Find(shell.Root).OfType<CadDocumentDock>().Single()));attempt++){await Task.Delay(20);window.UpdateLayout();}
        for(var attempt=0;attempt<100 && TopLevel.GetTopLevel(editor.Canvas)!=window;attempt++){await Task.Delay(20);window.UpdateLayout();}
        assert(TopLevel.GetTopLevel(editor.Canvas)==window,"Reset did not reattach the CAD viewport before docking input.");
        shell.PrepareDockTargets();
        var layer=shell.Tools[window.Model.Layers];
        var properties=shell.Tools[window.Model.EntityProperties];
        var target=(CadToolDock)properties.Owner!;
        var manager=shell.Control.DockManager;
        assert(shell.Root.EnableGlobalDocking==false && target.EnableGlobalDocking==false,"Global full-workspace docking guides are still enabled.");
        assert(window.Model.EntityProperties.Zone==DockZone.RightTop,"Reset did not restore the full-height right property region.");
        assert(!manager.IsDockTargetVisible(layer,target,DockOperation.Left) && !manager.ValidateDockable(layer,target,DragAction.Move,DockOperation.Left,false),"Narrow side panel offered a horizontal split that cannot fit.");
        assert(manager.ValidateDockable(layer,target,DragAction.Move,DockOperation.Fill,false),"Tab joining is not available in a narrow tool region.");
        var document=shell.Root is { } ? NativeDockShell.Find(shell.Root).OfType<CadDocumentDock>().Single() : throw new InvalidOperationException();
        assert(manager.ValidateDockable(layer,document,DragAction.Move,DockOperation.Fill,false) && document.VisibleDockables!.OfType<CadDockDocument>().Single().CanClose==false,"Native CAD tab docking is unavailable or permits closing the CAD host.");
        var small=new CadToolDock(); small.UpdateDropTargets(300,200);
        assert((small.AllowedDropOperations&(DockOperationMask.Left|DockOperationMask.Right|DockOperationMask.Top|DockOperationMask.Bottom))==0,"Small tool surface still offers unfit splits.");
        small.UpdateDropTargets(600,400);
        assert((small.AllowedDropOperations&(DockOperationMask.Left|DockOperationMask.Right|DockOperationMask.Top|DockOperationMask.Bottom))==(DockOperationMask.Left|DockOperationMask.Right|DockOperationMask.Top|DockOperationMask.Bottom),"Resizing a tool surface did not restore split targets.");
        assert(manager.ValidateDockable(layer,document,DragAction.Move,DockOperation.Left,false),$"A spacious canvas did not offer native left docking: mask={document.AllowedDropOperations}, canvas={editor.Canvas.Bounds}; controls={string.Join(';',window.GetVisualDescendants().Where(control=>control is DocumentControl or DocumentDockControl or ToolControl).Select(control=>$"{control.GetType().Name}:{(control as StyledElement)?.DataContext?.GetType().Name}:{control.Bounds}:visible={control.IsEffectivelyVisible}:same={ReferenceEquals((control as StyledElement)?.DataContext,document)}"))}.");
        if(report.HeadlessWindowing)
        {
            var chrome=window.GetVisualDescendants().OfType<ToolChromeControl>().First(control=>ReferenceEquals(control.DataContext,layer.Owner));
            var grip=chrome.GetVisualDescendants().OfType<Control>().Single(control=>control.Name=="PART_Grip");
            var from=grip.TranslatePoint(new Point(40,grip.Bounds.Height/2),window)!.Value;
            var to=editor.Canvas.TranslatePoint(new Point(editor.Canvas.Bounds.Width/2,editor.Canvas.Bounds.Height/2),window)!.Value;
            var originalOwner=layer.Owner;
            var vm=window.Model.CurrentEditorTabViewModel!.CadDocumentViewModel;
            vm.SetToolMode(Direct2dCad.ViewModels.Enums.CadCanvasToolMode.Line);
            var start=vm.CadEditor.Viewport.WorldToScreen(new Direct2dCad.Db.Geometry.CadPointD(0,0));
            vm.PointerDown(start,CadCanvasPointerButton.Left,false);vm.PointerUp(start,CadCanvasPointerButton.Left);
            var anchor=vm.DynamicInputScreenGeometry.Anchor;
            window.MouseDown(from,MouseButton.Left);
            window.MouseMove(from+new Vector(20,0),RawInputModifiers.LeftMouseButton);
            window.MouseMove(to,RawInputModifiers.LeftMouseButton);
            await Task.Delay(100);window.UpdateLayout();
            window.MouseMove(to+new Vector(1,0),RawInputModifiers.LeftMouseButton);
            await Task.Delay(50);window.UpdateLayout();
            assert(shell.Control.IsDraggingDock,"Native title drag did not begin through routed pointer input.");
            var liveGuides=window.GetVisualDescendants().OfType<DockTarget>().Where(control=>control.IsEffectivelyVisible).ToArray();
            using(var dragFrame=window.CaptureRenderedFrame())dragFrame!.Save(Path.Combine(Path.GetDirectoryName(output)!,"dock-drag.png"));
            assert(liveGuides.Length>0 && liveGuides.SelectMany(control=>control.GetVisualDescendants().OfType<Image>()).Where(control=>control.IsEffectivelyVisible).All(control=>control.Source is DrawingImage),"Active pointer drag does not display the native vector docking guide.");
            assert(!window.GetVisualDescendants().OfType<GlobalDockTarget>().Any(control=>control.IsEffectivelyVisible),"Title drag still displays whole-workspace docking arrows.");
            window.KeyPress(Key.Escape,RawInputModifiers.None,PhysicalKey.None,null);window.KeyRelease(Key.Escape,RawInputModifiers.None,PhysicalKey.None,null);
            assert(!shell.Control.IsDraggingDock && ReferenceEquals(layer.Owner,originalOwner) && vm.DynamicInputScreenGeometry.Anchor==anchor,"Escape did not cancel only docking, or it cancelled the in-progress CAD line.");
            window.MouseUp(to,MouseButton.Left);
            vm.Escape();vm.SetToolMode(Direct2dCad.ViewModels.Enums.CadCanvasToolMode.Select);
            report.Passed.Add("Routed native title dragging shows local vector guides; Escape releases capture and preserves the pending CAD operation");
            var tab=window.GetVisualDescendants().OfType<ToolTabStripItem>().Single(control=>ReferenceEquals(control.DataContext,layer));
            var tabPoint=tab.TranslatePoint(new Point(tab.Bounds.Width/2,tab.Bounds.Height/2),window)!.Value;
            window.MouseDown(tabPoint,MouseButton.Left);
            window.MouseMove(tabPoint+new Vector(20,-20),RawInputModifiers.LeftMouseButton);
            window.MouseMove(to,RawInputModifiers.LeftMouseButton);
            await Task.Delay(100);window.UpdateLayout();
            window.MouseMove(to+new Vector(1,0),RawInputModifiers.LeftMouseButton);
            await Task.Delay(50);window.UpdateLayout();
            var selector=window.GetVisualDescendants().OfType<DockTarget>().Where(control=>control.IsEffectivelyVisible).SelectMany(control=>control.GetVisualDescendants().OfType<Image>()).Single(control=>control.Name=="PART_LeftSelector" && control.IsEffectivelyVisible);
            var drop=selector.TranslatePoint(new Point(selector.Bounds.Width/2,selector.Bounds.Height/2),window)!.Value;
            window.MouseMove(drop,RawInputModifiers.LeftMouseButton);
            await Task.Delay(50);window.UpdateLayout();
            using(var dragFrame=window.CaptureRenderedFrame())dragFrame!.Save(Path.Combine(Path.GetDirectoryName(output)!,"dock-drag.png"));
            window.MouseUp(drop,MouseButton.Left);
            assert(!shell.Control.IsDraggingDock,"Releasing over a native split target left dragging active.");
        }
        else assert(manager.ValidateDockable(layer,document,DragAction.Move,DockOperation.Left,true),"Approved native left drop could not execute.");
        await Task.Delay(150);window.UpdateLayout();
        var splitOwner=layer.Owner;
        assert(splitOwner is CadToolDock && !ReferenceEquals(splitOwner,target),"Native left split was silently rewritten as tab joining.");
        var splitControl=window.GetVisualDescendants().OfType<ToolDockControl>().Single(control=>ReferenceEquals(control.DataContext,splitOwner));
        assert(splitControl.Bounds.Width>=240 && editor.Canvas.Bounds.Width>=320,"Native split starved the toolbox or the CAD viewport.");
        var toolRect=new Rect(splitControl.TranslatePoint(default,window)!.Value,splitControl.Bounds.Size);
        var canvasRect=new Rect(editor.Canvas.TranslatePoint(default,window)!.Value,editor.Canvas.Bounds.Size);
        assert(toolRect.Right<=canvasRect.Left+1,"Native left split overlaps the canvas.");
        var captured=shell.Capture();
        assert(captured.Version==2,"New docking policy is not versioned for migration.");
        if(report.HeadlessWindowing)
        {
            using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(Path.GetDirectoryName(output)!,"dock-split.png"));
            var guide=new DockTarget();
            var host=new Window {Width=500,Height=400,Content=guide};
            try
            {
                host.Show(window);host.UpdateLayout();
                var images=guide.GetVisualDescendants().OfType<Image>().Where(image=>image.Name?.EndsWith("Selector")==true).ToArray();
                assert(images.Length==5 && images.All(image=>image.Source is DrawingImage),"Dock guides still use legacy white bitmap arrows.");
                guide.GetVisualDescendants().OfType<Panel>().Single(panel=>panel.Name=="PART_CenterIndicator").Opacity=.25;
                using var guideFrame=host.CaptureRenderedFrame();guideFrame!.Save(Path.Combine(Path.GetDirectoryName(output)!,"dock-guides.png"));
            }
            finally {host.Close();}
        }
        window.DockTool(window.Model.Layers,DockZone.LeftTop);await Task.Delay(100);window.UpdateLayout();
        assert(ReferenceEquals(layer.Owner,shell.Groups[DockZone.LeftTop]) && shell.Groups[DockZone.RightTop].VisibleDockables!.Contains(properties),"Explicit redocking changed another panel or lost its stable destination.");
        var navigation=shell.Groups[DockZone.LeftTop];
        shell.Factory.FloatDockable(navigation);await Task.Delay(100);window.UpdateLayout();
        assert(shell.Root.Windows?.Count==1 && !ReferenceEquals(shell.Groups[DockZone.LeftTop],navigation) && NativeDockShell.Find(shell.Root).Contains(shell.Groups[DockZone.LeftTop]),"Floating a whole pane lost the main workspace's navigation destination.");
        assert(shell.Root.Windows!.Single().Host is HostWindow {WindowDecorations: WindowDecorations.None, ToolChromeControlsWholeWindow: true}, "Floating multiple tabs restored the duplicate Windows caption.");
        foreach(var tool in navigation.VisibleDockables!.OfType<CadDockTool>().ToArray())shell.Factory.PinDockable(tool);
        await Task.Delay(150);window.UpdateLayout();
        assert(shell.Root.Windows!.Count==0 && shell.Groups[DockZone.LeftTop].VisibleDockables!.Contains(layer) && shell.Groups[DockZone.LeftTop].VisibleDockables!.Contains(shell.Tools[window.Model.DocumentExplorer]),"Dock commands could not return an entire floated pane without leaving an empty host.");
        assert(ReferenceEquals(shell.Factory.FindRoot(layer,_=>true),shell.Root) && ReferenceEquals(shell.Groups[DockZone.LeftTop].Factory,shell.Factory),"Recreated main pane is missing its native owner/factory and cannot run its chrome commands.");
        report.Passed.Add("Floating a whole pane preserves the main destination; native Dock commands return its tools and close the empty floating window");
        var activeDocument=window.Model.CurrentEditorTabViewModel;
        window.FloatTool(window.Model.DocumentExplorer);await Task.Delay(100);
        var floatingHost=(HostWindow)shell.Root.Windows!.Single().Host!;floatingHost.UpdateLayout();
        var close=floatingHost.GetVisualDescendants().OfType<ToolChromeControl>().Single(control=>control.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="PART_CloseButton");
        close.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
        assert(shell.Root.Windows!.Count==0 && !window.Model.DocumentExplorer.IsOpen && window.IsVisible && ReferenceEquals(activeDocument,window.Model.CurrentEditorTabViewModel),"Floating Dock close button closed the main application, kept an empty host or failed to hide its tool.");
        window.DockTool(window.Model.DocumentExplorer,DockZone.LeftTop);await Task.Delay(100);window.UpdateLayout();
        assert(window.Model.DocumentExplorer.IsOpen && shell.Groups[DockZone.LeftTop].VisibleDockables!.Contains(shell.Tools[window.Model.DocumentExplorer]),"A closed borderless floating tool could not reopen in the main dock.");
        report.Passed.Add("Borderless floating title Close hides only its tool, removes the empty host, preserves the active drawing and allows reopening");
        report.Passed.Add("Dock guides and execution share native split/tab validation; narrow targets hide unfit directions; global overlays are disabled; canvas remains unobstructed");
    }

    private static async Task CheckBorderlessFloatingWindowAsync(HostWindow host, NativeSmokeReport report, Action<bool,string> assert, string output)
    {
        host.UpdateLayout();
        assert(host.WindowDecorations==WindowDecorations.None && !host.ExtendClientAreaToDecorationsHint && host.CanResize && host.ToolChromeControlsWholeWindow && host.DocumentChromeControlsWholeWindow,
            "Floating Dock hosts retain a system caption or lack native Dock movement/resize configuration.");
        var chrome=host.GetVisualDescendants().OfType<ToolChromeControl>().Single(control=>control.IsEffectivelyVisible);
        var grip=chrome.GetVisualDescendants().OfType<Control>().Single(control=>control.Name=="PART_Grip");
        assert(grip.TranslatePoint(default,host)!.Value.Y<8 && grip.Bounds.Height>0,"Borderless floating content still reserves an empty Windows caption area.");
        var maximize=chrome.GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="PART_MaximizeRestoreButton");
        maximize.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        assert(host.WindowState==WindowState.Maximized,"Dock's maximize action does not control its borderless host.");
        maximize.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        assert(host.WindowState==WindowState.Normal,"Dock's restore action did not restore its borderless host.");
        if(report.HeadlessWindowing)
        {
            host.MouseMove(new Point(1,host.Bounds.Height/2));var horizontal=host.Cursor;
            host.MouseMove(new Point(host.Bounds.Width/2,1));var vertical=host.Cursor;
            host.MouseMove(new Point(1,1));var diagonal=host.Cursor;
            assert(horizontal is not null && vertical is not null && diagonal is not null && !ReferenceEquals(horizontal,vertical) && !ReferenceEquals(vertical,diagonal),"Borderless window edges do not provide distinct resize cursors.");
            host.MouseMove(new Point(host.Bounds.Width/2,host.Bounds.Height/2));
            assert(host.Cursor is null,"A resize cursor persisted over floating toolbox content.");
            await Task.Delay(100);host.UpdateLayout();
            using var frame=host.CaptureRenderedFrame();frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"dock-floating-borderless.png"));
        }
        report.Passed.Add("Floating Dock hosts have no system frame or duplicate caption; native title maximize/restore and client-edge resize cursors remain available");
    }
}


