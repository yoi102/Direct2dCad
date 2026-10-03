using System.Text.Json;
using System.Runtime.InteropServices;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Documents;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

[Collection(CadApplicationCollection.Name)]
public sealed class RecoveryToolboxUiTests
{
    private const string LongName = "ArduinoUnoRev3PCB · 长文件名恢复图纸 · 电路板布局与机械安装位置";

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void RecoveryContextMenuTargetsTheClickedDrawing()
    {
        using var fixture = new CadApplicationFixture(directory => SeedRecovery(directory, true, 2052), captureBindings: true);
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            var rows = fixture.WaitForElement("DrawingAssistantToolbox")
                .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem"));
            rows[1].RightClick();
            Assert.True(fixture.WaitForElement("OpenRecoveryCopyMenuItem", includePopups: true).IsEnabled);
            Assert.True(fixture.WaitForElement("OpenRecoveryFolderMenuItem", includePopups: true).IsEnabled);
            Capture(fixture, "recovery-context-menu.png");
            fixture.WaitForElement("ClearRecoveryEntryMenuItem", includePopups: true).AsMenuItem().Invoke();
            fixture.WaitUntil(() => fixture.WaitForElement("DrawingAssistantToolbox")
                .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem")).Length == 15,
                "The recovery context menu did not remove its drawing.");
            Assert.Contains(fixture.WaitForElement("DrawingAssistantToolbox")
                .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem")), row => row.Name.StartsWith(LongName));
            fixture.WaitForElement("RecoveryEntryItem").RightClick();
            fixture.WaitForElement("ClearAllRecoveryMenuItem", includePopups: true).AsMenuItem().Invoke();
            fixture.WaitUntil(() => !fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen, "The context-menu clear all did not empty the recovery list.");
        }
        finally { SaveBindingTrace(fixture, "recovery-context-menu"); }
        AssertBindingTraceIsEmpty(fixture);
    }

    [Theory]
    [InlineData(true, 2052)]
    [InlineData(false, 1033)]
    [Trait("Category", "UiAutomation")]
    public void RightSideRecoveryButtonsAndClearAllWorkWithMouse(bool dark, int culture)
    {
        using var fixture = new CadApplicationFixture(directory => SeedRecovery(directory, dark, culture), captureBindings: true);
        var name = dark ? "recovery-actions-dark-zh" : "recovery-actions-light-en";
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            fixture.MainWindow.Patterns.Transform.Pattern.Move(0, 0);
            fixture.MainWindow.Patterns.Transform.Pattern.Resize(900, 700);
            var open = fixture.WaitForElement("OpenRecoveryEntryIconButton");
            var clear = fixture.WaitForElement("ClearRecoveryEntryButton");
            var toolboxBounds = fixture.WaitForElement("DrawingAssistantToolbox").BoundingRectangle;
            Assert.InRange(open.BoundingRectangle.Right, toolboxBounds.Left, toolboxBounds.Right);
            Assert.InRange(clear.BoundingRectangle.Right, toolboxBounds.Left, toolboxBounds.Right);
            Assert.True(open.IsEnabled);
            open.Click(); // Real pointer hit testing, rather than UIA Invoke.
            fixture.WaitForElement("CadCanvas");
            clear = fixture.WaitForElement("ClearRecoveryEntryButton");
            fixture.WaitUntil(() => clear.IsEnabled, "The recovery row remained disabled after opening.");
            clear.Click();
            var recovery = Path.Combine(fixture.SettingsDirectory, "Recovery");
            fixture.WaitUntil(() => Directory.GetFiles(recovery, "*.d2cad").Length == 15,
                "Clicking the right-side clear button did not delete the recovery copy.");
            fixture.WaitUntil(() => fixture.WaitForElement("DrawingAssistantToolbox")
                    .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem")).Length == 15,
                "The recovery list did not refresh after clearing one drawing.");
            Capture(fixture, name + "-single-cleared.png");
            fixture.WaitForElement("ClearAllRecoveryButton").Click();
            fixture.WaitUntil(() => Directory.GetFiles(recovery, "*.d2cad").Length == 0,
                "Clear all left recovery snapshots on disk.");
            Assert.False(fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen);
            Assert.False(fixture.WaitForElement("CadCanvas").IsOffscreen);
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("ClearAllRecoveryButton")) is null or { IsOffscreen: true });
            Capture(fixture, name + "-all-cleared.png");
        }
        finally { Capture(fixture, name + "-final.png"); SaveBindingTrace(fixture, name); }
        AssertBindingTraceIsEmpty(fixture);
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void CrashRecoveryAppearsAfterRestartAndDiscardedCloseDoesNotReappear()
    {
        using var fixture = new CadApplicationFixture(captureBindings: true);
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            fixture.WaitForElement("NewDocumentButton").AsButton().Invoke();
            fixture.WaitForElement("NewBlankDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
            fixture.WaitForElement("CadCanvas");
            ExecuteTerminal(fixture, "TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":100,\"y2\":0}", "created_entity_id");
            var recovery = Path.Combine(fixture.SettingsDirectory, "Recovery");
            fixture.WaitUntil(() => Directory.Exists(recovery) && Directory.GetFiles(recovery, "*.recovery.json").Length > 0,
                "The real 60-second recovery timer did not create an idle drawing snapshot.", TimeSpan.FromSeconds(90));
            fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
            Assert.False(fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen);
            Assert.Empty(fixture.WaitForElement("DrawingAssistantToolbox")
                .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem")));
            Capture(fixture, "recovery-live-session-hidden.png");
            AssertBindingTraceIsEmpty(fixture);
            SaveBindingTrace(fixture, "recovery-before-crash");

            fixture.Application.Kill(); // Only the isolated test process is terminated.
            Assert.True(SpinWait.SpinUntil(() => fixture.Application.HasExited, TimeSpan.FromSeconds(10)));
            fixture.RestartAfterExit();
            var open = fixture.WaitForElement("OpenRecoveryEntryIconButton");
            fixture.WaitUntil(() => open.IsEnabled && !open.IsOffscreen, "The crash recovery button was not ready after restarting.");
            Capture(fixture, "recovery-after-process-crash-before-opening.png");
            open.Click();
            fixture.WaitForElement("CadCanvas");
            ExecuteTerminal(fixture, "STATUS", "Entities: 1");
            Capture(fixture, "recovery-after-process-crash.png");

            fixture.MainWindow.Close();
            ClickEnabled(fixture, "MessageDialogOkButton");
            ClickEnabled(fixture, "CancelCloseDocumentsButton");
            fixture.WaitUntil(() => fixture.MainWindow.IsEnabled, "Cancelling close did not return to the editor.");
            Assert.NotEmpty(Directory.GetFiles(recovery, "*.d2cad"));
            // Finish the dialog's asynchronous close continuation before requesting another exit.
            ExecuteTerminal(fixture, "TOOL list_documents {}", "\"documents\"");
            fixture.MainWindow.Close();
            ClickEnabled(fixture, "MessageDialogOkButton");
            ClickEnabled(fixture, "DiscardDocumentsButton");
            Assert.True(SpinWait.SpinUntil(() => fixture.Application.HasExited, TimeSpan.FromSeconds(15)));
            Assert.Empty(Directory.GetFiles(recovery, "*.d2cad"));
            AssertBindingTraceIsEmpty(fixture);
            SaveBindingTrace(fixture, "recovery-after-discard");

            fixture.RestartAfterExit();
            fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
            Assert.False(fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen);
            Assert.Empty(fixture.WaitForElement("DrawingAssistantToolbox")
                .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem")));
            Capture(fixture, "recovery-after-normal-close.png");
        }
        finally
        {
            if (!fixture.Application.HasExited) Capture(fixture, "recovery-lifecycle-final.png");
            SaveBindingTrace(fixture, "recovery-after-clean-restart");
        }
        AssertBindingTraceIsEmpty(fixture);
    }

    private static void ExecuteTerminal(CadApplicationFixture fixture, string command, string expected)
    {
        var input = fixture.WaitForElement("CommandLineInput").AsTextBox();
        input.Focus();
        input.Text = command;
        fixture.MainWindow.Focus();
        input.Focus();
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => fixture.WaitForElement("CommandLineOutput").Properties.HelpText.ValueOrDefault?.Contains(expected) == true,
            $"Terminal did not produce '{expected}' for '{command}'.");
    }

    private static void ClickEnabled(CadApplicationFixture fixture, string id)
    {
        var button = fixture.WaitForElement(id).AsButton();
        fixture.WaitUntil(() => button.IsEnabled, $"'{id}' remained disabled.");
        button.Invoke();
    }

    [Theory]
    [InlineData(true, 2052, "图纸恢复", "恢复")]
    [InlineData(false, 1033, "Drawing recovery", "Recovered")]
    [Trait("Category", "UiAutomation")]
    public void RecoveryRowsAreDirectScrollableAndOpenWithKeyboard(bool dark, int culture, string title, string suffix)
    {
        using var fixture = new CadApplicationFixture(directory => SeedRecovery(directory, dark, culture), captureBindings: true);
        var artifactName = dark ? "recovery-dark-zh" : "recovery-light-en";
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            fixture.MainWindow.Patterns.Transform.Pattern.Move(0, 0);
            foreach (var (width, height) in new[] { (1300, 900), (900, 700) })
            {
                fixture.MainWindow.Patterns.Transform.Pattern.Resize(width, height);
                var toolbox = fixture.WaitForElement("DrawingAssistantToolbox");
                Assert.False(toolbox.IsOffscreen);
                Assert.NotNull(fixture.MainWindow.FindFirstDescendant(c => c.ByName(title)));
                var entries = toolbox.FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem"));
                Assert.Equal(16, entries.Length);
                Assert.All(entries, row => Assert.InRange(row.BoundingRectangle.Height, 42, 65));
                Assert.DoesNotContain(toolbox.FindAllDescendants(), element => element.Patterns.ExpandCollapse.IsSupported);
                var scroll = fixture.WaitForElement("RecoveryEntriesScrollViewer").Patterns.Scroll.Pattern;
                Assert.True(scroll.VerticallyScrollable.Value);
                Assert.False(scroll.HorizontallyScrollable.Value);
                scroll.SetScrollPercent(-1, 100);
                fixture.WaitUntil(() => !entries[^1].IsOffscreen, "The final recovery row is not reachable by scrolling.");
                scroll.SetScrollPercent(-1, 0);
                fixture.WaitUntil(() => scroll.VerticalScrollPercent.Value == 0 && !entries[0].IsOffscreen,
                    "The recovery list did not return to its newest entry.");
                Thread.Sleep(100); // Allow the scroll viewer to repaint before taking visual evidence.
                Capture(fixture, $"{artifactName}-{width}x{height}.png");
            }

            var first = fixture.WaitForElement("DrawingAssistantToolbox")
                .FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem"))[0];
            Assert.StartsWith(LongName, first.Name);
            var bounds = first.BoundingRectangle;
            Mouse.MoveTo((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
            var sourcePath = Path.Combine(fixture.SettingsDirectory, LongName + ".d2cad");
            fixture.WaitUntil(() => fixture.Automation.GetDesktop().FindFirstDescendant(c => c.ByName(sourcePath)
                    .And(c.ByProcessId(fixture.Application.ProcessId))) is not null,
                "Hovering a truncated recovery name did not show its complete source path.");
            fixture.MainWindow.Focus();
            first.Click();
            Assert.True(first.Patterns.SelectionItem.Pattern.IsSelected.Value);
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CadCanvas")) is null or { IsOffscreen: true });
            var recoverButton = first.FindFirstDescendant(c => c.ByAutomationId("OpenRecoveryEntryIconButton"))!;
            first.Focus();
            fixture.WaitUntil(() => first.Properties.HasKeyboardFocus.Value, "The selected recovery row did not receive keyboard focus.");
            Keyboard.Type(VirtualKeyShort.TAB);
            fixture.WaitUntil(() => recoverButton.Properties.HasKeyboardFocus.Value, "Tab did not focus the selected row's recovery button.");
            Capture(fixture, artifactName + "-keyboard-focus.png");
            Keyboard.Type(VirtualKeyShort.SPACE);
            Assert.False(fixture.WaitForElement("CadCanvas").IsOffscreen);
            var input = fixture.WaitForElement("CommandLineInput").AsTextBox();
            input.Focus();
            input.Text = "TOOL list_documents {}";
            fixture.MainWindow.Focus();
            input.Focus();
            Assert.True(input.Properties.HasKeyboardFocus.Value);
            Keyboard.Type(VirtualKeyShort.RETURN);
            var output = fixture.WaitForElement("CommandLineOutput");
            fixture.WaitUntil(() => output.Properties.HelpText.ValueOrDefault?.Contains("documents") == true,
                "The recovered document inventory did not appear.");
            var result = output.Properties.HelpText.ValueOrDefault!;
            // Command results escape non-ASCII text. Decode JSON before checking the complete document name.
            using var inventory = JsonDocument.Parse(result[result.IndexOf('{')..]);
            var document = Assert.Single(inventory.RootElement.GetProperty("result").GetProperty("documents").EnumerateArray());
            Assert.Equal($"{LongName} · {suffix}", document.GetProperty("name").GetString());
            Assert.Equal("", document.GetProperty("file_path").GetString());
            Assert.True(document.GetProperty("is_active").GetBoolean());
            Assert.Equal(16, Directory.GetFiles(Path.Combine(fixture.SettingsDirectory, "Recovery"), "*.d2cad").Length);
            Capture(fixture, $"{artifactName}-opened.png");
        }
        finally { Capture(fixture, artifactName + "-final.png"); SaveBindingTrace(fixture, artifactName); }
        AssertBindingTraceIsEmpty(fixture);
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void EmptyRecoveryToolboxStaysConciseOnWelcomeAndDocumentPages()
    {
        using var fixture = new CadApplicationFixture(captureBindings: true);
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            fixture.MainWindow.Patterns.Transform.Pattern.Move(0, 0);
            AssertCadTabsHidden(fixture);
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DrawingAssistantToolbox")) is null or { IsOffscreen: true });
            fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
            Assert.False(fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen);
            Assert.Empty(fixture.WaitForElement("DrawingAssistantToolbox").FindAllDescendants(c => c.ByAutomationId("RecoveryEntryItem")));
            Capture(fixture, "recovery-empty-welcome.png");
            fixture.WaitForElement("NewDocumentButton").AsButton().Invoke();
            fixture.WaitForElement("NewBlankDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
            fixture.WaitForElement("CadCanvas");
            AssertCadTabsVisible(fixture);
            fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
            Assert.False(fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen);
            fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
            fixture.WaitForElement("LineToolButton").AsToggleButton().Click();
            fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();
            fixture.WaitForElement("AnnotationRibbonTab").AsTabItem().Select();
            fixture.WaitForElement("DimAlignedToolButton").AsToggleButton().Click();
            fixture.MainWindow.FindFirstDescendant(c => c.ByName("Welcome"))!.Click();
            fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("AnnotationRibbonTab")) is null or { IsOffscreen: true },
                "Returning to Welcome did not hide the annotation tab.");
            AssertCadTabsHidden(fixture);
            Assert.False(fixture.WaitForElement("RecoveryEmptyHint").IsOffscreen);
            Capture(fixture, "recovery-empty-after-drawing.png");
            fixture.MainWindow.FindFirstDescendant(c => c.ByName("Untitled"))!.Click();
            AssertCadTabsVisible(fixture);
        }
        finally { SaveBindingTrace(fixture, "recovery-empty"); }
        AssertBindingTraceIsEmpty(fixture);
    }

    [Theory]
    [InlineData(true, 2052)]
    [InlineData(false, 1033)]
    [Trait("Category", "UiAutomation")]
    public void RecoverySelectionIsSeparateFromOpeningAndRepeatedOpenKeepsTheEditedDocument(bool dark, int culture)
    {
        using var fixture = new CadApplicationFixture(directory => SeedRecovery(directory, dark, culture), captureBindings: true);
        var name = dark ? "list-states-dark" : "list-states-light";
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            fixture.MainWindow.Patterns.Transform.Pattern.Move(0, 0);
            fixture.MainWindow.Patterns.Transform.Pattern.Resize(1300, 900);
            var rows = fixture.WaitForElement("RecoveryEntriesList")
                .FindAllChildren(c => c.ByAutomationId("RecoveryEntryItem"));
            var first = rows[0];
            var second = rows[1];
            var bounds = second.BoundingRectangle;
            Mouse.MoveTo((int)bounds.Left + 45, (int)(bounds.Top + bounds.Height / 2));
            Thread.Sleep(120);
            Capture(fixture, name + "-recovery-hover.png");
            Mouse.LeftClick();
            Assert.True(second.Patterns.SelectionItem.Pattern.IsSelected.Value);
            Assert.False(first.Patterns.SelectionItem.Pattern.IsSelected.Value);
            Keyboard.Type(VirtualKeyShort.SPACE); // Selecting a row must never recover it.
            Thread.Sleep(120);
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CadCanvas")) is null or { IsOffscreen: true });
            Capture(fixture, name + "-recovery-selected.png");
            bounds = first.BoundingRectangle;
            Mouse.MoveTo((int)bounds.Left + 45, (int)(bounds.Top + bounds.Height / 2));
            Mouse.LeftClick();
            Assert.True(first.Patterns.SelectionItem.Pattern.IsSelected.Value);
            Assert.False(second.Patterns.SelectionItem.Pattern.IsSelected.Value);
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CadCanvas")) is null or { IsOffscreen: true });

            var open = first.FindFirstDescendant(c => c.ByAutomationId("OpenRecoveryEntryIconButton"))!;
            open.Click();
            fixture.WaitForElement("CadCanvas");
            ExecuteTerminal(fixture, "TOOL list_documents {}", "\"documents\"");
            var originalId = Assert.Single(ReadDocuments(fixture)).GetProperty("document_id").GetString();
            ExecuteTerminal(fixture, "TOOL add_line {\"x1\":0,\"y1\":30,\"x2\":50,\"y2\":30}", "created_entity_id");
            open.Click();
            ExecuteTerminal(fixture, "TOOL list_documents {}", "\"documents\"");
            Assert.Equal(originalId, Assert.Single(ReadDocuments(fixture)).GetProperty("document_id").GetString());
            ExecuteTerminal(fixture, "STATUS", "Entities: 2");

            fixture.WaitForElement("NewDocumentButton").AsButton().Invoke();
            fixture.WaitForElement("NewBlankDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
            ExecuteTerminal(fixture, "STATUS", "Entities: 0");
            var documents = fixture.WaitForElement("DocumentExplorerList")
                .FindAllChildren(c => c.ByAutomationId("DocumentExplorerItem"));
            bounds = documents[0].BoundingRectangle;
            Mouse.MoveTo((int)bounds.Left + 45, (int)(bounds.Top + bounds.Height / 2));
            Thread.Sleep(120);
            Capture(fixture, name + "-documents-hover-selected.png");
            open.Click();
            ExecuteTerminal(fixture, "TOOL list_documents {}", "\"documents\"");
            var inventory = ReadDocuments(fixture);
            Assert.Equal(2, inventory.Length);
            Assert.Equal(originalId, Assert.Single(inventory, document => document.GetProperty("is_active").GetBoolean())
                .GetProperty("document_id").GetString());
            ExecuteTerminal(fixture, "STATUS", "Entities: 2");
            first.RightClick();
            fixture.WaitForElement("OpenRecoveryCopyMenuItem", includePopups: true).AsMenuItem().Invoke();
            ExecuteTerminal(fixture, "TOOL list_documents {}", "\"documents\"");
            Assert.Equal(2, ReadDocuments(fixture).Length);
            Assert.Equal(16, Directory.GetFiles(Path.Combine(fixture.SettingsDirectory, "Recovery"), "*.d2cad").Length);
            Capture(fixture, name + "-reactivated.png");
        }
        finally { SaveBindingTrace(fixture, name); }
        AssertBindingTraceIsEmpty(fixture);
    }

    private static JsonElement[] ReadDocuments(CadApplicationFixture fixture)
    {
        var output = fixture.WaitForElement("CommandLineOutput").Properties.HelpText.ValueOrDefault!;
        using var inventory = JsonDocument.Parse(output[output.IndexOf('{')..]);
        return inventory.RootElement.GetProperty("result").GetProperty("documents").EnumerateArray()
            .Select(document => document.Clone()).ToArray();
    }

    private static void SeedRecovery(string directory, bool dark, int culture)
    {
        File.WriteAllText(Path.Combine(directory, "user-settings.json"), JsonSerializer.Serialize(new
        {
            Version = 2, General = new { IsDarkTheme = dark, CultureLcid = culture }
        }));
        using var store = new CadRecoveryStore(Path.Combine(directory, "Recovery"), trackSession: true);
        for (var index = 0; index < 16; index++)
        {
            var name = index == 15 ? LongName : $"Drawing {index + 1:00}";
            var editor = new CadEditor(CadDocument.Create(name));
            editor.AddLine(default, new(100, 20));
            store.SaveAsync(editor, Path.Combine(directory, name + ".d2cad")).GetAwaiter().GetResult();
        }
    }

    private static void AssertCadTabsHidden(CadApplicationFixture fixture)
    {
        foreach (var id in new[] { "DrawRibbonTab", "EditRibbonTab", "AnnotationRibbonTab" })
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId(id)) is null or { IsOffscreen: true }, $"{id} remained visible on Welcome.");
    }

    private static void AssertCadTabsVisible(CadApplicationFixture fixture)
    {
        foreach (var id in new[] { "DrawRibbonTab", "EditRibbonTab", "AnnotationRibbonTab" })
            Assert.False(fixture.WaitForElement(id).IsOffscreen);
    }

    private static void Capture(CadApplicationFixture fixture, string name)
    {
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        fixture.MainWindow.CaptureToFile(Path.Combine(directory, name));
    }

    private static void SaveBindingTrace(CadApplicationFixture fixture, string name)
    {
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        if (File.Exists(fixture.BindingTracePath)) File.Copy(fixture.BindingTracePath, Path.Combine(directory, name + "-bindings.log"), overwrite: true);
    }

    private static void AssertBindingTraceIsEmpty(CadApplicationFixture fixture)
    {
        Assert.True(File.Exists(fixture.BindingTracePath), "WPF binding diagnostics did not initialize.");
        Assert.Equal("", fixture.ReadBindingTrace());
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
