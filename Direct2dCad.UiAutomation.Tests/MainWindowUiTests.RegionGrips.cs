using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Theory][InlineData(false)][InlineData(true)][Trait("Category", "UiAutomation")]
    public void BooleanRegionGripsPreviewCancelCommitAndUndo(bool corner)
    {
        CreateNewDocument();
        fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(1300, 900);
        fixture.MainWindow.Patterns.Transform.Pattern.Move(40, 40);
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":0,\"center_y\":50,\"radius\":100}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":0,\"center_y\":50,\"radius\":40}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 2 entities.");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("BooleanDifferenceToolButton").AsButton().Click();
        var canvas = fixture.WaitForElement("CadCanvas"); var bounds = canvas.BoundingRectangle;
        var cx = bounds.Left + bounds.Width / 2; var cy = bounds.Top + bounds.Height / 2;
        Mouse.Click(new Point(cx + 100, cy - 50));
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Properties.HelpText.ValueOrDefault?.Contains("Enter to confirm") == true,
            "Boolean preview did not become ready.");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        AssertBounds(-100, -50, 100, 150);
        CaptureScreenshot("region-selected-grips.png");
        var start = new Point(cx + (corner ? 100 : 0), cy - (corner ? 150 : 50));
        var target = new Point(cx + (corner ? 50 : 40), cy - (corner ? 100 : 80));
        Mouse.Click(start); WaitForCursor(32649, "Region grip did not start a drag."); Mouse.MoveTo(target);
        CaptureScreenshot(corner ? "region-scale-preview.png" : "region-move-preview.png");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC); AssertBounds(-100, -50, 100, 150);
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        Mouse.Click(start); WaitForCursor(32649, "Region grip did not restart after cancel.");
        Mouse.MoveTo(target); Mouse.Click(target); WaitForCursor(32512, "Region grip did not finish after the second click.");
        if (corner) AssertBounds(-100, -50, 50, 100); else AssertBounds(-60, -20, 140, 180);
        CaptureScreenshot(corner ? "region-scaled.png" : "region-moved.png");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed."); AssertBounds(-100, -50, 100, 150);
        ExecuteCommandAndWaitForOutput(input, output, "REDO", "Redo completed.");
        if (corner) AssertBounds(-100, -50, 50, 100); else AssertBounds(-60, -20, 140, 180);

        void AssertBounds(double minX, double minY, double maxX, double maxY)
        {
            ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"Region\"}", "min_x");
            var result = ReadCommandOutput(output);
            foreach (var (key, expected) in new[] { ("min_x", minX), ("min_y", minY), ("max_x", maxX), ("max_y", maxY) })
            {
                var match = Regex.Match(result, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?(?:[Ee][+-]?[0-9]+)?)");
                Assert.True(match.Success, "Region query did not return " + key + ": " + result);
                Assert.Equal(expected, double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 5);
            }
        }
    }
}
