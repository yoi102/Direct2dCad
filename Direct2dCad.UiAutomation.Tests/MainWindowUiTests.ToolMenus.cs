using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Theory]
    [Trait("Category", "UiAutomation")]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextMenuCreatesSeparateFitAndCvSplines(bool controlPoints)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.MoveTo(new Point(bounds.Left + 90, bounds.Top + 90));
        Mouse.Click(MouseButton.Right);
        fixture.WaitForElement("DrawContextMenu", includePopups: true).AsMenuItem().Expand();
        var fit = fixture.WaitForElement("SplineContextMenuItem", includePopups: true);
        var cv = fixture.WaitForElement("SplineCvContextMenuItem", includePopups: true);
        Assert.NotEqual(fit.Name, cv.Name);
        CaptureScreenshot(controlPoints ? "cv-spline-context-menu.png" : "fit-spline-context-menu.png");
        (controlPoints ? cv : fit).AsMenuItem().Invoke();
        var toolId = controlPoints ? "SplineCvToolButton" : "SplineToolButton";
        fixture.WaitUntil(() => fixture.WaitForElement(toolId).AsToggleButton().ToggleState == ToggleState.On,
            "The context menu did not activate the corresponding spline ribbon button.");
        foreach (var point in new[] { "0,0", "10,20", "30,20", "40,0" })
            ExecuteCommandAndWaitForOutput(input, output, point, "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "DONE", "Current step accepted");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        EnsureToolboxElement("EntityPropertiesToolbox", "toolbox.entity-properties");
        var grid = fixture.WaitForElement(controlPoints ? "SplineControlPoints" : "SplineFitPoints");
        Assert.Equal(4, grid.Patterns.Grid.Pattern.RowCount.Value);
        AssertCurveBindingsClean("spline-context-menu");
    }
}
