using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact]
    [Trait("Category", "UiAutomation")]
    public void NarrowDrawingRibbonScrollsToItsLastGroupAndKeyboardFocusReturnsToTheFirst()
    {
        CreateNewDocument();
        fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(600, 700);
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        var ribbon = fixture.WaitForElement("DrawRibbonScrollViewer");
        fixture.WaitUntil(() => ribbon.Patterns.Scroll.Pattern.HorizontallyScrollable.Value,
            "The drawing ribbon did not expose horizontal overflow in a narrow window.");
        ribbon.Patterns.Scroll.Pattern.SetScrollPercent(100, -1);
        var settings = fixture.WaitForElement("DocumentSettingsButton");
        fixture.WaitUntil(() => !settings.IsOffscreen && settings.BoundingRectangle.Right <= ribbon.BoundingRectangle.Right,
            "The last drawing group was not reachable by horizontal scrolling.");
        CaptureScreenshot("drawing-ribbon-scrolled-600x700.png", ribbonOnly: true);
        settings.Focus();
        fixture.WaitUntil(() => settings.Properties.HasKeyboardFocus.ValueOrDefault,
            "The last drawing group did not receive focus before navigating back.");
        var selection = fixture.WaitForElement("SelectToolButton");
        selection.Focus();
        fixture.WaitUntil(() => selection.Properties.HasKeyboardFocus.ValueOrDefault && !selection.IsOffscreen &&
            selection.BoundingRectangle.Left >= ribbon.BoundingRectangle.Left &&
            selection.BoundingRectangle.Right <= ribbon.BoundingRectangle.Right,
            "Keyboard focus did not bring the first drawing tool back into view.");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void CanvasBackspaceAndTerminalEmptyEnterPreserveAndCompleteTheSameDrawing()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "PL", "Polyline mode active.");
        ExecuteCommand(input, "0,0");
        ExecuteCommand(input, "20,0");
        var canvas = fixture.WaitForElement("CadCanvas");
        canvas.Focus();
        fixture.WaitUntil(() => canvas.Properties.HasKeyboardFocus.ValueOrDefault,
            "Canvas did not receive focus for Backspace.");
        Keyboard.Type(VirtualKeyShort.BACK);
        Keyboard.Type(VirtualKeyShort.RETURN);
        CaptureScreenshot("drawing-step-after-backspace-enter.png");
        var statusSurface = fixture.WaitForElement("MainStatusBarSurface");
        var diagnosticsDirectory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(diagnosticsDirectory))
        {
            File.WriteAllText(Path.Combine(diagnosticsDirectory, "drawing-step-status.txt"),
                $"Surface: {statusSurface.BoundingRectangle}, offscreen={statusSurface.IsOffscreen}" + Environment.NewLine +
                string.Join(Environment.NewLine, statusSurface.FindAllDescendants().Select(element =>
                    $"{element.AutomationId} | {element.Name} | {element.BoundingRectangle} | offscreen={element.IsOffscreen}")) +
                Environment.NewLine + fixture.ReadBindingTrace());
        }
        Assert.Contains("Polyline", fixture.WaitForElement("CurrentToolStatusText").Name);
        var step = fixture.WaitForElement("CurrentDrawingStepText");
        fixture.WaitUntil(() => step.Name.Contains("Specify 1 more point"),
            "Backspace did not remove the pending second point or Enter did not display the missing-point error.");
        Assert.False(step.IsOffscreen);
        CaptureScreenshot("drawing-step-missing-point.png");

        ExecuteCommand(input, "20,20");
        ExecuteCommand(input, "");
        fixture.WaitUntil(() => CountOutputMatches(output, "Current step accepted.") > 0,
            "Empty terminal Enter did not finish the existing polyline.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void SaveShortcutWorksWhileTerminalInputRetainsKeyboardFocus()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        input.Text = "unsubmitted text";
        input.Focus();
        fixture.WaitUntil(() => input.Properties.HasKeyboardFocus.ValueOrDefault,
            "Terminal input did not receive focus for saving.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Window? dialog = null;
        fixture.WaitUntil(() => (dialog = fixture.Automation.GetDesktop().FindFirstDescendant(
            c => c.ByClassName("#32770").And(c.ByProcessId(fixture.Application.ProcessId)))?.AsWindow()) is not null,
            "Ctrl+S from the terminal did not open the Save dialog.");
        dialog!.Focus();
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.Automation.GetDesktop().FindFirstDescendant(
            c => c.ByClassName("#32770").And(c.ByProcessId(fixture.Application.ProcessId))) is null,
            "The Save dialog did not close.");
        Assert.Equal("unsubmitted text", input.Text);
        fixture.EnsureApplicationIsRunning();
    }
}
