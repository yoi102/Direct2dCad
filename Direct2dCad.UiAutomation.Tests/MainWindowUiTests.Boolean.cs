using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact][Trait("Category", "UiAutomation")]
    public void BooleanRibbonAndContextMenuPreviewCommitCancelAndHistory()
    {
        CreateNewDocument(); var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(1300, 900);
        fixture.MainWindow.Patterns.Transform.Pattern.Move(40, 40);
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();
        var union = fixture.WaitForElement("BooleanUnionToolButton").AsButton();
        Assert.False(union.IsEnabled); Assert.False(fixture.WaitForElement("BooleanIntersectionToolButton").IsEnabled);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":0,\"center_y\":50,\"radius\":100}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        Assert.False(union.IsEnabled);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":0,\"center_y\":50,\"radius\":40}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 2 entities.");
        fixture.WaitUntil(() => union.IsEnabled, "Boolean buttons did not enable for two closed shapes.");
        CaptureScreenshot("boolean-modify-ribbon.png", ribbonOnly: true);
        union.Click(); var canvas = fixture.WaitForElement("CadCanvas");
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Properties.HelpText.ValueOrDefault?.Contains("Enter to confirm") == true,
            "Union preview did not become ready.");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 2");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 2 entities.");
        fixture.WaitForElement("BooleanDifferenceToolButton").AsButton().Click();
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Properties.HelpText.ValueOrDefault?.Contains("subject") == true,
            "Difference did not request a subject.");
        var bounds = canvas.BoundingRectangle;
        Mouse.Click(new Point(bounds.Left + bounds.Width / 2 + 100, bounds.Top + bounds.Height / 2 - 50));
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Properties.HelpText.ValueOrDefault?.Contains("Enter to confirm") == true,
            "Subject pick did not produce the difference preview.");
        CaptureScreenshot("boolean-difference-preview.png"); canvas.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CommonEntityProperties")) is not null,
            "Region properties did not appear after confirmation.");
        Assert.False(union.IsEnabled); CaptureScreenshot("boolean-region-properties.png");
        foreach (var direction in new[] { 1, 1, -1, -1 }) { Mouse.MoveTo(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2); Mouse.Scroll(direction); }
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 2");
        ExecuteCommandAndWaitForOutput(input, output, "REDO", "Redo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 2 entities.");
        Mouse.MoveTo(bounds.Left + 90, bounds.Top + 90); Mouse.Click(MouseButton.Right);
        var menu = fixture.WaitForElement("BooleanOperationsMenu", includePopups: true).AsMenuItem();
        Assert.True(menu.IsEnabled); menu.Expand();
        CaptureScreenshot("boolean-context-menu.png");
        fixture.WaitForElement("BooleanIntersectionMenuItem", includePopups: true).AsMenuItem().Invoke();
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Properties.HelpText.ValueOrDefault?.Contains("Enter to confirm") == true,
            "Context menu did not start intersection preview.");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        CaptureScreenshot("boolean-context-result.png");
    }
}
