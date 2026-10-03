using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact][Trait("Category", "UiAutomation")]
    public void ThreePointCircleRadiusOverlayCanBeClickedAndConfirmedFromTheCanvas()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "CIRCLE 3P", "CircleThreePoint mode active.");
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("0", "0"); EnterDynamicCoordinates("200", "0");
        var radius = fixture.WaitForElement("DynamicInputRadius").AsTextBox();
        Assert.StartsWith("R:", radius.Name);
        var bounds = radius.BoundingRectangle;
        Mouse.Click(new Point(bounds.Left + bounds.Width * 2 / 3, bounds.Top + bounds.Height / 2));
        fixture.WaitUntil(() => radius.Properties.HasKeyboardFocus.Value, "The radius overlay could not be clicked.");
        Keyboard.Type("125");
        fixture.WaitUntil(() => radius.Text == "125", "The clicked radius field did not accept typing.");
        CaptureScreenshot("circle-three-point-radius-input.png");
        var canvas = fixture.WaitForElement("CadCanvas").BoundingRectangle;
        Mouse.Click(new Point(canvas.Right - 30, canvas.Bottom - 30));
        fixture.WaitForElement("DynamicInputX");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"Circle\"}", "radius");
        WaitForCircleRadius(output, 125);
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        AssertCurveBindingsClean("circle-three-point");
    }

    [Fact][Trait("Category", "UiAutomation")]
    public void EllipseArcAxesAndSweepHavePrefixesAndSupportTabAndMouseEditingAtTheirGeometry()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "ELLIPSE ARC", "EllipseArc mode active.");
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("-100", "0"); EnterDynamicCoordinates("100", "0");
        EnterDynamicFields(("AxisX", "100"), ("AxisY", "60"));
        EnterDynamicFields(("StartAngle", "30"));
        var canvas = fixture.WaitForElement("CadCanvas"); canvas.Focus(); Keyboard.Type(VirtualKeyShort.TAB);
        var x = fixture.WaitForElement("DynamicInputAxisX").AsTextBox();
        fixture.WaitUntil(() => x.Properties.HasKeyboardFocus.Value, "Tab did not select ellipse X semiaxis.");
        Keyboard.Type("120"); Keyboard.Type(VirtualKeyShort.TAB);
        var y = fixture.WaitForElement("DynamicInputAxisY").AsTextBox();
        fixture.WaitUntil(() => y.Properties.HasKeyboardFocus.Value, "Tab did not select ellipse Y semiaxis.");
        Keyboard.Type("80"); Keyboard.Type(VirtualKeyShort.TAB);
        var angle = fixture.WaitForElement("DynamicInputAngle").AsTextBox();
        fixture.WaitUntil(() => angle.Properties.HasKeyboardFocus.Value, "Tab did not select ellipse sweep.");
        Keyboard.Type("150");
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DynamicInputX")));
        Assert.StartsWith("X:", x.Name); Assert.StartsWith("Y:", y.Name); Assert.StartsWith("A:", angle.Name);
        fixture.WaitUntil(() => x.Text == "120" && y.Text == "80" && angle.Text == "150" &&
            !x.BoundingRectangle.IntersectsWith(y.BoundingRectangle) && !x.BoundingRectangle.IntersectsWith(angle.BoundingRectangle) &&
            !y.BoundingRectangle.IntersectsWith(angle.BoundingRectangle), "Ellipse parameters overlapped or lost their typed values.");
        foreach (var field in new[] { x, y, angle })
        {
            Assert.True(canvas.BoundingRectangle.Contains(field.BoundingRectangle));
            Assert.InRange(field.BoundingRectangle.Width, 56, 128);
        }
        var yBounds = y.BoundingRectangle;
        Mouse.Click(new Point(yBounds.Left + yBounds.Width * 2 / 3, yBounds.Top + yBounds.Height / 2));
        fixture.WaitUntil(() => y.Properties.HasKeyboardFocus.Value, "Ellipse semiaxis could not be clicked.");
        CaptureScreenshot("ellipse-arc-parameter-inputs.png");
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"EllipseArc\"}", "sweep_angle_degrees");
        WaitForCurveParameter(output, "radius_x", 120); WaitForCurveParameter(output, "radius_y", 80);
        WaitForCurveParameter(output, "start_angle_degrees", 30); WaitForCurveParameter(output, "sweep_angle_degrees", 150);
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        ExecuteCommandAndWaitForOutput(input, output, "REDO", "Redo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        AssertCurveBindingsClean("ellipse-arc");
    }

    [Fact][Trait("Category", "UiAutomation")]
    public void ArcRadiusAndSweepAreEditableWithoutCoordinateOnlyFields()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "ARC CSA", "ArcCenterStartAngle mode active.");
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("0", "0");
        EnterDynamicFields(("Radius", "150"));
        var angle = fixture.WaitForElement("DynamicInputAngle").AsTextBox();
        Assert.StartsWith("A:", angle.Name);
        var bounds = angle.BoundingRectangle;
        Mouse.Click(new Point(bounds.Left + bounds.Width * 2 / 3, bounds.Top + bounds.Height / 2));
        fixture.WaitUntil(() => angle.Properties.HasKeyboardFocus.Value, "The arc angle overlay could not be clicked.");
        Keyboard.Type("135");
        try
        {
            fixture.WaitUntil(() => angle.Text == "135", "Clicking the live arc angle did not replace its previous value.");
        }
        catch (TimeoutException error)
        {
            CaptureScreenshot("arc-sweep-input-failure.png");
            throw new InvalidOperationException($"{error.Message} Actual: '{angle.Text}', focused: {angle.Properties.HasKeyboardFocus.Value}, before click: {bounds}, after click: {angle.BoundingRectangle}.", error);
        }
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DynamicInputX")));
        CaptureScreenshot("arc-sweep-input.png");
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"Arc\"}", "sweep_angle_degrees");
        WaitForCurveParameter(output, "radius", 150); WaitForCurveParameter(output, "sweep_angle_degrees", 135);
        AssertCurveBindingsClean("arc");
    }

    private void AssertCurveBindingsClean(string name)
    {
        var trace = fixture.ReadBindingTrace();
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is { Length: > 0 } directory)
            File.WriteAllText(Path.Combine(directory, name + ".bindings.log"), trace);
        Assert.True(string.IsNullOrWhiteSpace(trace), "WPF binding diagnostics: " + trace);
    }

    private void WaitForCurveParameter(AutomationElement output, string key, double expected) => fixture.WaitUntil(() =>
        Regex.Matches(output.Properties.HelpText.ValueOrDefault ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*([-+0-9.eE]+)")
            .Any(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) && Math.Abs(actual - expected) < 1e-7),
        $"The committed curve's {key} did not match {expected}.");
}
