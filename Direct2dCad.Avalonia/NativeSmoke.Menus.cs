using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckHandledCanvasReleaseAsync(MainWindow window,Views.EditorView view,NativeSmokeReport report,Action<bool,string> assert,string output)
    {
        var parent=view.FindControl<Grid>("CanvasSurface")!;
        void HandlePress(object? sender,PointerPressedEventArgs e)=>e.Handled=true;
        void HandleRelease(object? sender,PointerReleasedEventArgs e)=>e.Handled=true;
        parent.AddHandler(InputElement.PointerPressedEvent,HandlePress,global::Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        parent.AddHandler(InputElement.PointerReleasedEvent,HandleRelease,global::Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        try
        {
            var point=view.Canvas.TranslatePoint(new Point(220,200),window)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,point,MouseButton.Right);
            assert(view.Canvas.HasCapturedPointer,"A handled right press did not reach the canvas's gesture handler.");
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,point,MouseButton.Right);
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
            assert(view.Canvas.ContextMenu?.IsOpen==true && !view.Canvas.HasCapturedPointer,"A handled right release skipped canvas capture cleanup or context menu opening.");
            await Task.Delay(120);
            assert(view.Canvas.ContextMenu?.IsOpen==true,"Canvas menu closed immediately after the handled right release.");
            window.UpdateLayout();
            var menu=view.Canvas.ContextMenu!;
            assert(menu.Template is not null && menu.Bounds.Width>80 && menu.Bounds.Height>100 && menu.GetVisualDescendants().OfType<MenuItem>().Any(item=>item.IsEffectivelyVisible && item.Bounds.Height>0),$"Canvas context menu opened without a visible styled surface: template={menu.Template is not null}, bounds={menu.Bounds}.");
            assert(menu.Placement==PlacementMode.Pointer && menu.PlacementRect is null && menu.Bounds.Height<=560 && menu.GetVisualDescendants().OfType<MenuItem>().Where(item=>item.IsEffectivelyVisible && item.Bounds.Height>0).All(item=>item.Bounds.Height<=33),$"Canvas menu has oversized rows or stale anchor placement: {menu.Bounds}.");
            var scroll=menu.GetVisualDescendants().OfType<ScrollViewer>().First();
            assert(scroll.VerticalScrollBarVisibility==global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,"Long canvas menus cannot scroll within their available height.");
            var clickScreen=view.Canvas.PointToScreen(new Point(220,200));
            var menuScreen=menu.PointToScreen(default);
            var scale=TopLevel.GetTopLevel(menu)!.RenderScaling;
            assert(Math.Min(Math.Abs(menuScreen.X-clickScreen.X),Math.Abs(menuScreen.X+menu.Bounds.Width*scale-clickScreen.X))<=8 && Math.Min(Math.Abs(menuScreen.Y-clickScreen.Y),Math.Abs(menuScreen.Y+menu.Bounds.Height*scale-clickScreen.Y))<=8,$"Canvas menu is detached from the click: click={clickScreen}, menu={menuScreen}, size={menu.Bounds.Size}.");
            using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(TopLevel.GetTopLevel(menu)!))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"canvas-context-menu.png"));
            await CheckCompactScrollBarAsync(scroll,assert,output,"canvas-scrollbar");
            report.Passed.Add("Canvas menu scroll bar stays narrow during hover and pressed thumb drag; dragging scrolls command content");
            view.Canvas.ContextMenu!.Close();
            // Repeat near the canvas's lower right edge: recompute the height on
            // each invocation and preserve pan/wheel's separate gesture path.
            point=view.Canvas.TranslatePoint(new Point(view.Canvas.Bounds.Width-8,view.Canvas.Bounds.Height-8),window)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,point,MouseButton.Right);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,point,MouseButton.Right);
            await Task.Delay(120);window.UpdateLayout();
            assert(menu.IsOpen && !view.Canvas.HasCapturedPointer && menu.Bounds.Height<=menu.MaxHeight+1 && menu.MaxHeight<=560,"Canvas edge menu escaped its recomputed height limit or retained pointer capture.");
            menu.Close();
            report.Passed.Add("Handled canvas right click releases capture and opens visible compact, pointer-placed, scrollable command menu at normal and lower-right canvas positions");
        }
        finally
        {
            parent.RemoveHandler(InputElement.PointerPressedEvent,HandlePress);parent.RemoveHandler(InputElement.PointerReleasedEvent,HandleRelease);
        }
    }

    private static async Task CheckLayerMenuAsync(MainWindow window, NativeSmokeReport report, Action<bool,string> assert, string output)
    {
        var model=window.Model.Layers;
        window.NativeDock.Select(model);
        var view=window.ToolView(model) as Views.Toolboxes.LayersView ?? throw new InvalidOperationException("Layer business view is missing.");
        for(var attempt=0;attempt<100 && TopLevel.GetTopLevel(view)!=window;attempt++){window.UpdateLayout();await Task.Delay(20);}
        window.UpdateLayout();
        var list=view.FindControl<ListBox>("LayersList")!;
        var row=list.GetVisualDescendants().OfType<ListBoxItem>().First();
        var layer=(LayerItemViewModel)row.DataContext!;
        list.SelectedItem=null;
        var point=row.TranslatePoint(new Point(2,row.Bounds.Height/2),window)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,point,MouseButton.Right);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,point,MouseButton.Right);
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
        var menu=view.ContextMenu!;
        assert(menu.IsOpen && ReferenceEquals(model.SelectedLayer,layer),"Layer row right click did not select its own layer/open the layer actions.");
        assert(menu.Items.OfType<MenuItem>().Any(item=>ReferenceEquals(item.Command,model.AddLayerCommand)) && window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.ToolChromeControl>().All(chrome=>chrome.ToolFlyout?.IsOpen!=true),"Layer content still opens a docking menu.");
        window.UpdateLayout();
        var menuRows=menu.Items.OfType<MenuItem>().ToArray();
        assert(menuRows.Length>=8 && menuRows.All(item=>item.Bounds.Height>0 && item.Bounds.Height<=33 && item.FontSize<=12),"Layer actions retain oversized menu rows or font sizes.");
        foreach(var item in menuRows)
        {
            var caption=item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text=>Equals(text.Text,item.Header));
            assert(caption is not null && caption.IsEffectivelyVisible,"Compact layer menu lost its command caption.");
            if(caption is not null)
            {
                var captionPoint=caption.TranslatePoint(default,item)!.Value;
                assert(captionPoint.Y>=0 && captionPoint.Y+caption.Bounds.Height<=item.Bounds.Height+1,"Compact layer menu clips a command caption.");
            }
        }
        assert(menu.GetVisualDescendants().OfType<Separator>().All(separator=>separator.Bounds.Height<=1 && separator.Margin.Top+separator.Margin.Bottom<=8),"Layer menu retains oversized separator spacing.");
        using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(TopLevel.GetTopLevel(menu)!))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"layer-menu-compact.png"));
        var locked=menu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,Direct2dCad.Lang.CadUiText.Get("Locked")));
        var previous=layer.IsLocked;
        locked.Command!.Execute(null);
        assert(window.Model.CurrentEditorTabViewModel!.CadDocumentViewModel.CadEditor.Document.GetLayer(layer.LayerId).IsLocked!=previous,"Layer menu Lock did not use the business mutation path.");
        window.Model.CurrentEditorTabViewModel.CadDocumentViewModel.Undo();
        assert(window.Model.CurrentEditorTabViewModel.CadDocumentViewModel.CadEditor.Document.GetLayer(layer.LayerId).IsLocked==previous,"Layer menu mutation was not undoable.");
        menu.Close();
        point=list.TranslatePoint(new Point(list.Bounds.Width/2,list.Bounds.Height-10),window)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,point,MouseButton.Right);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,point,MouseButton.Right);
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
        assert(menu.IsOpen && window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.ToolChromeControl>().All(chrome=>chrome.ToolFlyout?.IsOpen!=true),"Layer blank content showed docking commands instead of layer actions.");
        menu.Close();
        report.Passed.Add("Compact layer menu rows and separators retain fully visible captions; row/blank right click selects layer actions and Lock changes are undoable");
    }

    private static async Task CheckToolTabMenuAsync(MainWindow window, NativeSmokeReport report, Action<bool,string> assert)
    {
        var tab = window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.ToolTabStripItem>().First(item =>
            item.IsEffectivelyVisible && item.DataContext is Services.CadDockTool tool && tool.Owner is global::Dock.Model.Core.IDock group && group.ActiveDockable != tool);
        var target = (Services.CadDockTool)tab.DataContext!;
        var owner = (global::Dock.Model.Core.IDock)target.Owner!; var previous = owner.ActiveDockable;
        var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, point, MouseButton.Right);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, point, MouseButton.Right);
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {}, global::Avalonia.Threading.DispatcherPriority.Background);
        var chrome = window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.ToolChromeControl>().Single(control => ReferenceEquals(control.DataContext, owner));
        assert(ReferenceEquals(owner.ActiveDockable, target) && chrome.ToolFlyout?.IsOpen == true, $"Inactive tool-tab right menu did not activate the clicked tool or open its docking commands: target={target.Id}, active={owner.ActiveDockable?.Id}, open={chrome.ToolFlyout?.IsOpen}, hit={window.InputHitTest(point)?.GetType().Name}, bounds={tab.Bounds}, point={point}.");
        chrome.ToolFlyout!.Hide();
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
        if (previous is not null) window.NativeDock.Factory.SetActiveDockable(previous);
        report.Passed.Add("Inactive tool-tab right click targets that tool before opening its docking menu");
    }

    private static async Task CheckListMenusAsync(MainWindow mainWindow, CadRecoveryEntry entry, NativeSmokeReport report, Action<bool,string> assert)
    {
        var main = mainWindow.Model;
        var documentsModel = main.LayoutService.Anchorables.OfType<DocumentExplorerToolboxViewModel>().Single();
        documentsModel.RefreshCommand.Execute(null);
        var documents = new Views.Toolboxes.DocumentsView { DataContext = documentsModel };
        var recovery = new Views.Toolboxes.RecoveryView { DataContext = main.LayoutService.Anchorables.OfType<DrawingRecoveryToolboxViewModel>().Single() };
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,*") };
        grid.Children.Add(documents); Grid.SetRow(recovery, 1); grid.Children.Add(recovery);
        var testWindow = new Window { Width = 420, Height = 480, Content = grid };
        main.RecoveryEntries.Add(entry);
        testWindow.Show(); testWindow.UpdateLayout();
        async Task RightClick(Control control, Point local)
        {
            testWindow.UpdateLayout();
            var point = control.TranslatePoint(local, testWindow)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(testWindow, point, MouseButton.Right);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(testWindow, point, MouseButton.Right);
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {}, global::Avalonia.Threading.DispatcherPriority.Background);
        }
        try
        {
            var docList = documents.FindControl<ListBox>("Documents")!;
            var row = docList.GetVisualDescendants().OfType<ListBoxItem>().First();
            var document = (DocumentExplorerItemViewModel)row.DataContext!;
            docList.SelectedItem = null;
            await RightClick(row, new Point(2, row.Bounds.Height / 2));
            var menu = docList.ContextMenu!;
            assert(menu.IsOpen && ReferenceEquals(docList.SelectedItem, document), "Right-click on document row padding did not select its document or open the menu.");
            assert(menu.Items.OfType<MenuItem>().Any(item => ReferenceEquals(item.Command, main.CloseEditorDocumentCommand) && ReferenceEquals(item.CommandParameter, document.Document)), "Document panel menu targets a different document.");
            menu.Close();
            await RightClick(docList, new Point(docList.Bounds.Width / 2, docList.Bounds.Height - 10));
            assert(menu.IsOpen && ReferenceEquals(docList.SelectedItem, document), "Document list blank space did not open the selected document's menu.");
            menu.Close(); docList.SelectedItem = null;
            await RightClick(docList, new Point(docList.Bounds.Width / 2, docList.Bounds.Height - 10));
            assert(menu.IsOpen && menu.Items.OfType<MenuItem>().Count(item => item.IsEnabled) == 1, "No-selection document panel must enable Refresh only.");
            menu.Close();
            report.Passed.Add("Document panel right click covers row padding and blank list space; commands target the clicked or selected document and no selection enables Refresh only");

            var recoveryList = recovery.FindControl<ListBox>("Entries")!;
            testWindow.UpdateLayout();
            row = recoveryList.GetVisualDescendants().OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, entry));
            await RightClick(row, new Point(2, row.Bounds.Height / 2));
            menu = recoveryList.ContextMenu!;
            assert(menu.IsOpen && ReferenceEquals(recoveryList.SelectedItem, entry), "Recovery row padding failed to open/select its entry.");
            assert(menu.Items.OfType<MenuItem>().Any(item => ReferenceEquals(item.Command, main.OpenRecoveryCopyCommand) && ReferenceEquals(item.CommandParameter, entry)), "Recovery menu lost the clicked snapshot target.");
            menu.Close();
            await RightClick(recoveryList, new Point(recoveryList.Bounds.Width / 2, recoveryList.Bounds.Height - 10));
            assert(menu.IsOpen && ReferenceEquals(recoveryList.SelectedItem, entry), "Recovery blank list space failed to open the selected snapshot's menu.");
            menu.Close(); recoveryList.SelectedItem = null;
            await RightClick(recoveryList, new Point(recoveryList.Bounds.Width / 2, recoveryList.Bounds.Height - 10));
            assert(menu.IsOpen && menu.Items.OfType<MenuItem>().All(item => !item.IsEnabled || ReferenceEquals(item.Command, main.ClearAllRecoveryCommand)), "Recovery commands for a snapshot remained enabled without selection.");
            menu.Close();
            report.Passed.Add("Recovery panel right click covers row padding and blank list space; snapshot commands use the clicked/selected entry and are disabled without selection");
        }
        finally { testWindow.Close(); main.RecoveryEntries.Remove(entry); }
    }
}
