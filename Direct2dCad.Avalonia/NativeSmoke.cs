using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvalonDock.Core;
using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;
using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Avalonia;

internal sealed class NativeSmokeReport
{
    public DateTimeOffset TimestampUtc { get; set; }
    public string? ExecutableSha256 { get; set; }
    public bool NativeAot { get; set; }
    public bool HeadlessWindowing { get; set; }
    public List<string> Passed { get; set; } = [];
    public string? Failure { get; set; }
    public int ToolSchemas { get; set; }
    public int PropertyViews { get; set; }
    public bool HardwareDirect2D { get; set; }
}
[JsonSerializable(typeof(NativeSmokeReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SmokeJsonContext : JsonSerializerContext;

internal static partial class NativeSmoke
{
    public static async Task RunAsync(MainWindow window, IServiceProvider services, string output)
    {
        var report = new NativeSmokeReport { NativeAot = !Environment.GetCommandLineArgs().Contains("--allow-managed-smoke") && !RuntimeFeature.IsDynamicCodeSupported, HeadlessWindowing = Environment.GetCommandLineArgs().Contains("--smoke-test-headless"), TimestampUtc = DateTimeOffset.UtcNow,
            ExecutableSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!))) };
        void Assert(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        try
        {
            Assert(report.NativeAot || Environment.GetCommandLineArgs().Contains("--allow-managed-smoke"), "This validation must run from the NativeAOT published executable."); if(report.NativeAot)report.Passed.Add("NativeAOT runtime has no dynamic code");
            Assert(!File.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!,"Direct2dCad.Ole.Native.dll")),"Application output still contains the removed custom C++ bridge.");
            report.Passed.Add("C# CsWin32 OLE and printing run without Direct2dCad.Ole.Native.dll");
            var main = window.Model;
            Assert(main.CurrentEditorTabViewModel is null && window.FindControl<TabControl>("Documents")!.Items.Count == 1, "Startup must show the WPF welcome page without creating a dirty drawing.");
            await CheckEmptyApplicationExitAsync(window, report, Assert, output);
            main.NewCommand.Execute(null);
            report.Passed.Add("Welcome startup creates no drawing; New activates a CAD document");
            var tab = main.CurrentEditorTabViewModel ?? throw new InvalidOperationException("No active document.");
            var vm = tab.CadDocumentViewModel; var document = vm.CadEditor.Document;
            var editorView = (Views.EditorView)((TabItem)window.FindControl<TabControl>("Documents")!.SelectedItem!).Content!;
            var documentTabs=window.FindControl<TabControl>("Documents")!;var activeDocumentTab=documentTabs.SelectedItem;var welcomeTab=window.FindControl<TabItem>("WelcomeTab")!;
            documentTabs.SelectedItem=welcomeTab;
            Assert(main.CurrentEditorTabViewModel is null && ReferenceEquals(documentTabs.SelectedItem,welcomeTab),"An old CAD dock context stole focus from the welcome page.");
            documentTabs.SelectedItem=activeDocumentTab;
            Assert(ReferenceEquals(main.CurrentEditorTabViewModel,tab),"Selecting the CAD document did not restore its context after Welcome.");
            report.Passed.Add("Welcome and CAD tabs switch context without reentrant focus stealing");
            // Dock's deferred content template materializes the document host on a
            // subsequent render pass; input must target its attached canvas.
            for(var attempt=0;attempt<100 && TopLevel.GetTopLevel(editorView.Canvas) is null;attempt++) {window.UpdateLayout();await Task.Delay(20);}
            Assert(TopLevel.GetTopLevel(editorView.Canvas)==window,"Native dock did not materialize the active document canvas.");
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            window.UpdateLayout();
            Assert(window.GetVisualDescendants().OfType<Controls.CadIcon>().Any(icon => icon.Data is not null && icon.Template is not null && icon.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().Any()), "Material vector icons have no rendered PathIcon template.");
            Assert(window.GetLogicalDescendants().OfType<TabControl>().Any(control => control.Classes.Contains("ribbon") && control.TabStripPlacement == global::Avalonia.Controls.Dock.Bottom), "Ribbon tabs must be below the WPF tool strip.");
            var nativeWorkspace=NativeDockShell.Find(window.NativeDock.Root).OfType<global::Dock.Model.Mvvm.Controls.ProportionalDock>().Single(d=>d.Id=="workspace");
            Assert(nativeWorkspace.Orientation==global::Dock.Model.Core.Orientation.Vertical && nativeWorkspace.VisibleDockables!.Last().Id=="pair.BottomLeft" && window.GetVisualDescendants().Contains(window.NativeDock.Control), "Native bottom toolbox region must span the rendered workspace.");
            report.Passed.Add("Material icons have rendered templates; Ribbon tabs are below tools and bottom dock spans the workspace");
            Assert(window.GetVisualDescendants().Any(v => v.GetType().Namespace == "Material.Ripple"), "Core controls did not load Material templates.");
            Assert(global::Avalonia.Application.Current!.Styles.OfType<Material.Styles.Themes.CustomMaterialTheme>().Single() is { }, "Custom Material theme was not installed.");
            report.Passed.Add("Pinned Material control templates load inside the NativeAOT shell");
            if (report.HeadlessWindowing) await CheckRibbonKeyboardAsync(window, report, Assert);
            if (report.HeadlessWindowing)
            {
                var topMenu=window.GetLogicalDescendants().OfType<Menu>().First();var topFile=topMenu.Items.OfType<MenuItem>().First();
                var menuPoint=global::Avalonia.VisualExtensions.TranslatePoint(topFile,new global::Avalonia.Point(topFile.Bounds.Width/2,topFile.Bounds.Height/2),window)!.Value;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,menuPoint,global::Avalonia.Input.MouseButton.Left);
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,menuPoint,global::Avalonia.Input.MouseButton.Left);
                Assert(topFile.IsSubMenuOpen && topFile.Bounds.Height<=32,"Title File menu does not open at its visible header; a 48 px library item was clipped by the 32 px title bar.");
                await Task.Delay(120);window.UpdateLayout();
                var fileRows=topFile.Items.OfType<MenuItem>().ToArray();
                Assert(fileRows.Length>5 && fileRows.All(item=>item.Bounds.Height>0 && item.Bounds.Height<=33 && item.FontSize<=12),"File command menu retains oversized rows or font sizes.");
                using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"file-menu-compact.png"));
                topFile.IsSubMenuOpen=false;
                report.Passed.Add("Compact title menus open through real routed pointer clicks without clipped scrolling headers");
                Assert(topMenu.Items.OfType<MenuItem>().Contains(window.FindControl<MenuItem>("PanelsMenu")!),"View is still nested under File instead of beside it.");
                Assert(window.FindControl<Button>("ApplicationIconButton")?.Content is Image {Source: {} icon} && icon.Size.Width>0,"Original app icon asset did not decode.");
                var documentTab=window.FindControl<TabControl>("Documents")!.Items.OfType<TabItem>().Single(item=>ReferenceEquals(item.Tag,tab));
                var tabPoint=global::Avalonia.VisualExtensions.TranslatePoint(documentTab,new global::Avalonia.Point(8,8),window)!.Value;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,tabPoint,global::Avalonia.Input.MouseButton.Right);global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,tabPoint,global::Avalonia.Input.MouseButton.Right);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(documentTab.ContextMenu?.IsOpen==true && documentTab.ContextMenu.Items.OfType<MenuItem>().Any(item=>ReferenceEquals(item.Command,main.CloseEditorDocumentCommand) && ReferenceEquals(item.CommandParameter,tab)),"Document tab right menu did not open with the clicked document as its target.");
                documentTab.ContextMenu!.Close();
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
                report.Passed.Add("Original app icon decodes; View is a top-level sibling of File; tab right click opens commands targeting its own document");
                var toolChrome=window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.ToolChromeControl>().First(control=>control.IsEffectivelyVisible);
                var toolGrip=toolChrome.GetVisualDescendants().OfType<Control>().Single(control=>control.Name=="PART_Grip");
                var chromePoint=global::Avalonia.VisualExtensions.TranslatePoint(toolGrip,new global::Avalonia.Point(20,toolGrip.Bounds.Height/2),window)!.Value;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,chromePoint,global::Avalonia.Input.MouseButton.Right);global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,chromePoint,global::Avalonia.Input.MouseButton.Right);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(toolChrome.ToolFlyout?.IsOpen==true,$"Tool header right click did not open the native docking menu: chrome={toolChrome.Bounds}, point={chromePoint}, hit={window.InputHitTest(chromePoint)?.GetType().Name}, flyout={toolChrome.ToolFlyout?.GetType().Name}.");
                toolChrome.ToolFlyout!.Hide();
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
                report.Passed.Add("Tool header right click opens the native docking menu without dragging the pane");
                await CheckToolTabMenuAsync(window, report, Assert);
                await CheckLayerMenuAsync(window, report, Assert, output);
                var dialogService = services.GetRequiredService<IDialogService>();
                var host = window.FindControl<DialogHostAvalonia.DialogHost>("RootDialog")!;
                editorView.Canvas.Focus(); var originalFocus = window.FocusManager!.GetFocusedElement();
                var input = new TextBox { Text = "invalid" };
                var form = DialogService.Show("Material dialog", input, ("OK", true, (Func<bool>)(() => input.Text == "valid")), ("Cancel", false, null));
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                window.UpdateLayout();
                Assert(host.IsOpen && !editorView.Canvas.IsEffectivelyEnabled && input.IsEffectivelyEnabled, $"Dialog overlay did not disable only its underlay: open={host.IsOpen}, canvasEnabled={editorView.Canvas.IsEffectivelyEnabled}, canvasVisible={editorView.Canvas.IsEffectivelyVisible}, canvasAttached={TopLevel.GetTopLevel(editorView.Canvas) is not null}, inputEnabled={input.IsEffectivelyEnabled}.");
                Assert(ReferenceEquals(window.FocusManager.GetFocusedElement(), input), "Dialog did not focus its first input.");
                Assert(input.Bounds.Height <= 36 && window.FindControl<TabControl>("Documents")!.GetVisualDescendants().OfType<TabItem>().All(item => item.Bounds.Width < 400), "Material density overrides did not reach the control templates.");
                input.IsReadOnly=true;
                var textPoint=global::Avalonia.VisualExtensions.TranslatePoint(input,new global::Avalonia.Point(input.Bounds.Width/2,input.Bounds.Height/2),window)!.Value;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,textPoint,global::Avalonia.Input.MouseButton.Right);global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,textPoint,global::Avalonia.Input.MouseButton.Right);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(input.ContextFlyout is MenuFlyout {IsOpen:true},"Text input right click did not open its library edit menu.");
                var textMenu=(MenuFlyout)input.ContextFlyout!;
                var textItems=textMenu.Items.OfType<MenuItem>().ToArray();
                var cut=textItems.Single(item=>item.InputGesture?.Key==global::Avalonia.Input.Key.X);
                var paste=textItems.Single(item=>item.InputGesture?.Key==global::Avalonia.Input.Key.V);
                var selectAll=textItems.Single(item=>item.InputGesture?.Key==global::Avalonia.Input.Key.A);
                Assert(!cut.IsEnabled && !paste.IsEnabled,"Read-only text input allowed Cut/Paste through the context menu.");
                selectAll.Command!.Execute(null);
                Assert(input.SelectedText==input.Text && input.Text=="invalid","Select All failed on a read-only input or modified its text.");
                textMenu.Hide();input.IsReadOnly=false;input.Focus();
                report.Passed.Add("Text input right click opens editing commands; read-only Cut/Paste are disabled and Select All works without modifying text");
                var modalTheme = global::Avalonia.Application.Current.RequestedThemeVariant;
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    global::Avalonia.Application.Current.RequestedThemeVariant = theme; await Task.Delay(600); window.UpdateLayout();
                    using var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                    Assert(frame is not null, "Dialog overlay failed to render.");
                    frame!.Save(Path.Combine(Path.GetDirectoryName(output)!, theme == ThemeVariant.Light ? "dialog-light.png" : "dialog-dark.png"));
                }
                global::Avalonia.Application.Current.RequestedThemeVariant = modalTheme;
                report.Passed.Add("Compact Material input and tab templates render modal overlays in light and dark themes");
                void DialogKey(global::Avalonia.Input.Key key, global::Avalonia.Input.RawInputModifiers modifiers = global::Avalonia.Input.RawInputModifiers.None)
                {
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, key, modifiers, global::Avalonia.Input.PhysicalKey.None, null);
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window, key, modifiers, global::Avalonia.Input.PhysicalKey.None, null);
                }
                var documentCount = main.LayoutService.Documents.Count();
                DialogKey(global::Avalonia.Input.Key.N, global::Avalonia.Input.RawInputModifiers.Control);
                Assert(main.LayoutService.Documents.Count() == documentCount, "Ctrl+N escaped the modal dialog.");
                DialogKey(global::Avalonia.Input.Key.Enter);
                Assert(!form.IsCompleted && host.IsOpen, "Enter accepted an invalid modal form.");
                for (var i = 0; i < 8; i++) DialogKey(global::Avalonia.Input.Key.Tab);
                Assert(window.FocusManager.GetFocusedElement() is Control focused && focused.GetVisualAncestors().Contains(host), "Tab escaped the modal overlay.");
                input.Focus(); input.Text = "valid"; DialogKey(global::Avalonia.Input.Key.Enter);
                Assert(await form && !host.IsOpen && editorView.Canvas.IsEffectivelyEnabled, "Valid modal form failed to close or restore its underlay.");
                Assert(ReferenceEquals(window.FocusManager.GetFocusedElement(), originalFocus), "Dialog did not restore original keyboard focus.");
                report.Passed.Add("Routed modal keys reject invalid input, accept valid input, isolate CAD shortcuts, cycle Tab and restore focus");
                var unsaved = dialogService.ShowUnsavedDocumentDialogAsync("Unsaved synthetic drawing");
                var replacement = dialogService.ShowOrReplaceMessageDialogWithCancelAsync("Replacement", "Dialog replacement");
                Assert(await unsaved == UnsavedDocumentDialogResult.Cancel, "Replacing a save dialog defaulted to Save.");
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                DialogKey(global::Avalonia.Input.Key.Escape);
                Assert(!await replacement && !host.IsOpen, "Escape did not cancel the replacement dialog.");
                report.Passed.Add("Replacing unsaved confirmation returns Cancel; Escape cancels in-window messages");
                var progress = dialogService.ShowProgressBarDialog();
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(host.IsOpen && !editorView.Canvas.IsEffectivelyEnabled, "Progress did not cover the main window.");
                var afterProgress = dialogService.ShowOrReplaceMessageDialogWithCancelAsync("A new dialog survives disposal of old progress", "Progress replacement");
                progress.Dispose();
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(host.IsOpen && !afterProgress.IsCompleted, "Old progress disposal closed a newer dialog.");
                host.CurrentSession!.Close(false); await afterProgress;
                using (var ownProgress = dialogService.ShowProgressBarDialog())
                    await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(!host.IsOpen, "Progress disposal did not close its own overlay.");
                report.Passed.Add("Progress overlays disable the underlay and dispose only their own session");
                var nestedSettingsModel = new Direct2dCad.ViewModels.Settings.UserSettings.UserSettingsViewModel(Direct2dCad.Client.Common.Settings.CadUserSettings.CreateDefault(), services.GetRequiredService<IUserSettingsStore>(), _ => { });
                dialogService.ShowUserSettingsDialog(nestedSettingsModel);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                var settingsHost = DialogService.Host(ViewServiceIdentifiers.DocumentSettingsDialogHost);
                Assert(!ReferenceEquals(settingsHost, host) && TopLevel.GetTopLevel(settingsHost) is Window, "Settings modal did not get its own DialogHost.");
                var settingsWindow = (Window)TopLevel.GetTopLevel(settingsHost)!; settingsWindow.UpdateLayout();
                var nestedMessage = dialogService.ShowOrReplaceMessageDialogWithCancelAsync("Nested settings message", "Settings", ViewServiceIdentifiers.DocumentSettingsDialogHost);
                Assert(settingsHost.IsOpen && !host.IsOpen, "Settings message covered the wrong window.");
                settingsWindow.Close();
                Assert(!await nestedMessage && !settingsHost.IsOpen, "Closing settings left a pending dialog session or accepted it.");
                report.Passed.Add("Settings messages use the settings overlay; closing its window cancels the pending session");
            }
            var rename = new Controls.CadEditableText { Text = "Original" };
            var renameHost = new Window { Content = rename, Width = 260, Height = 100 };
            try
            {
                renameHost.Show(window); renameHost.UpdateLayout();
                Assert(rename.BeginEdit(), "Rename did not enter editing mode.");
                var inputBox = rename.GetVisualDescendants().OfType<TextBox>().Single(); inputBox.Text = "Renamed"; rename.EndEdit(true);
                Assert(rename.Text == "Renamed", "Rename did not commit the new name.");
                rename.BeginEdit(); inputBox.Text = "Cancelled"; rename.EndEdit(false); Assert(rename.Text == "Renamed", "Escape/cancel changed the name.");
                rename.BeginEdit(); inputBox.Text = " "; rename.EndEdit(true); Assert(rename.Text == "Renamed", "Blank name changed the model."); rename.EndEdit(false);
                rename.IsReadOnly = true; Assert(!rename.BeginEdit(), "Model-space/read-only name entered editing mode.");
            }
            finally { renameHost.Close(); }
            report.Passed.Add("Inline rename commits, cancels, rejects blank names and respects read-only model tabs");
            var messageFilter = CadMessageFilterConverter.Instance;
            var warningLevel = Direct2dCad.ViewModels.Services.Platform.Notifications.CadMessageLevel.Warning;
            var warningOption = messageFilter.Convert(warningLevel, typeof(object), null, System.Globalization.CultureInfo.CurrentCulture);
            Assert(Equals(messageFilter.ConvertBack(warningOption, typeof(object), null, System.Globalization.CultureInfo.CurrentCulture), warningLevel) && messageFilter.ConvertBack(CadMessageFilterConverter.Options[0], typeof(object), null, System.Globalization.CultureInfo.CurrentCulture) is null, "Message filter cannot return from a severity to All.");
            var userSettings = new Direct2dCad.ViewModels.Settings.UserSettings.UserSettingsViewModel(Direct2dCad.Client.Common.Settings.CadUserSettings.CreateDefault(), services.GetRequiredService<IUserSettingsStore>(), _ => { });
            var paritySettingsView = Views.SettingsWorkspace.Create(userSettings);
            userSettings.SelectedSection = userSettings.Rendering;
            Assert(paritySettingsView.GetLogicalDescendants().OfType<ContentControl>().Any(control => control.Content is Control view && ReferenceEquals(view.DataContext, userSettings.Rendering)), "Settings navigation did not switch to the typed rendering page.");
            Assert(userSettings.TryApply(), "Settings Apply did not validate and persist the working copy.");
            report.Passed.Add("Message severity and All roundtrip; settings category navigation and Apply persist a valid working copy");
            IEnumerable<MenuItem> MenuItems(System.Collections.IEnumerable items)
            {
                foreach (var item in items.OfType<MenuItem>()) { yield return item; foreach (var nested in MenuItems(item.Items)) yield return nested; }
            }
            var canvasMenu = MenuItems(editorView.Canvas.ContextMenu!.Items).ToArray();
            Assert(canvasMenu.Count(item => item.Command is not null) == Views.Generated.EditorContextMenu.SourceCommandCount, "The full compiled editor context menu did not resolve all WPF command bindings.");
            Assert(canvasMenu.Any(item => ReferenceEquals(item.Command, tab.CopySelectedEntitiesCommand)) && canvasMenu.Any(item => ReferenceEquals(item.Command, tab.SaveAsFileCommand)), "Editor context menu lost clipboard or Save As commands.");
            var circleMenu = canvasMenu.Single(item => (string?)item.CommandParameter == "CircleCenterRadius"); circleMenu.Command!.Execute(circleMenu.CommandParameter);
            Assert(vm.CadCanvasToolMode == CadCanvasToolMode.CircleCenterRadius, "Context menu did not activate the circle tool."); vm.SetToolMode(CadCanvasToolMode.Select);
            report.Passed.Add("Full typed editor context menu resolves clipboard, file and drawing commands");
            vm.SetToolMode(CadCanvasToolMode.CircleCenterRadius);
            var center = vm.CadEditor.Viewport.WorldToScreen(new CadPointD(0, 0));
            vm.PointerDown(center, CadCanvasPointerButton.Left, false); vm.PointerUp(center, CadCanvasPointerButton.Left);
            vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new CadPointD(20, 0))); vm.PointerLeave();
            Assert(vm.DynamicInputFields.Count == 1, "Circle radius input is missing after pointer leave.");
            vm.DynamicInputFields[0].Text = "12";
            Assert(vm.SubmitDynamicInput(), "Numeric circle input failed.");
            Assert(document.Entities.Values.OfType<CadCircle>().Any(c => !c.IsErased && Math.Abs(c.Radius - 12) < 1e-8), "Numeric input did not commit the requested radius.");
            vm.Escape(); vm.Undo(); Assert(!document.Entities.Values.OfType<CadCircle>().Any(c => !c.IsErased), "Undo did not erase the circle."); vm.Redo();
            report.Passed.Add("Numeric circle, pointer leave, undo and redo");
            if (report.HeadlessWindowing)
            {
                var input = global::Avalonia.Input.RawInputModifiers.None;
                void Key(global::Avalonia.Input.Key key, global::Avalonia.Input.RawInputModifiers modifiers)
                {
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, key, modifiers, global::Avalonia.Input.PhysicalKey.None, null);
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window, key, modifiers, global::Avalonia.Input.PhysicalKey.None, null);
                }
                vm.SetToolMode(CadCanvasToolMode.CircleCenterRadius);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                var uiCenter = vm.CadEditor.Viewport.WorldToScreen(new CadPointD(-30, 0));
                var centerInWindow = global::Avalonia.VisualExtensions.TranslatePoint(editorView.Canvas, new global::Avalonia.Point(uiCenter.X, uiCenter.Y), window)!.Value;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, centerInWindow);
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, centerInWindow, global::Avalonia.Input.MouseButton.Left);
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, centerInWindow, global::Avalonia.Input.MouseButton.Left);
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, centerInWindow + new global::Avalonia.Vector(20, 0));
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new global::Avalonia.Point(5, 5));
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(vm.DynamicInputFields.Count == 1, "Routed pointer leave lost the dynamic radius field.");
                Key(global::Avalonia.Input.Key.Tab, input);
                global::Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(window, "18");
                Key(global::Avalonia.Input.Key.Enter, input);
                Assert(document.Entities.Values.OfType<CadCircle>().Any(circle => !circle.IsErased && circle.Radius == 18), "Routed Tab/text/Enter input did not create the requested circle.");
                Key(global::Avalonia.Input.Key.Escape, input);
                Key(global::Avalonia.Input.Key.Z, global::Avalonia.Input.RawInputModifiers.Control);
                Assert(!document.Entities.Values.OfType<CadCircle>().Any(circle => !circle.IsErased && circle.Radius == 18), "Routed Ctrl+Z did not undo the circle.");
                Key(global::Avalonia.Input.Key.Z, global::Avalonia.Input.RawInputModifiers.Control | global::Avalonia.Input.RawInputModifiers.Shift);
                Assert(document.Entities.Values.OfType<CadCircle>().Any(circle => !circle.IsErased && circle.Radius == 18), "Routed Ctrl+Shift+Z did not redo the circle.");
                report.Passed.Add("Headless routed canvas input: mouse, pointer leave, Tab, text, Enter, Escape, undo and redo");

                vm.SetToolMode(CadCanvasToolMode.Rectangle);
                var rectangleStart = vm.CadEditor.Viewport.WorldToScreen(new CadPointD(0,0));
                vm.PointerDown(rectangleStart,CadCanvasPointerButton.Left,false);vm.PointerUp(rectangleStart,CadCanvasPointerButton.Left);
                vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new CadPointD(30,20)));
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },global::Avalonia.Threading.DispatcherPriority.Background);
                window.UpdateLayout();
                var rectangleFields=editorView.FindControl<ItemsControl>("DynamicFields")!;
                Assert(vm.DynamicInputFields.Count==2 && editorView.FindControl<global::Avalonia.Controls.Shapes.Path>("DynamicGuides")!.Data is not null,"Rectangle width/height construction guides were not realized.");
                var widthField=rectangleFields.ContainerFromIndex(0)!;var heightField=rectangleFields.ContainerFromIndex(1)!;
                Assert(global::Avalonia.Controls.Canvas.GetLeft(widthField)!=global::Avalonia.Controls.Canvas.GetLeft(heightField)||global::Avalonia.Controls.Canvas.GetTop(widthField)!=global::Avalonia.Controls.Canvas.GetTop(heightField),"Rectangle width and height inputs overlap at the same position.");
                vm.Escape();vm.SetToolMode(CadCanvasToolMode.Select);
                report.Passed.Add("Rectangle width and height inputs realize separate construction-line positions and settle without a layout loop");
                var panStart = global::Avalonia.VisualExtensions.TranslatePoint(editorView.Canvas,new global::Avalonia.Point(180,180),window)!.Value;
                var panEnd = panStart + new global::Avalonia.Vector(75,40);
                var panScale = vm.CadEditor.Viewport.Zoom;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,panStart,global::Avalonia.Input.MouseButton.Right);
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window,panEnd,global::Avalonia.Input.RawInputModifiers.RightMouseButton);
                Assert(vm.IsPanning,$"Right drag did not start pan: canvas={editorView.Canvas.Bounds}, point={panStart}.");
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,panEnd,global::Avalonia.Input.MouseButton.Right);
                Assert(!vm.IsPanning && editorView.Canvas.ContextMenu?.IsOpen != true,$"Right drag left panning or a context menu active: pan={vm.IsPanning}, menu={editorView.Canvas.ContextMenu?.IsOpen}.");
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseWheel(window,panEnd,new global::Avalonia.Vector(0,1));
                Assert(vm.CadEditor.Viewport.Zoom>panScale,"Wheel zoom stopped after right-button pan.");
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,panEnd,global::Avalonia.Input.MouseButton.Right);
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,panEnd,global::Avalonia.Input.MouseButton.Right);
                Assert(editorView.Canvas.ContextMenu?.IsOpen==true,"A fresh right click after pan no longer opens the context menu.");
                editorView.Canvas.ContextMenu!.Close();
                report.Passed.Add("Right-button pan releases capture and preserves immediate wheel zoom without opening the context menu");
                await CheckHandledCanvasReleaseAsync(window,editorView,report,Assert,output);
                await CheckChoiceControlsAsync(window,report,Assert,output);
                vm.SetToolMode(CadCanvasToolMode.Select);
                var radialEnabled = vm.UserSettings.Interaction.RadialMenu.IsEnabled;
                vm.UserSettings.Interaction.RadialMenu.IsEnabled = true;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, centerInWindow, global::Avalonia.Input.MouseButton.Middle);
                Assert(editorView.Canvas.IsRadialMenuActive, "Routed middle press did not open the radial menu.");
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);
                window.UpdateLayout();
                Assert(editorView.Canvas.RadialUsesOwnerWindow,"Radial menu created a separate opaque popup window instead of a transparent owner-window overlay.");
                await Task.Delay(120);
                using(var radialFrame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))radialFrame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"radial-overlay.png"));
                global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, global::Avalonia.Input.Key.LeftShift, global::Avalonia.Input.RawInputModifiers.Shift, global::Avalonia.Input.PhysicalKey.None, null);
                Assert(editorView.Canvas.RadialGesture == Direct2dCad.Client.Common.Settings.CadRadialMenuGesture.ShiftMiddle, "Shift did not switch the radial profile.");
                global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window, global::Avalonia.Input.Key.LeftShift, input, global::Avalonia.Input.PhysicalKey.None, null);
                Assert(editorView.Canvas.RadialGesture == Direct2dCad.Client.Common.Settings.CadRadialMenuGesture.Middle, "Releasing Shift left the radial profile stuck.");
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, centerInWindow, global::Avalonia.Input.MouseButton.Middle);
                Assert(!editorView.Canvas.IsRadialMenuActive, "Middle release did not close the radial menu.");
                vm.UserSettings.Interaction.RadialMenu.IsEnabled = radialEnabled;
                report.Passed.Add("Routed radial menu restores the middle profile after modifier release");
            }

            var originalTheme = global::Avalonia.Application.Current!.RequestedThemeVariant;
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                global::Avalonia.Application.Current.RequestedThemeVariant = theme;
                foreach (var mode in Enum.GetValues<CadCanvasToolMode>())
                {
                    vm.SetToolMode(mode); var model = main.EntityProperties.Entity;
                    if (model is null) continue;
                    var view = KnownViews.Create(model); Assert(view is not null, $"Missing property view for {model.GetType().Name}.");
                    view!.Measure(new global::Avalonia.Size(300, 800)); view.Arrange(new global::Avalonia.Rect(0, 0, 300, 800)); report.PropertyViews++;
                }
            }
            global::Avalonia.Application.Current.RequestedThemeVariant = originalTheme; vm.SetToolMode(CadCanvasToolMode.Select);
            report.Passed.Add("Drawing property views instantiate in light and dark themes");

            var workspace = services.GetRequiredService<ICadToolWorkspace>(); var executor = new CadWorkspaceToolExecutor(workspace);
            report.ToolSchemas = CadWorkspaceToolExecutor.ToolDefinitions.Count;
            Assert(report.ToolSchemas > 50 && CadWorkspaceToolExecutor.ToolDefinitions.All(t => t.Parameters.ValueKind == JsonValueKind.Object && t.Parameters.TryGetProperty("properties", out _)), "Native tool schemas lost their property metadata.");
            async Task<JsonElement> Execute(string name, string args)
            {
                using var result = JsonDocument.Parse(await executor.ExecuteAsync(new(Guid.NewGuid().ToString("N"), name, args), CancellationToken.None));
                Assert(result.RootElement.GetProperty("success").GetBoolean(), name + ": " + result.RootElement.GetRawText()); return result.RootElement.Clone();
            }
            await Execute("add_line", "{\"x1\":0,\"y1\":0,\"x2\":30,\"y2\":10}");
            await Execute("list_entities", "{\"limit\":20}");
            await Execute("add_dimension", "{\"kind\":\"Aligned\",\"anchors\":[{\"x\":0,\"y\":0},{\"x\":30,\"y\":10}],\"x\":10,\"y\":20}");
            report.Passed.Add("Native AI schemas, editing, querying and dimension creation");

            var polylineId = vm.CadEditor.AddPolyline([new(0, 0), new(10, 0), new(10, 10)], true); vm.SelectEntities([polylineId]);
            Assert(main.EntityProperties.Entity is Direct2dCad.ViewModels.Toolboxes.EntityProperty.PolylinePropertyViewModel, "Polyline property model is missing.");
            var polylineModel = (Direct2dCad.ViewModels.Toolboxes.EntityProperty.PolylinePropertyViewModel)main.EntityProperties.Entity!;
            var polylineView = KnownViews.Create(polylineModel)!;
            // Controls must be attached before the DataGrid can realize rows and editing templates.
            // With --smoke-test-headless this is a managed test window, without an OS window.
            var propertyWindow = new Window { Content = polylineView, Width = 400, Height = 900 };
            try
            {
                propertyWindow.Show(window);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                propertyWindow.UpdateLayout();
                var grid = polylineView.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault();
                Assert(grid is not null, "Attached polyline property form did not materialize its DataGrid.");
                Assert(grid!.Columns.Count == 3 && grid.Columns.OfType<DataGridTextColumn>().All(c => c.Binding is global::Avalonia.Data.CompiledBinding), "DataGrid columns are not compiled bindings.");
                Assert(grid.ItemsSource is System.Collections.IEnumerable rows && rows.Cast<object>().Count() == 3, "Native DataGrid did not bind the vertices.");
                grid.SelectedItem = polylineModel.Vertices[0]; grid.CurrentColumn = grid.Columns[1];
                grid.ScrollIntoView(grid.SelectedItem, grid.CurrentColumn); grid.UpdateLayout(); grid.Focus();
                Assert(grid.BeginEdit(), "Native DataGrid could not enter cell editing.");
                var cell = grid.GetVisualDescendants().OfType<TextBox>().Single();
                Assert(cell.Text == "0", "Compiled grid column did not read its row value.");
                cell.Text = "3";
                Assert(grid.CommitEdit(DataGridEditingUnit.Row, true), "Native DataGrid could not commit the edited row.");
                Assert(((CadPolyline)document.Entities[polylineId]).Points[0].X == 3, "Compiled vertex column did not update the drawing.");
                grid.CollectionView.SortDescriptions.Add(global::Avalonia.Collections.DataGridSortDescription.FromPath("X", System.ComponentModel.ListSortDirection.Descending));
                Assert(grid.CollectionView.Cast<Direct2dCad.ViewModels.Toolboxes.EntityProperty.PolylineVertexPropertyViewModel>().First().X == 10 && ((CadPolyline)document.Entities[polylineId]).Points[0].X == 3, "Native grid sorting lost property metadata or changed geometry order.");
                grid.CollectionView.SortDescriptions.Clear();
                grid.SelectedItem = polylineModel.Vertices[0]; grid.CurrentColumn = grid.Columns[1]; grid.UpdateLayout();
                Assert(grid.BeginEdit(), "Native grid could not reopen the edited cell.");
                grid.GetVisualDescendants().OfType<TextBox>().Single().Text = "NaN";
                Assert(!grid.CommitEdit(DataGridEditingUnit.Row, true) && ((CadPolyline)document.Entities[polylineId]).Points[0].X == 3, "Invalid grid input was accepted or changed the drawing.");
                grid.CancelEdit(DataGridEditingUnit.Row);
                grid.CurrentColumn = grid.Columns[0]; Assert(!grid.BeginEdit(), "Vertex index column is editable.");
            }
            finally { propertyWindow.Close(); }
            vm.Undo(); Assert(((CadPolyline)document.Entities[polylineId]).Points[0].X == 0, "Vertex editing is not undoable."); vm.ClearSelection();
            report.Passed.Add("Official DataGrid compiled columns, vertex editing, sorting, invalid input and undo");

            var splineId = vm.CadEditor.AddSpline([new(0, 0), new(10, 5), new(20, 0)]); vm.SelectEntities([splineId]);
            Assert(main.EntityProperties.Entity is Direct2dCad.ViewModels.Toolboxes.EntityProperty.SplinePropertyViewModel, "Spline property model is missing.");
            var splineModel = (Direct2dCad.ViewModels.Toolboxes.EntityProperty.SplinePropertyViewModel)main.EntityProperties.Entity!;
            var splineWindow = new Window { Content = KnownViews.Create(splineModel), Width = 400, Height = 900 };
            try
            {
                splineWindow.Show(window);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                splineWindow.UpdateLayout();
                var grid = splineWindow.GetVisualDescendants().OfType<DataGrid>().Single();
                grid.SelectedItem = splineModel.FitPoints[1]; grid.CurrentColumn = grid.Columns[2];
                grid.ScrollIntoView(grid.SelectedItem, grid.CurrentColumn); grid.UpdateLayout(); grid.Focus();
                Assert(grid.BeginEdit(), "Native spline grid could not enter cell editing.");
                grid.GetVisualDescendants().OfType<TextBox>().Single().Text = "-2.5";
                Assert(grid.CommitEdit(DataGridEditingUnit.Row, true) && ((CadSpline)document.Entities[splineId]).FitPoints[1].Y == -2.5, "Spline grid did not commit the fit point.");
            }
            finally { splineWindow.Close(); }
            vm.Undo(); Assert(((CadSpline)document.Entities[splineId]).FitPoints[1].Y == 5, "Spline grid editing is not undoable."); vm.ClearSelection();
            var numberConverter = CadGridNumberConverter.Instance;
            Assert(Equals(numberConverter.ConvertBack("1,5", typeof(double), null, System.Globalization.CultureInfo.GetCultureInfo("de-DE")), 1.5), "Grid numeric input ignored its culture.");
            report.Passed.Add("Spline DataGrid fit point editing, undo and culture-aware numeric conversion");

            var storage = new CadDocumentStorage(); var path = Path.Combine(Path.GetDirectoryName(output)!, "smoke.d2cad");
            await storage.SaveAsync(document, path); var loaded = await storage.LoadAsync(path);
            Assert(loaded.Entities.Values.OfType<CadCircle>().Any(c => !c.IsErased && Math.Abs(c.Radius - 12) < 1e-8), "Native compressed drawing roundtrip lost circle geometry.");
            Assert(loaded.Entities.Values.OfType<CadDimension>().Any(), "Native drawing roundtrip lost dimensions."); report.Passed.Add("Native compressed file and generated dimension JSON roundtrip");
            var dxf = Path.Combine(Path.GetDirectoryName(output)!, "smoke.dxf");
            var dxfStorage = new Direct2dCad.IO.Dxf.CadDxfStorage();
            Assert(dxfStorage.AnalyzeExport(document).Count > 0, "Dimension export should report its representation loss.");
            var exchange = Direct2dCad.Db.Cad.CadDocument.Create("Native DXF fixture"); exchange.DocumentSettings.SetUnit(Direct2dCad.Db.Cad.Settings.CadUnit.Millimeter); exchange.AddCircle(new CadPointD(0, 0), 12);
            Assert(dxfStorage.AnalyzeExport(exchange).Count == 0, "Circle fixture has unexpected DXF losses."); await dxfStorage.ExportAsync(exchange, dxf);
            var imported = await dxfStorage.ImportAsync(dxf);
            Assert(imported.Document.Entities.Values.OfType<CadCircle>().Any(c => !c.IsErased && Math.Abs(c.Radius - 12) < 1e-8), "DXF roundtrip lost circle geometry."); report.Passed.Add("Native DXF export and import");
            var ole = (OleHostService)services.GetRequiredService<IOleHostService>(); var fixture = ole.CreateSmokeFixture();
            var envelope = OlePayload.Decode(fixture.OleBytes); Assert(envelope.Encode().AsSpan().SequenceEqual(fixture.OleBytes), "OLE envelope roundtrip differs from WPF format.");
            var oleSession = Guid.NewGuid(); var renderId = Guid.NewGuid();
            try { var oleFrame = ole.DrawOleObject(oleSession, new(null, renderId, fixture.OleBytes, 64, 64, 0, 0, 64, 64)); Assert(oleFrame is not null && oleFrame.Pixels.Where((_, i) => i % 4 != 3).Any(b => b < 200), "Native OLE fixture did not draw colored pixels."); }
            finally { ole.ReleaseTransientRenderSession(oleSession, renderId); }
            report.Passed.Add("WPF OLE storage envelope, native save/load and rendering");
            Direct2dCad.Windows.Interop.InteropSelfChecks.VerifyOleCallbacks();
            report.Passed.Add("CsWin32 callback interfaces share IUnknown identity, dispatch update/save events, map exceptions and ignore callbacks after disposal");
            var preciseBounds = CadRectD.FromXYWH(-154.35000000000002, 17.20630351343372, 126.69999999999999, 40.58739297313255);
            var oleEntity = vm.CadEditor.AddOleObject(preciseBounds, fixture.OleBytes); vm.SelectEntities([oleEntity]);
            var olePath=Path.Combine(Path.GetDirectoryName(output)!,"ole-roundtrip.d2cad");
            await storage.SaveAsync(document,olePath);
            var oleReload=await storage.LoadAsync(olePath);
            Assert(oleReload.GetEntity(oleEntity) is CadOleObject reloadedOle && reloadedOle.CopyOleBytes().AsSpan().SequenceEqual(fixture.OleBytes),"Saved drawing changed or lost its OLE storage envelope.");
            report.Passed.Add("C# OLE storage remains byte-identical after saving and reloading a CAD drawing");
            var olePropertyView = KnownViews.Create(main.EntityProperties.Entity);
            Assert(olePropertyView is Views.Generated.OleObjectPropertyView, "OLE entity has no typed property form.");
            var numericWindow = new Window { Content = olePropertyView, Width = 400, Height = 900 };
            try
            {
                numericWindow.Show(window);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                numericWindow.UpdateLayout();
                Assert(((CadOleObject)document.Entities[oleEntity]).Bounds == preciseBounds, "Displaying rounded properties changed the original OLE geometry.");
                var widthBox = olePropertyView!.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Single(box => box.Value is double value && value == preciseBounds.Width);
                Assert(widthBox.Text == "126.7", "Property display did not format the number.");
                widthBox.Text = "30.25";
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                Assert(((CadOleObject)document.Entities[oleEntity]).Bounds.Width == 30.25, "Typed numeric property did not update the geometry.");
                widthBox.Text = "NaN";
                Assert(((CadOleObject)document.Entities[oleEntity]).Bounds.Width == 30.25, "Invalid numeric property changed the geometry.");
                vm.Undo();
                Assert(((CadOleObject)document.Entities[oleEntity]).Bounds == preciseBounds, "Numeric property editing did not preserve exact undo geometry.");
            }
            finally { numericWindow.Close(); }
            report.Passed.Add("Formatted numeric properties preserve exact geometry on display, reject NaN, edit and undo");
            var circlePair = document.Entities.Single(pair => pair.Value is CadCircle { IsErased: false, Radius: 12 });
            var inspector=window.ToolView(main.EntityProperties)!;
            vm.ClearSelection();
            Assert(inspector.GetVisualDescendants().OfType<TextBlock>().Single(t=>global::Avalonia.Automation.AutomationProperties.GetAutomationId(t)=="EntityPropertiesEmptyHint").IsVisible==false,"Active drawing with no selection incorrectly shows the no-document hint.");
            var dimensionId=document.Entities.First(pair=>pair.Value is CadDimension {IsErased:false}).Key;
            vm.SelectEntities([dimensionId]);window.NativeDock.Select(main.EntityProperties);window.UpdateLayout();
            var detachButton=inspector.GetVisualDescendants().OfType<Button>().Single(b=>global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)=="DetachDimensionButton");
            Assert(detachButton.IsVisible && detachButton.Parent is Control {IsVisible:true} && ReferenceEquals(detachButton.Command,vm.DetachDimensionCommand),"Selected dimension lost its association actions.");
            Assert(inspector.GetVisualDescendants().OfType<Views.Generated.CommonEntityPropertyView>().Any() && inspector.GetVisualDescendants().OfType<Views.Generated.CadAnnotationParametersView>().Any(),"Dimension parameters replaced the common entity property controls.");
            Assert(CadIconData.Get("LinkVariant") is not null && CadIconData.Get("LinkVariantOff") is not null,"Dimension action icons are missing.");
            report.Passed.Add("Property inspector keeps dimension common properties and association actions; no-document hint only appears without a drawing");
            vm.SelectEntities([circlePair.Key]);
            var circleProperties = (Direct2dCad.ViewModels.Toolboxes.EntityProperty.CirclePropertyViewModel)main.EntityProperties.Entity!;
            var groupedView = KnownViews.Create(circleProperties)!;
            var groupedWindow = new Window { Content = groupedView, Width = 400, Height = 900 };
            try
            {
                groupedWindow.Show(window);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                groupedWindow.UpdateLayout();
                var sections = groupedView.GetLogicalDescendants().OfType<Expander>().ToArray();
                var advanced = sections.Single(section => global::Avalonia.Automation.AutomationProperties.GetAutomationId(section) == "EntityAdvancedSettings");
                var fill = sections.Single(section => global::Avalonia.Automation.AutomationProperties.GetAutomationId(section) == "EntityFillSettings");
                Assert(!advanced.IsExpanded && !fill.IsExpanded && sections.Count(section => section.IsExpanded) == 1, "Property groups lost WPF expansion defaults.");
                Assert(advanced.GetLogicalDescendants().OfType<Controls.CadPropertyNumberBox>().Any(box => box.IsReadOnly && box.Value is double value && value == 24), "Circle information is outside the Advanced group.");
                var radiusBox = groupedView.GetLogicalDescendants().OfType<Controls.CadPropertyNumberBox>().Single(box => box.ShowStepper && box.Value is double value && value == 12);
                if(report.HeadlessWindowing)
                {
                    var layerChoice=groupedView.GetVisualDescendants().OfType<ComboBox>().Single(box=>global::Avalonia.Automation.AutomationProperties.GetAutomationId(box)=="EntityLayerSelector");
                    var headers=groupedView.GetVisualDescendants().OfType<ToggleButton>().Where(button=>button.Name=="PART_ToggleButton" && button.IsEffectivelyVisible).ToArray();
                    Assert(radiusBox.Bounds.Height<=30 && layerChoice.Bounds.Height<=30 && headers.Length>=3 && headers.All(header=>header.Bounds.Height<=30),$"Property inspector is not compact: radius={radiusBox.Bounds}, layer={layerChoice.Bounds}, headers={string.Join(',',headers.Select(header=>header.Bounds.Height))}.");
                    foreach(var header in headers)
                    {
                        var caption=header.GetVisualDescendants().OfType<TextBlock>().Single(block=>Equals(block.Text,header.Content));
                        var captionPoint=caption.TranslatePoint(default,header)!.Value;
                        Assert(caption.FontSize<=12 && captionPoint.Y>=0 && captionPoint.Y+caption.Bounds.Height<=header.Bounds.Height+1,$"Compact property header clips its caption: text={caption.Text}, font={caption.FontSize}, top={captionPoint.Y}, caption={caption.Bounds}, header={header.Bounds}.");
                    }
                    using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(groupedWindow))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"property-compact.png"));
                    report.Passed.Add("Compact WPF-like property rows and flat section headers retain library templates and WPF expansion defaults");
                }
                Assert(radiusBox.Text == 12d.ToString("F3", System.Globalization.CultureInfo.CurrentCulture) && ((CadCircle)document.Entities[circlePair.Key]).Radius == 12, "F3 display changed circle geometry.");
                radiusBox.Step(1);
                Assert(((CadCircle)document.Entities[circlePair.Key]).Radius == 13, "Numeric stepper did not execute the property command.");
                if (report.HeadlessWindowing)
                {
                    await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                    window.UpdateLayout();
                    var dockedRadius = window.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().SingleOrDefault(box => box.DataContext is Direct2dCad.ViewModels.Toolboxes.EntityProperty.CirclePropertyViewModel && box.Value is double radius && radius == 13);
                    Assert(dockedRadius is not null, "Main-window Circle radius input was not materialized after the property command. " + string.Join(", ", window.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Select(box => $"{box.DataContext?.GetType().Name}:{box.Value}")));
                    Assert(dockedRadius!.Focus(), "Main-window numeric input could not receive keyboard focus.");
                    dockedRadius.Text = "invalid";
                    void PropertyKey(global::Avalonia.Input.Key key, global::Avalonia.Input.RawInputModifiers modifiers = global::Avalonia.Input.RawInputModifiers.None)
                    {
                        global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, key, modifiers, global::Avalonia.Input.PhysicalKey.None, null);
                        global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window, key, modifiers, global::Avalonia.Input.PhysicalKey.None, null);
                    }
                    PropertyKey(global::Avalonia.Input.Key.Enter);
                    Assert(dockedRadius.IsFocused && DataValidationErrors.GetHasErrors(dockedRadius) && ((CadCircle)document.Entities[circlePair.Key]).Radius == 13, "Invalid property Enter moved focus or changed geometry.");
                    PropertyKey(global::Avalonia.Input.Key.S, global::Avalonia.Input.RawInputModifiers.Control);
                    PropertyKey(global::Avalonia.Input.Key.S, global::Avalonia.Input.RawInputModifiers.Control | global::Avalonia.Input.RawInputModifiers.Shift);
                    PropertyKey(global::Avalonia.Input.Key.P, global::Avalonia.Input.RawInputModifiers.Control);
                    Assert(dockedRadius.IsFocused && dockedRadius.Text == "invalid", "Invalid property file shortcuts did not retain the edit.");
                    PropertyKey(global::Avalonia.Input.Key.Escape);
                    Assert(!DataValidationErrors.GetHasErrors(dockedRadius) && dockedRadius.Text == 13d.ToString("F3", System.Globalization.CultureInfo.CurrentCulture) && editorView.Canvas.IsFocused, "Property Escape did not discard invalid text and return to the canvas.");
                    dockedRadius.Focus(); dockedRadius.Text = "14";
                    PropertyKey(global::Avalonia.Input.Key.Enter);
                    Assert(editorView.Canvas.IsFocused && ((CadCircle)document.Entities[circlePair.Key]).Radius == 14, "Valid property Enter did not commit and return focus.");
                    vm.Undo();
                    Assert(((CadCircle)document.Entities[circlePair.Key]).Radius == 13, "Property edit undo did not restore the previous radius.");
                    dockedRadius.Focus();
                    report.Passed.Add("Routed property Enter validates and commits; Escape discards invalid text; invalid Save, Save As and Print remain in the editor");
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, global::Avalonia.Input.Key.Z, global::Avalonia.Input.RawInputModifiers.Control, global::Avalonia.Input.PhysicalKey.None, null);
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window, global::Avalonia.Input.Key.Z, global::Avalonia.Input.RawInputModifiers.Control, global::Avalonia.Input.PhysicalKey.None, null);
                }
                else vm.Undo();
                Assert(((CadCircle)document.Entities[circlePair.Key]).Radius == 12, "Numeric stepper undo did not restore circle geometry.");
                if (report.HeadlessWindowing)
                {
                    window.ConfigureFloatingShortcuts(groupedWindow);
                    vm.SetToolMode(CadCanvasToolMode.Polyline);
                    foreach (var point in new[] { new CadPointD(50, 50), new CadPointD(80, 50) })
                    {
                        var screen = vm.CadEditor.Viewport.WorldToScreen(point);
                        vm.PointerDown(screen, CadCanvasPointerButton.Left, false); vm.PointerUp(screen, CadCanvasPointerButton.Left);
                    }
                    var entityCount = document.Entities.Values.Count(entity => !entity.IsErased);
                    var pendingAnchor = vm.DynamicInputScreenGeometry.Anchor;
                    Assert(pendingAnchor is not null && radiusBox.Focus(), "Pending polyline or hosted property focus was not available.");
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(groupedWindow, global::Avalonia.Input.Key.Enter, global::Avalonia.Input.RawInputModifiers.None, global::Avalonia.Input.PhysicalKey.None, null);
                    global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(groupedWindow, global::Avalonia.Input.Key.Enter, global::Avalonia.Input.RawInputModifiers.None, global::Avalonia.Input.PhysicalKey.None, null);
                    Assert(document.Entities.Values.Count(entity => !entity.IsErased) == entityCount && vm.DynamicInputScreenGeometry.Anchor == pendingAnchor && editorView.Canvas.IsFocused, "Hosted property Enter completed a pending polyline instead of only returning to the canvas.");
                    vm.Escape(); vm.SetToolMode(CadCanvasToolMode.Select); vm.SelectEntities([circlePair.Key]);
                    report.Passed.Add("Hosted property Enter returns to the main canvas without completing a pending two-point polyline");
                }
                circleProperties.UseByLayerLineWeight = true;
                var appearance = sections.Single(section => section.IsExpanded);
                var readOnlyWeight = appearance.GetLogicalDescendants().OfType<Controls.CadPropertyNumberBox>().Single(box => box.IsReadOnly);
                Assert(readOnlyWeight.IsEffectivelyVisible && readOnlyWeight.Value is double weight && weight == circleProperties.LineWeight, "ByLayer line weight lost its visible read-only value.");
                var colorEditor = appearance.GetLogicalDescendants().OfType<ColorPicker>().Single();
                Assert(colorEditor.IsEffectivelyEnabled == circleProperties.IsExplicitColorSource, "Color source does not control the explicit color editor.");
                var bounded = new Controls.CadPropertyNumberBox { Value = 2, Interval = 1, Minimum = 1, Maximum = 3, ShowStepper = true };
                bounded.Step(1); Assert(bounded.Value is int upper && upper == 3, "Integer stepper lost the integer value type.");
                bounded.Step(1); Assert(bounded.Value is int clamped && clamped == 3, "Stepper exceeded the numeric maximum.");
                bounded.Text = "0"; Assert(bounded.Value is int valid && valid == 3, "Out-of-range typed value changed the model.");
                bounded.IsReadOnly = true; bounded.Step(-1); Assert(bounded.Value is int unchanged && unchanged == 3, "Read-only numeric value changed through stepping.");
            }
            finally { groupedWindow.Close(); }
            report.Passed.Add("WPF property titles, groups, F3 precision, stepper commands and undo, bounds, ByLayer fallback and color-source gating");
            if(report.HeadlessWindowing) await CheckExplicitColorSourceAsync(window,vm,circlePair.Key,report,Assert,output);
            var layerTool = main.LayoutService.Anchorables.OfType<Direct2dCad.ViewModels.Toolboxes.LayersToolboxViewModel>().Single();
            var originalLayerOpen = layerTool.IsOpen;
            var panelToggle = window.FindControl<MenuItem>("PanelsMenu")!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Direct2dCad.Lang.CadUiText.Get("Layers")));
            panelToggle.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert(layerTool.IsOpen != originalLayerOpen && panelToggle.IsChecked == layerTool.IsOpen, "View menu item did not synchronize hidden panel state.");
            panelToggle.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert(layerTool.IsOpen == originalLayerOpen, "View menu item did not restore the panel.");
            report.Passed.Add("View menu item hides and restores the same docked toolbox with synchronized state");
            var blocksTool=main.LayoutService.Anchorables.OfType<Direct2dCad.ViewModels.Toolboxes.BlocksToolboxViewModel>().Single();
            foreach(var tool in new AvalonDock.Core.IToolbox[]{blocksTool,layerTool,blocksTool,layerTool})
            {
                window.NativeDock.Select(tool);
                for(var attempt=0;attempt<100 && !window.GetVisualDescendants().Contains(window.ToolView(tool));attempt++) {window.UpdateLayout();await Task.Delay(20);}
                Assert(window.GetVisualDescendants().Contains(window.ToolView(tool)) && !window.GetVisualDescendants().Contains(window.ToolView(ReferenceEquals(tool,layerTool)?blocksTool:layerTool)),"Native tabs recycled a different toolbox's view.");
            }
            report.Passed.Add("Native Layers/Blocks tab switching renders each original business view without recycling another toolbox");
            vm.ClearSelection();
            var payload = new Direct2dCad.Commands.AddOleObjectCommand(CadRectD.FromXYWH(0, 0, 20, 20), new byte[4 * 1024 * 1024]);
            Assert(Direct2dCad.Commands.CadCommandPayloadEstimate.Estimate(payload) >= 4 * 1024 * 1024, "Native command history lost its retained private byte payload."); report.Passed.Add("Native command history retains 4 MiB payload accounting");
            var printViewport = new Direct2dCad.Rendering.CadViewport(); printViewport.SetSize(64, 64); var printBackground = document.ViewSettings.BackgroundColor;
            // Move the tested paper area away from the OLE fixture and other geometry.
            printViewport.SetView(1, new CadPointD(-1000, 1000));
            var printFrame = WindowsPrintService.RenderPage(vm.CreatePrintRequest("Native print fixture"), printViewport, new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false }, 64, 64);
            Assert(printFrame.Pixels.All(p => p == 255) && document.ViewSettings.BackgroundColor == printBackground, $"Print rendering did not produce white paper (first BGRA: {string.Join(',', printFrame.Pixels.Take(4))}) or changed the document background."); report.Passed.Add("White paper print rendering preserves document background");
            vm.CadEditor.AddText("Native vector print", new CadPointD(0, -10), 3);
            var vectorRequest = vm.CreatePrintRequest("Vector fixture"); var xps = Path.Combine(Path.GetDirectoryName(output)!, "vector-print.xps");
            using (var page = WindowsPrintService.CreateVectorPage(vectorRequest, vectorRequest.PaperBounds, 800, 600, CadRectD.FromXYWH(0, 0, 800, 600), Direct2dCad.ViewModels.Services.Platform.Printing.CadPaperScaling.Fit))
                VectorPrintBridge.ExportXps(page, xps, 800, 600);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(xps))
            {
                var fixedPage = archive.Entries.Single(entry => entry.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase));
                using var stream = fixedPage.Open(); var markup = System.Xml.Linq.XDocument.Load(stream);
                Assert(markup.Descendants().Count(element => element.Name.LocalName == "Path") > 2 && markup.Descendants().Any(element => element.Name.LocalName == "Glyphs"), "Printed XPS lost vector paths or text glyphs.");
                Assert(document.ViewSettings.BackgroundColor == printBackground, "Vector printing changed the document background.");
            }
            report.Passed.Add("Native Direct2D/XPS vector printing retains geometry paths and text glyphs");
            // Text creation schedules DirectWrite bounds measurement on the next render.
            // Finish that document mutation before recovery captures a versioned snapshot.
            vm.RequestRender();
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            // Native recovery exercises both the file resolver and generated metadata context.
            using var recovery = new CadRecoveryStore(Path.Combine(Path.GetDirectoryName(output)!, "recovery-check"));
            var entry = await recovery.SaveAsync(vm.CadEditor, path); Assert(recovery.List().Any(r => r.DocumentId == entry.DocumentId), "Native recovery metadata could not be read."); report.Passed.Add("Recovery snapshot and metadata");
            if (report.HeadlessWindowing) await CheckListMenusAsync(window, entry, report, Assert);

            var settings = services.GetRequiredService<IUserSettingsStore>(); var user = settings.Load(); settings.Save(user); Assert(settings.Load().General.CultureLcid == user.General.CultureLcid, "Native settings roundtrip failed."); report.Passed.Add("Generated user settings JSON");
            var settingsVm = new Direct2dCad.ViewModels.Settings.UserSettings.UserSettingsViewModel(user, settings, _ => { });
            foreach (var section in settingsVm.Sections) { var settingsView = KnownViews.Create(section); Assert(settingsView is not null, "Missing typed settings view."); settingsView!.Measure(new global::Avalonia.Size(480, 800)); settingsView.Arrange(new global::Avalonia.Rect(0, 0, 480, 800)); }
            var radialSettings = settingsVm.Interaction.RadialMenu; var previousAction = radialSettings.SelectedProfile.Slots[0].SelectedAction;
            radialSettings.SelectedProfile.Slots[0].SelectedAction = radialSettings.ActionOptions.First(o => o.Action == Direct2dCad.Client.Common.Settings.CadRadialMenuAction.Line);
            Assert(settingsVm.TryApply() && settings.Load().Interaction.RadialMenu.MiddleActions[0] == Direct2dCad.Client.Common.Settings.CadRadialMenuAction.Line, "Radial slot settings did not persist."); settings.Save(user);
            report.Passed.Add("Typed settings forms and radial slot persistence");
            var culture = services.GetRequiredService<IApplicationCultureService>(); var oldCulture = culture.GetCurrentCultureLCID();
            var fileMenu = window.GetLogicalDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().First();
            try { culture.ChangeCulture("en-US"); Assert((string?)fileMenu.Header == Direct2dCad.Lang.CadUiText.Get("File"), "English menu did not refresh."); culture.ChangeCulture("zh-CN"); Assert((string?)fileMenu.Header == Direct2dCad.Lang.CadUiText.Get("File"), "Chinese menu did not refresh."); if(report.HeadlessWindowing) await CheckChineseUiAsync(window,services.GetRequiredService<IDialogService>(),report,Assert,output); }
            finally { culture.ChangeCulture(oldCulture); }
            report.Passed.Add("Live English and Chinese menu localization");
            var bottomPanel = window.NativeDock.Groups[DockZone.BottomRight];
            var activeBottomTool=bottomPanel.VisibleDockables!.Last();window.NativeDock.Factory.SetActiveDockable(activeBottomTool);
            vm.SelectEntities([circlePair.Key]);window.NativeDock.Select(main.EntityProperties);
            var oldZone = main.EntityProperties.Zone; var originalPropertyView=window.ToolView(main.EntityProperties)??throw new InvalidOperationException("The property view is missing.");window.FloatTool(main.EntityProperties);
            for(var attempt=0;attempt<100 && !originalPropertyView!.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(box=>box.IsEffectivelyVisible);attempt++)await Task.Delay(20);
            Assert(originalPropertyView!.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(box=>box.IsEffectivelyVisible),"Floating lost the selected entity's property form.");
            await CheckBorderlessFloatingWindowAsync((global::Dock.Avalonia.Controls.HostWindow)TopLevel.GetTopLevel(originalPropertyView)!, report, Assert, output);
            if(report.HeadlessWindowing)
            {
                var floatingWindow=(Window)TopLevel.GetTopLevel(originalPropertyView)!;
                var circleEditor=(Direct2dCad.ViewModels.Toolboxes.EntityProperty.CirclePropertyViewModel)main.EntityProperties.Entity!;
                var beforeRadius=((CadCircle)document.Entities[circlePair.Key]).Radius;circleEditor.Radius=beforeRadius+1;
                for(var attempt=0;attempt<100 && !originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(box=>box.IsEffectivelyVisible && !box.IsReadOnly);attempt++) {floatingWindow.UpdateLayout();await Task.Delay(20);}
                originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().First(box=>box.IsEffectivelyVisible && !box.IsReadOnly).Focus();
                void FloatingKey(global::Avalonia.Input.RawInputModifiers modifiers) {global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(floatingWindow,global::Avalonia.Input.Key.Z,modifiers,global::Avalonia.Input.PhysicalKey.None,null);global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(floatingWindow,global::Avalonia.Input.Key.Z,modifiers,global::Avalonia.Input.PhysicalKey.None,null);}
                FloatingKey(global::Avalonia.Input.RawInputModifiers.Control);Assert(((CadCircle)document.Entities[circlePair.Key]).Radius==beforeRadius,"Floating numeric focus lost document Ctrl+Z.");
                FloatingKey(global::Avalonia.Input.RawInputModifiers.Control|global::Avalonia.Input.RawInputModifiers.Shift);Assert(((CadCircle)document.Entities[circlePair.Key]).Radius==beforeRadius+1,"Floating numeric focus lost document Ctrl+Shift+Z.");
                FloatingKey(global::Avalonia.Input.RawInputModifiers.Control);
                report.Passed.Add("Floating property numeric focus routes document undo and redo to the active CAD document");
            }
            Assert(window.NativeDock.Root.Windows?.Any(w=>w.Layout is not null && NativeDockShell.Find(w.Layout).Contains(window.NativeDock.Tools[main.EntityProperties]))==true,"Native floating did not create a window containing the original toolbox.");
            window.NativeDock.Factory.PinDockable(window.NativeDock.Tools[main.EntityProperties]);await Task.Delay(100);window.UpdateLayout();
            Assert(window.NativeDock.Root.Windows!.Count==0 && window.NativeDock.Factory.HostWindows.Count()==0,"Returning an uncaptured floating tool left an empty live host with a copied zone ID.");
            window.FloatTool(main.EntityProperties);await Task.Delay(100);window.UpdateLayout();
            var floatingState=window.NativeDock.Capture();SettingsPath.Write("smoke-floating-dock.json",floatingState,SettingsJsonContext.Default.NativeDockState);
            using(var restoredShell=new NativeDockShell(new Border(),window.NativeDock.Tools.Values.Select(t=>(t.Model,(Control)new Border())),initializeDefaults:false))
            {
                restoredShell.Restore(SettingsPath.Read("smoke-floating-dock.json",SettingsJsonContext.Default.NativeDockState)!);
                var restoredWindow=restoredShell.Root.Windows!.Single();
                Assert(restoredWindow.Host is not null && restoredWindow.Layout!.Window==restoredWindow && NativeDockShell.Find(restoredWindow.Layout).Contains(restoredShell.Tools[main.EntityProperties]) && restoredWindow.Width==floatingState.Windows[0].Width && restoredWindow.Height==floatingState.Windows[0].Height,"Floating window host, content and dimensions did not survive reconstruction.");
                Assert(restoredWindow.Host is global::Dock.Avalonia.Controls.HostWindow {WindowDecorations: WindowDecorations.None, ToolChromeControlsWholeWindow: true, CanResize: true}, "Restored floating panes regained a system frame or lost the Dock title grip.");
                var restoredTool=restoredShell.Tools[main.EntityProperties];
                Assert(restoredTool.OriginalOwner is not null,"Restored floating tool lost its native Dock menu destination.");
                restoredShell.Factory.PinDockable(restoredTool);
                Assert(ReferenceEquals(restoredTool.Owner,restoredShell.Groups[oldZone]),"Floating Dock command did not restore the previous pane.");
                Assert(restoredShell.Root.Windows!.Count==0,"Redocking the last restored floating toolbox left an empty window.");
            }
            window.DockTool(main.EntityProperties, DockZone.RightTop); window.DockTool(main.EntityProperties, oldZone);
            Assert(ReferenceEquals(originalPropertyView,window.ToolView(main.EntityProperties)),"Floating replaced the original toolbox view.");
            for(var attempt=0;attempt<100 && (!window.GetVisualDescendants().Contains(originalPropertyView)||!originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(box=>box.IsEffectivelyVisible));attempt++) {window.UpdateLayout();await Task.Delay(20);}
            Assert(window.GetVisualDescendants().Contains(originalPropertyView) && originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(box=>box.IsEffectivelyVisible),$"Redocking lost the selected entity's visible property editors: attached={TopLevel.GetTopLevel(originalPropertyView)?.GetType().Name}, parent={originalPropertyView.Parent?.GetType().Name}, visible={originalPropertyView.IsEffectivelyVisible}, content={(originalPropertyView as ContentControl)?.Content?.GetType().Name}, entity={main.EntityProperties.Entity?.GetType().Name}, numbers={originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Count()}, currentDoc={main.CurrentEditorTabViewModel?.Title}.");
            vm.ClearSelection();
            Assert(ReferenceEquals(bottomPanel.ActiveDockable, activeBottomTool), "Floating or docking a toolbox reset another panel's selected page.");
            report.Passed.Add("Floating toolbox and docking back preserve the active page in other panels");
            foreach (var zone in Enum.GetValues<DockZone>())
            {
                window.DockTool(main.EntityProperties, zone);
                Assert(window.NativeDock.Groups[zone].VisibleDockables!.Contains(window.NativeDock.Tools[main.EntityProperties]), "Toolbox did not reach its independent dock zone: " + zone);
            }
            window.DockTool(main.EntityProperties, oldZone); window.UpdateLayout();
            nativeWorkspace.Proportion=.73;bottomPanel.Proportion=.61;
            var dockState = window.NativeDock.Capture();
            SettingsPath.Write("smoke-dock-state.json", dockState, SettingsJsonContext.Default.NativeDockState);
            var restoredDock = SettingsPath.Read("smoke-dock-state.json", SettingsJsonContext.Default.NativeDockState);
            using(var restoredShell=new NativeDockShell(new Border(),window.NativeDock.Tools.Values.Select(t=>(t.Model,(Control)new Border())),initializeDefaults:false))
            {
                restoredShell.Restore(restoredDock!);
                Assert(NativeDockShell.Find(restoredShell.Root).Single(d=>d.Id=="workspace").Proportion==.73 && restoredShell.Groups[DockZone.BottomRight].Proportion==.61 && restoredShell.Groups[DockZone.BottomRight].ActiveDockable?.Id==activeBottomTool.Id,"Native layout proportions and active toolbox did not survive reconstruction.");
            }
            report.Passed.Add("Six independent dock zones and generated split-proportion settings");
            var propertyContent = window.ToolView(main.EntityProperties)!;
            window.SetAutoHidden(main.EntityProperties,true); window.UpdateLayout();
            Assert(window.IsAutoHidden(main.EntityProperties) && !window.NativeDock.Groups[oldZone].VisibleDockables!.Contains(window.NativeDock.Tools[main.EntityProperties]),"Auto-hide did not remove the pane from its dock group.");
            window.ShowAutoTool(main.EntityProperties); window.UpdateLayout();
            Assert(window.IsAutoHideVisible && window.GetVisualDescendants().Contains((Control)propertyContent!),"Auto-hide did not show the original view in the edge overlay.");
            var autoState=window.NativeDock.Capture();SettingsPath.Write("smoke-auto-dock.json",autoState,SettingsJsonContext.Default.NativeDockState);
            using(var restoredShell=new NativeDockShell(new Border(),window.NativeDock.Tools.Values.Select(t=>(t.Model,(Control)new Border())),initializeDefaults:false))
            {
                restoredShell.Restore(SettingsPath.Read("smoke-auto-dock.json",SettingsJsonContext.Default.NativeDockState)!);
                Assert(restoredShell.IsPinned(main.EntityProperties),"Auto-hidden state did not persist.");
                restoredShell.Pin(main.EntityProperties,false);
                Assert(restoredShell.Groups[oldZone].VisibleDockables!.Contains(restoredShell.Tools[main.EntityProperties]),"Restarted auto-hidden toolbox did not pin back into the original group.");
            }
            if(report.HeadlessWindowing) {await Task.Delay(200); using var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);frame!.Save(Path.Combine(Path.GetDirectoryName(output)!,"dock-auto-hide.png"));}
            Button? previewPin=null;
            for(var attempt=0;attempt<100 && previewPin is null;attempt++)
            {
                window.UpdateLayout();
                previewPin=window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.ToolChromeControl>()
                    .Where(chrome=>ReferenceEquals(chrome.DataContext,window.NativeDock.Root.PinnedDock))
                    .SelectMany(chrome=>chrome.GetVisualDescendants().OfType<Button>()).FirstOrDefault(button=>button.Name=="PART_PinButton" && button.IsEffectivelyVisible && button.IsEnabled && button.Command is not null);
                if(previewPin is null)await Task.Delay(20);
            }
            Assert(previewPin is not null,"Auto-hide preview has no enabled native pin command.");
            previewPin!.Command!.Execute(previewPin.CommandParameter);window.UpdateLayout();
            Assert(!window.IsAutoHidden(main.EntityProperties),"Native preview pin command did not restore the docked pane.");
            window.HideAutoTool();Assert(window.NativeDock.Root.PinnedDock?.VisibleDockables?.Count is null or 0,"Auto-hide overlay did not close after native pinning.");
            Assert(window.NativeDock.Groups[oldZone].VisibleDockables!.Contains(window.NativeDock.Tools[main.EntityProperties]) && ReferenceEquals(window.ToolView(main.EntityProperties),propertyContent),"Pinning did not restore the original property view.");
            report.Passed.Add("Dock.Avalonia floating, six groups and auto-hide reuse views; native layout persists and pins back after restart");
            window.NativeDock.Factory.CloseDockable(window.NativeDock.Tools[main.EntityProperties]);
            Assert(!main.EntityProperties.IsOpen && window.NativeDock.Root.HiddenDockables!.Contains(window.NativeDock.Tools[main.EntityProperties]),"Native close did not synchronize the business toolbox state.");
            main.EntityProperties.IsOpen=true;window.UpdateLayout();
            Assert(window.NativeDock.Groups[oldZone].VisibleDockables!.Contains(window.NativeDock.Tools[main.EntityProperties]) && ReferenceEquals(originalPropertyView,window.ToolView(main.EntityProperties)),"Reopening a native closed toolbox lost its original group or view.");
            report.Passed.Add("Native close and business reopen retain the same toolbox and dock location");
            window.NativeDock.Factory.DockAsDocument(window.NativeDock.Tools[main.EntityProperties]);window.UpdateLayout();await Task.Delay(100);
            Assert(window.NativeDock.Tools[main.EntityProperties].Owner is global::Dock.Model.Controls.IDocumentDock && window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.DocumentTabStrip>().Any(strip=>strip.IsEffectivelyVisible),"Docking a toolbox as a document hid its navigation tabs.");
            window.DockTool(main.EntityProperties,oldZone);window.UpdateLayout();await Task.Delay(100);
            Assert(window.GetVisualDescendants().OfType<global::Dock.Avalonia.Controls.DocumentTabStrip>().All(strip=>!strip.IsEffectivelyVisible),"The redundant outer CAD tab remained visible after redocking the toolbox.");
            report.Passed.Add("Dock as tabbed document exposes native navigation; redocking restores the compact CAD tabs");
            vm.SelectEntities([circlePair.Key]);window.NativeDock.Select(main.EntityProperties);
            for(var cycle=0;cycle<6;cycle++)
            {
                foreach(var control in originalPropertyView.GetVisualDescendants().OfType<Control>())control.InvalidateMeasure();
                window.FloatTool(main.EntityProperties);await Task.Delay(60);
                foreach(var control in originalPropertyView.GetVisualDescendants().OfType<Control>())control.InvalidateMeasure();
                window.DockTool(main.EntityProperties,cycle%2==0?DockZone.RightTop:oldZone);
                window.UpdateLayout();await Task.Delay(60);
                Assert(vm.CadEditor.Selection.EntityIds.Contains(circlePair.Key) && main.EntityProperties.Entity is not null,$"Float/redock cycle {cycle} lost the selected entity: selection={vm.CadEditor.Selection.EntityIds.Count}, propertyDocument={main.EntityProperties.DocumentViewModel is not null}.");
            }
            window.NativeDock.ResetLayout();window.UpdateLayout();
            for(var attempt=0;attempt<100 && !originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(c=>c.IsEffectivelyVisible);attempt++){await Task.Delay(20);window.UpdateLayout();}
            Assert(window.NativeDock.Root.Windows?.Count==0 && window.NativeDock.Groups[DockZone.LeftTop].VisibleDockables!.Contains(window.NativeDock.Tools[main.Layers]),"Layout reset did not close floating windows and group navigation tools.");
            Assert(window.NativeDock.Groups[DockZone.BottomLeft].IsEmpty && window.NativeDock.Groups[DockZone.BottomLeft].IsCollapsable,"Empty bottom slot still reserves dock space.");
            Assert(ReferenceEquals(window.ToolView(main.EntityProperties),originalPropertyView) && originalPropertyView.GetVisualDescendants().OfType<Controls.CadPropertyNumberBox>().Any(c=>c.IsEffectivelyVisible),$"Layout reset lost selected entity property content: root={TopLevel.GetTopLevel(originalPropertyView)?.GetType().Name}, parent={originalPropertyView.Parent?.GetType().Name}, entity={main.EntityProperties.Entity?.GetType().Name}, active={main.CurrentEditorTabViewModel?.Title}, selected={window.FindControl<TabControl>("Documents")?.SelectedIndex}, visible={originalPropertyView.IsEffectivelyVisible}.");
            report.Passed.Add("Repeated invalidated cross-window float/redock and layout reset retain content without stale layout managers; empty dock slots collapse");
            await CheckDockTargetsAsync(window,editorView,report,output,Assert);
            for(var attempt=0;attempt<100 && TopLevel.GetTopLevel(editorView.Canvas)!=window;attempt++) {window.UpdateLayout();await Task.Delay(20);}
            Assert(TopLevel.GetTopLevel(editorView.Canvas)==window,"Redocking did not restore the active CAD canvas.");
            if(report.HeadlessWindowing)
            {
                vm.SetToolMode(CadCanvasToolMode.CircleCenterRadius);
                var badgePoint=global::Avalonia.VisualExtensions.TranslatePoint(editorView.Canvas,new global::Avalonia.Point(editorView.Canvas.Bounds.Width-12,editorView.Canvas.Bounds.Height-12),window)!.Value;
                global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window,badgePoint);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Background);window.UpdateLayout();
                var badge=editorView.GetVisualDescendants().OfType<Border>().Single(c=>global::Avalonia.Automation.AutomationProperties.GetAutomationId(c)=="CanvasToolBadge");
                Assert(badge.IsVisible && badge.Child is not null && global::Avalonia.Controls.Canvas.GetLeft(badge)>=0 && global::Avalonia.Controls.Canvas.GetTop(badge)>=0,"Tool cursor badge did not render or flipped outside the canvas.");
                vm.SetToolMode(CadCanvasToolMode.Select);Assert(!badge.IsVisible,"Select mode left the drawing cursor badge visible.");
                report.Passed.Add("Drawing cursor badge follows routed pointer input, flips at edges and hides for Select");
                using var printPreview=new Views.WindowsPrintPreview(vm.CreatePrintRequest("Native inline printer options"));
                printPreview.Window.Show(window);printPreview.Window.UpdateLayout();
                TControl PrintControl<TControl>(string id) where TControl:Control=>printPreview.Window.GetVisualDescendants().OfType<TControl>().Single(c=>global::Avalonia.Automation.AutomationProperties.GetAutomationId(c)==id);
                var paperCombo=PrintControl<ComboBox>("PrintPreviewPaperSizeCombo");var orientationCombo=PrintControl<ComboBox>("PrintPreviewOrientationCombo");
                Assert(paperCombo.ItemCount>0 && PrintControl<ComboBox>("PrintPreviewPrinterCombo").ItemCount>0 && printPreview.Printer.Mode!=0 && PrintControl<Button>("PrintFromPreviewButton").IsEnabled,"Installed printer capabilities did not produce a valid preview.");
                await CheckCompactScrollBarAsync(PrintControl<ScrollViewer>("PrintOptionsScrollViewer"),Assert,output,"print-scrollbar");
                report.Passed.Add("Print options scroll bar stays narrow while hovered and pressed; thumb drag scrolls printer settings");
                orientationCombo.SelectedIndex=0;var portraitWidth=printPreview.PageWidth;var portraitHeight=printPreview.PageHeight;
                orientationCombo.SelectedIndex=1;Assert(printPreview.PageWidth>printPreview.PageHeight && portraitHeight>portraitWidth,"Driver landscape did not change the printable page orientation.");
                if(paperCombo.ItemCount>1){var previousPaper=printPreview.Printer.PaperId;paperCombo.SelectedIndex=(paperCombo.SelectedIndex+1)%paperCombo.ItemCount;Assert(printPreview.Printer.PaperId!=previousPaper,"Inline paper selection did not reach the validated DEVMODE.");}
                PrintControl<NumericUpDown>("PrintPreviewCopiesInput").Value=2;Assert(printPreview.Printer.Copies==2,"Inline copy count did not reach the driver ticket.");
                var copiesInput=PrintControl<NumericUpDown>("PrintPreviewCopiesInput");
                var copiesPeer=global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(copiesInput);
                var rangeNotifications=0;
                copiesPeer.PropertyChanged+=(_,e)=>
                {
                    if(e.Property==global::Avalonia.Automation.RangeValuePatternIdentifiers.ValueProperty)
                    { Assert(e.OldValue is double && e.NewValue is double,"Numeric automation published unsupported decimal VARIANT values.");rangeNotifications++; }
                };
                var rangeProvider=copiesPeer.GetProvider<global::Avalonia.Automation.Provider.IRangeValueProvider>()!;
                rangeProvider.SetValue(3);Assert(printPreview.Printer.Copies==3 && rangeProvider.Value==3 && rangeNotifications==1,"Accessible numeric spin did not update the driver ticket.");
                rangeProvider.SetValue(2);
                report.Passed.Add("Printing numeric automation exposes double range values and notifications without decimal VARIANT crashes");
                if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("PrintTicket smoke requires Windows 10 or later.");
                Direct2dCad.Windows.Interop.InteropSelfChecks.VerifyPrintTicket(printPreview.Printer.Name, printPreview.Printer.Mode, printPreview.Printer.Copies);
                report.Passed.Add("CsWin32 converts the installed printer Unicode DEVMODE into valid PrintTicket XML preserving copies without submitting a job");
                PrintControl<NumericUpDown>("PrintPreviewDpiInput").Value=600;Assert(printPreview.Dpi==600,"Inline DPI did not reach vector print settings.");
                var scalingCombo=PrintControl<ComboBox>("PrintScalingCombo");scalingCombo.SelectedIndex=2;Assert(printPreview.Scaling==Direct2dCad.ViewModels.Services.Platform.Printing.CadPaperScaling.Custom,"Custom print scale is unavailable.");scalingCombo.SelectedIndex=1;
                foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark}){global::Avalonia.Application.Current.RequestedThemeVariant=theme;await Task.Delay(600);printPreview.Window.UpdateLayout();using var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(printPreview.Window);frame!.Save(Path.Combine(Path.GetDirectoryName(output)!,theme==ThemeVariant.Light?"print-light.png":"print-dark.png"));}
                printPreview.Window.Close();global::Avalonia.Application.Current.RequestedThemeVariant=originalTheme;
                report.Passed.Add("Installed Windows printer enumeration, paper, orientation, copies, DPI and scale validate without submitting a print job");
            }
            using var handler = new SmokeHttpHandler(); using var http = new HttpClient(handler); var lm = new LmStudioChatClient(http);
            var completion = await lm.CompleteAsync(new("http://localhost:1234/v1", "native-smoke", [AiChatMessage.User("test")], [CadWorkspaceToolExecutor.ToolDefinitions.First()]));
            Assert(completion.Content == "ok" && handler.Request?.Contains("native-smoke", StringComparison.Ordinal) == true && handler.Request.Contains("tool_choice", StringComparison.Ordinal), "Native LM Studio request serialization failed."); report.Passed.Add("LM Studio request serialization with a local stub");
            for(var attempt=0;attempt<100 && !editorView.Canvas.IsRenderHostAttached;attempt++){window.UpdateLayout();await Task.Delay(20);}
            Assert(editorView.Canvas.IsRenderHostAttached,$"CAD canvas did not reattach its render host after layout reset: visible={editorView.Canvas.IsEffectivelyVisible}, bounds={editorView.Canvas.Bounds}.");
            window.UpdateLayout();vm.FitToWindow(); vm.RequestRender(); await Task.Delay(50);await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            for(var attempt=0;attempt<100 && !editorView.Canvas.IsRenderHostAttached;attempt++){window.UpdateLayout();await Task.Delay(20);}
            Assert(editorView.Canvas.IsRenderHostAttached,"A stale document-host callback detached the active CAD canvas before its frame.");
            vm.RequestRender();await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},global::Avalonia.Threading.DispatcherPriority.Render);
            var pixels = ((Direct2dCad.Rendering.Direct2D.Hosting.Direct2DImageRenderHost)vm.RenderSession).CaptureBackBufferPixels(); Assert(pixels.Length > 0 && pixels.Any(p => p != 0), "Direct2D did not produce a frame."); report.HardwareDirect2D = !vm.RenderSession.UsingWarp; report.Passed.Add("Direct2D rendered a real frame");
            if (report.HeadlessWindowing)
            {
                vm.SelectEntities([polylineId]);
                window.NativeDock.Select(main.EntityProperties);
                try
                {
                    foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    {
                        global::Avalonia.Application.Current.RequestedThemeVariant = theme;
                        await Task.Delay(600); // Allow Material's 100 ms timer and 350 ms brush transitions to settle.
                        window.UpdateLayout();
                        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
                        using var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                        Assert(frame is not null && frame.PixelSize.Width >= 1000 && frame.PixelSize.Height >= 640, "Headless Skia window did not render a complete frame.");
                        frame!.Save(Path.Combine(Path.GetDirectoryName(output)!, theme == ThemeVariant.Light ? "preview-light.png" : "preview-dark.png"));
                    }
                }
                finally { global::Avalonia.Application.Current.RequestedThemeVariant = originalTheme; vm.ClearSelection(); }
                report.Passed.Add("Full window Skia previews rendered in light and dark themes without desktop input");
            }
            await CheckUnsavedApplicationExitAsync(window, services, report, Assert, output);
        }
        catch (Exception ex) { report.Failure = ex.ToString(); if(report.HeadlessWindowing) {using var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"failure.png"));} }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, JsonSerializer.Serialize(report, SmokeJsonContext.Default.NativeSmokeReport));
            window.CloseForSmoke(report.Failure is null ? 0 : 1);
        }
    }
    private sealed class SmokeHttpHandler : HttpMessageHandler
    {
        public string? Request { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            Request = await message.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}") };
        }
    }
}



