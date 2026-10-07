using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Theory]
    [InlineData("CENTER", CadCanvasToolMode.EllipseCenter)]
    [InlineData("AXIS", CadCanvasToolMode.EllipseAxisEnd)]
    [InlineData("ARC", CadCanvasToolMode.EllipseArc)]
    [Trait("Category", "UiAutomation")]
    public void TiltedEllipseConstructionKeepsRotationThroughNumericInputAndHistory(string mode, CadCanvasToolMode tool)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "ELLIPSE " + mode, tool + " mode active.");
        ShowDynamicInputOnCanvas();
        ExecuteCommand(input, mode == "CENTER" ? "0,0" : "-40,-40");
        ExecuteCommand(input, "40,40");
        EnterDynamicFields(("AxisX", "30"), ("AxisY", "12"));
        if (mode == "ARC")
        {
            EnterDynamicFields(("StartAngle", "30"));
            EnterDynamicFields(("Angle", "120"));
        }
        var type = mode == "ARC" ? "EllipseArc" : "Ellipse";
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"" + type + "\"}", "rotation_degrees");
        WaitForCurveParameter(output, "rotation_degrees", 45);
        WaitForCurveParameter(output, "radius_x", 30); WaitForCurveParameter(output, "radius_y", 12);
        CaptureScreenshot("tilted-ellipse-" + mode.ToLowerInvariant() + ".png");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        ExecuteCommandAndWaitForOutput(input, output, "REDO", "Redo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"" + type + "\"}", "rotation_degrees");
        WaitForCurveParameter(output, "rotation_degrees", 45);
        WaitForCurveParameter(output, "radius_x", 30); WaitForCurveParameter(output, "radius_y", 12);
        AssertCurveBindingsClean("tilted-ellipse-" + mode.ToLowerInvariant());
    }
}
