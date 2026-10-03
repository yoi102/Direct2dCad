using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.Definitions;

namespace Direct2dCad.UiAutomation.Tests;

[Collection(CadApplicationCollection.Name)]
public sealed class DocumentMenusAndRibbonUiTests
{
    [Fact]
    [Trait("Category", "UiAutomation")]
    public void DocumentMenusCloseTheTargetAndDisableFolderForUnsavedDrawing()
    {
        using var fixture = new CadApplicationFixture(captureBindings: true);
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            NewDrawing(fixture);
            ExecuteTerminal(fixture, "TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":100,\"y2\":0}", "created_entity_id");
            var save = "TOOL save_document " + JsonSerializer.Serialize(new { file_path = Path.Combine(fixture.SettingsDirectory, "Saved drawing.d2cad") });
            ExecuteTerminal(fixture, save, "\"saved\"");
            Assert.True(File.Exists(Path.Combine(fixture.SettingsDirectory, "Saved drawing.d2cad")));
            NewDrawing(fixture);
            var list = fixture.WaitForElement("DocumentExplorerList");
            fixture.WaitUntil(() => list.FindAllChildren(c => c.ByAutomationId("DocumentExplorerItem")).Length == 2, "The document list did not populate.");
            var rows = list.FindAllChildren(c => c.ByAutomationId("DocumentExplorerItem"));
            rows[0].RightClick();
            Assert.True(fixture.WaitForElement("OpenExplorerDocumentFolderMenuItem", includePopups: true).IsEnabled);
            Capture(fixture, "documents-saved-context-menu.png");
            fixture.WaitForElement("CloseExplorerDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
            fixture.WaitUntil(() => list.FindAllChildren(c => c.ByAutomationId("DocumentExplorerItem")).Length == 1, "Closing the inactive drawing did not update the list.");
            ExecuteTerminal(fixture, "STATUS", "Entities: 0");
            list.FindFirstChild(c => c.ByAutomationId("DocumentExplorerItem"))!.RightClick();
            Assert.False(fixture.WaitForElement("OpenExplorerDocumentFolderMenuItem", includePopups: true).IsEnabled);
            Capture(fixture, "documents-unsaved-context-menu.png");
            fixture.WaitForElement("CloseExplorerDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
            fixture.WaitForElement("UnsavedDocumentDialog");
            ClickEnabled(fixture, "CancelCloseDocumentButton");
            fixture.WaitUntil(() => fixture.MainWindow.IsEnabled, "Cancelling document close did not return to the editor.");
            Assert.Single(list.FindAllChildren(c => c.ByAutomationId("DocumentExplorerItem")));
            list.FindFirstChild(c => c.ByAutomationId("DocumentExplorerItem"))!.RightClick();
            fixture.WaitForElement("CloseExplorerDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
            ClickEnabled(fixture, "DiscardDocumentButton");
            fixture.WaitUntil(() => list.FindAllChildren(c => c.ByAutomationId("DocumentExplorerItem")).Length == 0, "Discarding document close did not remove the drawing.");
        }
        finally { Capture(fixture, "documents-context-menu-final.png"); SaveTrace(fixture, "document-menus"); }
        Assert.Equal("", fixture.ReadBindingTrace());
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void SwitchingEditAndAnnotationTabsDoesNotHighlightAnInactiveTool()
    {
        using var fixture = new CadApplicationFixture(captureBindings: true);
        try
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            NewDrawing(fixture);
            fixture.WaitForElement("EditRibbonTab").Click();
            Assert.Equal(ToggleState.Off, fixture.WaitForElement("OffsetToolButton").AsToggleButton().ToggleState);
            Capture(fixture, "ribbon-modify-no-active-tool.png");
            fixture.WaitForElement("AnnotationRibbonTab").Click();
            Assert.Equal(ToggleState.Off, fixture.WaitForElement("DimLinearXToolButton").AsToggleButton().ToggleState);
            Capture(fixture, "ribbon-dimensions-no-active-tool.png");
            ExecuteTerminal(fixture, "STATUS", "Mode: Select");
            fixture.WaitForElement("DimLinearXToolButton").Click();
            Assert.Equal(ToggleState.On, fixture.WaitForElement("DimLinearXToolButton").AsToggleButton().ToggleState);
            fixture.WaitForElement("EditRibbonTab").Click();
            Assert.Equal(ToggleState.Off, fixture.WaitForElement("OffsetToolButton").AsToggleButton().ToggleState);
            fixture.WaitForElement("AnnotationRibbonTab").Click();
            Assert.Equal(ToggleState.On, fixture.WaitForElement("DimLinearXToolButton").AsToggleButton().ToggleState);
            Capture(fixture, "ribbon-dimensions-active-tool.png");
            ExecuteTerminal(fixture, "CANCEL", "Select mode active.");
            Assert.Equal(ToggleState.Off, fixture.WaitForElement("DimLinearXToolButton").AsToggleButton().ToggleState);
        }
        finally { SaveTrace(fixture, "ribbon-mode-focus"); }
        Assert.Equal("", fixture.ReadBindingTrace());
    }

    private static void NewDrawing(CadApplicationFixture fixture)
    {
        fixture.WaitForElement("NewDocumentButton").AsButton().Invoke();
        fixture.WaitForElement("NewBlankDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
        fixture.WaitForElement("CadCanvas");
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

    private static void Capture(CadApplicationFixture fixture, string name)
    {
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        fixture.MainWindow.CaptureToFile(Path.Combine(directory, name));
    }

    private static void SaveTrace(CadApplicationFixture fixture, string name)
    {
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        if (File.Exists(fixture.BindingTracePath)) File.Copy(fixture.BindingTracePath, Path.Combine(directory, name + "-bindings.log"), true);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
