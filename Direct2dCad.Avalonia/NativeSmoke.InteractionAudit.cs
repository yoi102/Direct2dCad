using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckInteractionAuditAsync(MainWindow window, Views.EditorView editor, NativeSmokeReport report, Action<bool, string> assert)
    {
        var failures = new List<string>();
        var vm = editor.Model.CadDocumentViewModel;
        async Task Probe(string name, Func<Task> check)
        {
            try { await check(); report.Passed.Add(name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
            finally { vm.Escape(); editor.Canvas.Focus(); }
        }
        await Probe("Dynamic Enter release in another panel permits the next numeric drawing while autorepeat remains suppressed", async () =>
        {
            async Task<TextBox> Radius(double value)
            {
                vm.SetToolMode(CadCanvasToolMode.CircleCenterRadius);
                var point = vm.CadEditor.Viewport.WorldToScreen(new CadPointD(0, 0));
                vm.PointerDown(point, CadCanvasPointerButton.Left, false); vm.PointerUp(point, CadCanvasPointerButton.Left);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); window.UpdateLayout();
                var box = editor.FindControl<ItemsControl>("DynamicFields")!.GetVisualDescendants().OfType<TextBox>().Single();
                box.Focus(); box.Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
                return box;
            }
            await Radius(27);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            assert(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>().Any(c => !c.IsErased && c.Radius == 27), "First radius did not commit.");
            var entityCount = vm.CadEditor.Document.Entities.Values.Count(c => !c.IsErased);
            var prompt = vm.StepInputError;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            assert(vm.StepInputError == prompt && vm.CadEditor.Document.Entities.Values.Count(c => !c.IsErased) == entityCount, "Held Enter continued into the canvas after numeric input committed.");
            var terminal = window.ToolView(window.Model.CommandLine)!.FindControl<TextBox>("Input")!;
            assert(terminal.Focus(), "Command input could not receive the release.");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await Radius(28);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            assert(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>().Any(c => !c.IsErased && c.Radius == 28), "Enter stayed latched after its release in another panel.");
        });
        await Probe("Command-line Enter ignores autorepeat and observes handled release after focus returns to the canvas", async () =>
        {
            var terminal = window.ToolView(window.Model.CommandLine)!.FindControl<TextBox>("Input")!;
            var commands = window.Model.CommandLine;
            commands.CommandText = "line"; commands.DismissSuggestions(); terminal.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            if (commands.ExecuteCommandCommand.ExecutionTask is { } first) await first;
            assert(vm.CadCanvasToolMode == CadCanvasToolMode.Line && commands.CommandText == "", "Command-line Enter did not run Line.");
            commands.CommandText = "circle"; commands.DismissSuggestions();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            assert(commands.CommandText == "circle" && vm.CadCanvasToolMode == CadCanvasToolMode.Line, "Repeated Enter ran a second terminal command.");
            editor.Canvas.Focus();
            editor.Canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Enter, Handled = true });
            terminal.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            if (commands.ExecuteCommandCommand.ExecutionTask is { } second) await second;
            assert(commands.CommandText == "" && vm.CadCanvasToolMode == CadCanvasToolMode.CircleCenterRadius, "Terminal Enter stayed blocked after a handled release outside the terminal.");
        });
        await Probe("Holding Escape closes only the ribbon menu and preserves an unfinished CAD operation until the next distinct press", async () =>
        {
            vm.SetToolMode(CadCanvasToolMode.Line);
            var point = vm.CadEditor.Viewport.WorldToScreen(new CadPointD(0, 0));
            vm.PointerDown(point, CadCanvasPointerButton.Left, false); vm.PointerUp(point, CadCanvasPointerButton.Left);
            var anchor = vm.DynamicInputScreenGeometry.Anchor;
            var button = window.GetLogicalDescendants().OfType<CadRibbonDropDownButton>().Single(c => global::Avalonia.Automation.AutomationProperties.GetAutomationId(c) == "NewDocumentButton");
            button.Focus(); window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var menu = (MenuFlyout)button.Flyout!;
            assert(menu.IsOpen && anchor is not null, "Menu isolation fixture was not ready.");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            assert(!menu.IsOpen && vm.DynamicInputScreenGeometry.Anchor == anchor, "First Escape did not close only the menu.");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            var preserved = vm.DynamicInputScreenGeometry.Anchor == anchor;
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            assert(preserved, "Repeated Escape escaped the closed menu and cancelled the CAD operation.");
            editor.Canvas.Focus(); window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            assert(vm.CadCanvasToolMode == CadCanvasToolMode.Select, "A fresh Escape press no longer cancels CAD.");
        });
        await Probe("Invalid persisted Dock layouts are rejected before changing live owners or layout", () =>
        {
            var shell = window.NativeDock;
            var original = shell.Root;
            var json = JsonSerializer.Serialize(shell.Capture(), SettingsJsonContext.Default.NativeDockState);
            var malformed = new[]
            {
                json.Replace("\"Layout\": {", "\"Layout\": null, \"IgnoredLayout\": {", StringComparison.Ordinal),
                json.Replace("\"Owners\": {", "\"Owners\": null, \"IgnoredOwners\": {", StringComparison.Ordinal),
                json.Replace("\"Windows\": []", "\"Windows\": null", StringComparison.Ordinal)
            };
            foreach (var input in malformed)
            {
                assert(input != json, "Invalid Dock JSON fixture did not change.");
                var state = JsonSerializer.Deserialize(input, SettingsJsonContext.Default.NativeDockState)!;
                Exception? error = null;
                try { shell.Restore(state); } catch (Exception ex) { error = ex; }
                assert(error is InvalidDataException && ReferenceEquals(shell.Root, original), $"Invalid Dock state escaped safe rejection: {error?.GetType().Name ?? "accepted"}; root retained={ReferenceEquals(shell.Root, original)}.");
            }
            // Also reject null nested children, undefined enums and wrong leaf kinds.
            foreach (var mutate in new Action<NativeDockState>[]
            {
                state => state.Layout.Children = null!,
                state => state.Layout.Children[0].Children.Add(null!),
                state => state.Layout.Children[0].Orientation = 999,
                state => state.Hidden = ["cad.documents"],
                state => state.Layout.Children[0].Kind = "tool"
            })
            {
                var state = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.NativeDockState)!; mutate(state);
                Exception? error = null;
                try { shell.Restore(state); } catch (Exception ex) { error = ex; }
                assert(error is InvalidDataException && ReferenceEquals(shell.Root, original), "Malformed nested Dock state changed the live layout or escaped safe rejection.");
            }
            return Task.CompletedTask;
        });
        assert(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
