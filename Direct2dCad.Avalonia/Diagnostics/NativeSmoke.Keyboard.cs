using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.Db.Geometry;
using CommunityToolkit.Mvvm.Input;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckRibbonKeyboardAsync(MainWindow window, NativeSmokeReport report, Action<bool, string> assert)
    {
        var button = window.GetLogicalDescendants().OfType<CadRibbonDropDownButton>().Single(control => global::Avalonia.Automation.AutomationProperties.GetAutomationId(control) == "NewDocumentButton");
        var menu = (MenuFlyout)button.Flyout!;
        var first = menu.Items.OfType<MenuItem>().First();
        var command = first.Command;
        var executions = 0;
        var document = window.Model.CurrentEditorTabViewModel!.CadDocumentViewModel;
        document.SetToolMode(CadCanvasToolMode.Line);
        var start = document.CadEditor.Viewport.WorldToScreen(new CadPointD(0, 0));
        document.PointerDown(start, CadCanvasPointerButton.Left, false); document.PointerUp(start, CadCanvasPointerButton.Left);
        var anchor = document.DynamicInputScreenGeometry.Anchor;
        assert(anchor is not null, "Line start did not produce an anchor for popup isolation.");
        void Key(Key key)
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        }
        try
        {
            assert(button.Focus(), "Ribbon dropdown could not receive focus.");
            Key(global::Avalonia.Input.Key.Down);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            assert(menu.IsOpen && first.IsFocused, "Down did not open the native ribbon menu and focus its first item.");
            command = first.Command;
            Key(global::Avalonia.Input.Key.Escape);
            assert(!menu.IsOpen && executions == 0 && document.DynamicInputScreenGeometry.Anchor == anchor, "Escape did not close only the native ribbon menu; it cancelled the pending CAD line.");
            button.Focus(); Key(global::Avalonia.Input.Key.Down);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            first.SetCurrentValue(MenuItem.CommandProperty, new RelayCommand(() => executions++));
            Key(global::Avalonia.Input.Key.Enter);
            assert(!menu.IsOpen && executions == 1, $"Enter did not execute exactly one ribbon menu command and dismiss the menu: open={menu.IsOpen}, executions={executions}, focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}.");
            report.Passed.Add("Native ribbon menus open with Down, focus their first item, cancel with Escape and dismiss after Enter execution");
            var point=global::Avalonia.VisualExtensions.TranslatePoint(button,new global::Avalonia.Point(button.Bounds.Width/2,button.Bounds.Height/2),window)!.Value;
            window.MouseDown(point,MouseButton.Right);window.MouseUp(point,MouseButton.Right);
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
            assert(menu.IsOpen,"Right click did not open the ribbon dropdown's existing menu.");
            Key(global::Avalonia.Input.Key.Escape);
            assert(!menu.IsOpen && executions==1 && document.DynamicInputScreenGeometry.Anchor==anchor,"Right-menu Escape altered the unfinished CAD operation.");
            report.Passed.Add("Ribbon right click opens the same menu; Escape dismisses it without running a command or cancelling the CAD operation");
        }
        finally { menu.Hide(); first.SetCurrentValue(MenuItem.CommandProperty, command); document.Escape(); document.SetToolMode(CadCanvasToolMode.Select); }
    }
}
