using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.Definitions;
using System.Drawing;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact][Trait("Category","UiAutomation")]
    public void NurbsControlPolygonIsVisibleWhileSelectedAndFollowsGripEditing()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output,
            "TOOL add_spline {\"degree\":3,\"control_points\":[{\"x\":-120,\"y\":0},{\"x\":-120,\"y\":87},{\"x\":120,\"y\":87},{\"x\":120,\"y\":0}],\"knots\":[0,0,0,0,1,1,1,1]}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL set_viewport {\"operation\":\"center\",\"x\":0,\"y\":0}", "visible_bounds");
        var canvas = fixture.WaitForElement("CadCanvas");
        // The terminal's grip prompt can resize the canvas while its world origin stays fixed.
        var origin = EditCanvasPoint(canvas, 0, 0);
        Point At(int x, int y) => new(origin.X + x, origin.Y - y);
        Mouse.MoveTo(At(200, -40));
        fixture.WaitUntil(() => CountSplineGuidePixels(At(0, 87)) > 4,
            "Selected CV spline did not display its control polygon.");
        CaptureScreenshot("nurbs-control-polygon-selected.png");

        Mouse.Click(At(-120, 87));
        Mouse.MoveTo(At(-120, 147));
        fixture.WaitUntil(() => CountSplineGuidePixels(At(0, 117)) > 4,
            "The control polygon did not follow the dragged control point.");
        CaptureScreenshot("nurbs-control-polygon-drag.png");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        Mouse.MoveTo(At(200, -40));
        fixture.WaitUntil(() => CountSplineGuidePixels(At(0, 87)) == 0 &&
            CountSplineGuidePixels(At(0, 117)) == 0,
            "Cancelling grip editing did not clear the selection and its preview guides.");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        fixture.WaitUntil(() => CountSplineGuidePixels(At(0, 87)) > 4 &&
            CountSplineGuidePixels(At(0, 117)) == 0,
            "Reselecting the spline did not restore the unmodified control polygon.");
        canvas.Focus();
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => CountSplineGuidePixels(At(0, 87)) == 0,
            "Deselecting the spline left its control polygon visible.");
        CaptureScreenshot("nurbs-control-polygon-cleared.png");
        AssertCurveBindingsClean("nurbs-control-polygon");
    }

    private static int CountSplineGuidePixels(Point point)
    {
        using var bitmap = new Bitmap(24, 8);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(point.X - 12, point.Y - 4, 0, 0, bitmap.Size);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R > 65 && color.G > 65 && color.B > 65) count++;
        }
        return count;
    }

    [Fact][Trait("Category","UiAutomation")]
    public void NurbsWeightEditorCommitsAndUndoesThroughTheRealPropertyGrid()
    {
        CreateNewDocument();var input=GetOrOpenCommandLineInput();var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_spline {\"degree\":2,\"control_points\":[{\"x\":100,\"y\":0},{\"x\":100,\"y\":100},{\"x\":0,\"y\":100}],\"knots\":[0,0,0,1,1,1],\"weights\":[1,0.7071067811865476,1]}","created_entity_id");
        var id=ReadLatestJson(output).GetProperty("result").GetProperty("created_entity_id").GetInt64();
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        EnsureToolboxElement("EntityPropertiesToolbox","toolbox.entity-properties");
        var grid=fixture.WaitForElement("SplineControlPoints");
        fixture.WaitUntil(()=>!grid.IsOffscreen && grid.Patterns.Grid.Pattern.RowCount.Value==3,"NURBS controls were not displayed.");
        var weight=grid.Patterns.Grid.Pattern.GetItem(1,3);weight.Focus();Keyboard.Type(VirtualKeyShort.F2);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL,VirtualKeyShort.KEY_A);Keyboard.Type("1");Keyboard.Type(VirtualKeyShort.RETURN);
        CaptureScreenshot("nurbs-weight-editor.png");
        ExecuteCommandAndWaitForOutput(input,output,$"TOOL get_entity_geometry {{\"entity_id\":{id}}}","geometry");
        var geometry=ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("geometry");
        Assert.Equal(1,geometry.GetProperty("weights")[1].GetDouble());Assert.Equal(2,geometry.GetProperty("degree").GetInt32());
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,$"TOOL get_entity_geometry {{\"entity_id\":{id}}}","geometry");
        geometry=ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("geometry");
        Assert.Equal(Math.Sqrt(.5),geometry.GetProperty("weights")[1].GetDouble(),12);
        AssertCurveBindingsClean("nurbs-weight-editor");
    }

    [Theory][Trait("Category","UiAutomation")]
    [InlineData(false)]
    [InlineData(true)]
    public void NurbsRibbonButtonsCreateSeparateFitAndControlPointGeometry(bool controlPoints)
    {
        CreateNewDocument();var input=GetOrOpenCommandLineInput();var output=fixture.WaitForElement("CommandLineOutput");
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        var fitButton = fixture.WaitForElement("SplineToolButton").AsToggleButton();
        var cvButton = fixture.WaitForElement("SplineCvToolButton").AsToggleButton();
        (controlPoints ? fitButton : cvButton).Click();
        ExecuteCommandAndWaitForOutput(input,output,"-100,-100","Point accepted:");
        (controlPoints ? cvButton : fitButton).Click();
        Assert.Equal(controlPoints ? ToggleState.Off : ToggleState.On, fitButton.ToggleState);
        Assert.Equal(controlPoints ? ToggleState.On : ToggleState.Off, cvButton.ToggleState);
        EnsureToolboxElement("EntityPropertiesToolbox","toolbox.entity-properties");
        foreach(var point in new[]{"0,0","10,20","30,20","40,0"})ExecuteCommandAndWaitForOutput(input,output,point,"Point accepted:");
        ShowDynamicInputOnCanvas();CaptureScreenshot(controlPoints ? "nurbs-cv-preview.png" : "nurbs-fit-preview.png");
        ExecuteCommandAndWaitForOutput(input,output,"DONE","Current step accepted");
        fixture.WaitForElement("CadCanvas").Focus();Keyboard.Type(VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        var grid=fixture.WaitForElement(controlPoints ? "SplineControlPoints" : "SplineFitPoints");
        Assert.Equal(4,grid.Patterns.Grid.Pattern.RowCount.Value);
        Assert.True(grid.Patterns.Grid.Pattern.GetItem(1,controlPoints ? 3 : 2).IsEnabled);
        var hiddenGridId = controlPoints ? "SplineFitPoints" : "SplineControlPoints";
        Assert.DoesNotContain(fixture.MainWindow.FindAllDescendants(cf => cf.ByAutomationId(hiddenGridId)), element => !element.IsOffscreen);
        Assert.DoesNotContain(fixture.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)), element => element.Name.Contains("Convert to control") || element.Name.Contains("转为控制点"));
        CaptureScreenshot(controlPoints ? "nurbs-cv-drawn.png" : "nurbs-fit-drawn.png");
        AssertCurveBindingsClean(controlPoints ? "nurbs-cv-drawing" : "nurbs-fit-drawing");
    }
}
