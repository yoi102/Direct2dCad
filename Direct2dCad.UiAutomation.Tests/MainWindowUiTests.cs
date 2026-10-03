using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Specialized;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Direct2dCad.UiAutomation.Tests;

[Collection(CadApplicationCollection.Name)]
public sealed partial class MainWindowUiTests : IDisposable
{
    private readonly CadApplicationFixture fixture = CreateFixture();

    private static CadApplicationFixture CreateFixture()
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        return new CadApplicationFixture();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    public void Dispose() => fixture.Dispose();

    [Fact][Trait("Category","UiAutomation")]
    public void AnnotationRibbonPreviewPlacementPropertiesAndTemplateAreUsable()
    {
        CreateNewDocument();var input=GetOrOpenCommandLineInput();var output=fixture.WaitForElement("CommandLineOutput");
        fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(900, 700);
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":100,\"y2\":0}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        fixture.WaitForElement("AnnotationRibbonTab").AsTabItem().Select();fixture.WaitForElement("DimAlignedToolButton").AsToggleButton().Click();
        fixture.WaitUntil(()=>fixture.WaitForElement("CurrentToolStatusText").Name.Contains("Aligned"),"Annotation mode did not appear in the status bar.");
        AssertAnnotationAssistantHidden();
        CaptureScreenshot("annotation-ribbon-active.png", ribbonOnly: true);
        ExecuteCommandAndWaitForOutput(input, output, "50,20", "Point accepted");
        fixture.WaitForElement("CadCanvas").Focus();Keyboard.Type(VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL select_entities {\"entity_ids\":[2]}","success");
        var propertyScroll = fixture.WaitForElement("EntityPropertiesScrollViewer");
        Assert.True(propertyScroll.Patterns.Scroll.Pattern.VerticallyScrollable.Value);
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("ApplyDimensionButton")));
        var font = ScrollPropertyIntoView("DimensionShapeFontSelector").AsComboBox();
        font.Select("Simplex"); font.Collapse();
        var arrow = ScrollPropertyIntoView("DimensionArrowSelector").AsComboBox();
        arrow.Select("Slash"); arrow.Collapse();
        var height = ScrollPropertyIntoView("DimensionTextHeightInput").AsTextBox(); height.Focus(); height.Text = "3";
        var size = ScrollPropertyIntoView("DimensionArrowSizeInput").AsTextBox(); size.Focus(); size.Text = "4";
        fixture.WaitForElement("DimensionDetailsSection").Patterns.ExpandCollapse.Pattern.Expand();
        var scale = ScrollPropertyIntoView("DimensionAnnotationScaleInput").AsTextBox(); scale.Focus(); scale.Text = "2";
        var lineWeight = ScrollPropertyIntoView("DimensionLineWeightInput").AsTextBox();
        lineWeight.Focus();
        lineWeight.Text = "0.35";
        Assert.True(lineWeight.Properties.HasKeyboardFocus.Value);
        CaptureScreenshot("annotation-properties-scrolled-900x700.png");
        var text=ScrollPropertyIntoView("DimensionTextOverrideInput").AsTextBox();
        text.Focus(); Keyboard.Type("CHECK");
        fixture.WaitUntil(() => text.Text == "CHECK", "Continuous input did not update the text override.");
        Assert.True(text.Properties.HasKeyboardFocus.Value);
        ExecuteCommandAndWaitForOutput(input,output,"TOOL list_entities {\"type\":\"Dimension\"}","CHECK");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");fixture.WaitUntil(()=>text.Text=="","Undo did not refresh annotation properties.");
        Assert.Equal("Simplex", fixture.WaitForElement("DimensionShapeFontSelector").AsComboBox().SelectedItem?.Text);
        Assert.Equal("Slash", fixture.WaitForElement("DimensionArrowSelector").AsComboBox().SelectedItem?.Text);
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        fixture.WaitUntil(() => lineWeight.Text == "0.18", "Undo did not restore the previous line weight.");
        ExecuteCommandAndWaitForOutput(input,output,"REDO","Redo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"REDO","Redo completed.");
        Assert.Equal("Simplex", fixture.WaitForElement("DimensionShapeFontSelector").AsComboBox().SelectedItem?.Text);
        Assert.Equal("Slash", fixture.WaitForElement("DimensionArrowSelector").AsComboBox().SelectedItem?.Text);
        Assert.Equal("0.35", fixture.WaitForElement("DimensionLineWeightInput").AsTextBox().Text);
        var style = ScrollPropertyIntoView("DimensionStyleSelector").AsComboBox();
        style.Select("Fine"); style.Collapse();
        fixture.WaitUntil(() => height.Text == "2" && size.Text == "2" && lineWeight.Text == "0.13",
            "The style preset did not update all annotation properties immediately.");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        fixture.WaitUntil(() => height.Text == "3" && size.Text == "4" && lineWeight.Text == "0.35",
            "One undo did not restore the whole previous style.");
        Assert.Equal("CHECK", text.Text);
        propertyScroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        Thread.Sleep(100);
        var viewport = propertyScroll.BoundingRectangle;
        Mouse.MoveTo((int)(viewport.Left + viewport.Width / 2), (int)(viewport.Top + viewport.Height / 2));
        Mouse.Scroll(2);
        fixture.WaitUntil(() => propertyScroll.Patterns.Scroll.Pattern.VerticalScrollPercent.Value < 100,
            "The entity's nested scroll viewer swallowed mouse-wheel input.");
        ScrollPropertyIntoView("DimensionShapeFontSelector");
        CaptureScreenshot("annotation-font-and-arrows-900x700.png");
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(1300, 900);
        fixture.MainWindow.Focus();
        ToggleToolboxShortcut(VirtualKeyShort.KEY_E);
        ScrollPropertyIntoView("DimensionArrowSizeInput");
        foreach (var id in new[] { "DimensionShapeFontSelector", "DimensionArrowSelector", "DimensionTextHeightInput", "DimensionArrowSizeInput" })
            Assert.False(fixture.WaitForElement(id).IsOffscreen);
        CaptureScreenshot("annotation-font-and-arrows-1300x900.png");
        var screenshot=Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if(!string.IsNullOrWhiteSpace(screenshot)){Directory.CreateDirectory(screenshot);fixture.MainWindow.CaptureToFile(Path.Combine(screenshot,"annotation-properties.png"));}
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        CaptureScreenshot("file-ribbon-icons.png", ribbonOnly: true);
        fixture.WaitForElement("NewDocumentButton").AsButton().Invoke();
        fixture.WaitForElement("NewA4TemplateButton", includePopups: true).AsMenuItem().Invoke();
        WaitForRibbonMenuClosed("NewDocumentMenu");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 0");fixture.EnsureApplicationIsRunning();
    }

    [Fact][Trait("Category", "UiAutomation")]
    public void AnnotationAssistantIsHiddenAndConfirmedPointsRemainVisibleUntilCancelled()
    {
        CreateNewDocument();
        fixture.WaitForElement("AnnotationRibbonTab").AsTabItem().Select();
        var status = fixture.WaitForElement("CurrentToolStatusText");
        foreach (var id in new[] { "DimLinearXToolButton", "DimLinearYToolButton", "DimAlignedToolButton",
                     "DimRadiusToolButton", "DimDiameterToolButton", "DimAngularToolButton", "LeaderToolButton" })
        {
            fixture.WaitForElement(id).AsToggleButton().Click();
            AssertAnnotationAssistantHidden();
            Assert.False(fixture.WaitForElement("AnnotationParametersSurface").IsOffscreen);
            Assert.False(string.IsNullOrWhiteSpace(status.Properties.HelpText.ValueOrDefault));
        }
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        var first = new Point((int)(bounds.Left + bounds.Width * .3), (int)(bounds.Top + bounds.Height * .35));
        var next = new Point((int)(bounds.Left + bounds.Width * .65), (int)(bounds.Top + bounds.Height * .65));
        foreach (var id in new[] { "DimAlignedToolButton", "DimAngularToolButton", "LeaderToolButton" })
        {
            fixture.WaitForElement(id).AsToggleButton().Click();
            canvas.Focus();
            var prompt = status.Properties.HelpText.ValueOrDefault;
            Assert.Equal(0, CountAnchorMarkerPixels(first));
            Mouse.Click(first); Mouse.MoveTo(next);
            fixture.WaitUntil(() => CountAnchorMarkerPixels(first) > 8,
                "The confirmed first point has no persistent marker.");
            Assert.NotEqual(prompt, status.Properties.HelpText.ValueOrDefault);
            CaptureScreenshot($"annotation-first-point-{id}.png");
            var tabBounds = fixture.WaitForElement("AnnotationRibbonTab").BoundingRectangle;
            Mouse.MoveTo((int)tabBounds.Left + 10, (int)tabBounds.Top + 10);
            fixture.WaitUntil(() => CountAnchorMarkerPixels(first) > 8,
                "The confirmed point marker disappeared when the pointer left the canvas.");
            canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
            fixture.WaitUntil(() => CountAnchorMarkerPixels(first) == 0,
                "Cancel left a confirmed point marker behind.");
            fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select",
                "Leaving annotation did not restore selection mode.");
        }
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("LineToolButton").AsToggleButton().Click();
        ShowDynamicInputOnCanvas();
        Assert.False(fixture.WaitForElement("DynamicInputX").IsOffscreen);
    }

    private static int CountAnchorMarkerPixels(Point point)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(point.X - 8, point.Y - 8, 0, 0, bitmap.Size);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.B > 150 && color.G > color.R + 40 && color.B > color.R + 60) count++;
        }
        return count;
    }

    private void AssertAnnotationAssistantHidden() => fixture.WaitUntil(() =>
        new[] { "CurrentToolModeText", "DrawingStepInput", "SubmitDrawingStepButton" }.All(id =>
            fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId(id)) is null or { IsOffscreen: true }),
        "Annotation still displays the drawing assistant controls.");

    [Fact][Trait("Category", "UiAutomation")]
    public void CanvasDynamicInputSupportsTabTypingPreviewEdgesAndExactCircleUndo()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("CircleCenterDiameterToolButton").AsToggleButton().Click();
        ShowDynamicInputOnCanvas();
        AssertAnnotationAssistantHidden();
        var canvas = fixture.WaitForElement("CadCanvas");
        Keyboard.Type(VirtualKeyShort.TAB);
        var x = fixture.WaitForElement("DynamicInputX").AsTextBox();
        fixture.WaitUntil(() => x.Properties.HasKeyboardFocus.Value, "Tab did not select the X field.");
        Keyboard.Type("10"); Keyboard.Type(VirtualKeyShort.TAB);
        var y = fixture.WaitForElement("DynamicInputY").AsTextBox();
        fixture.WaitUntil(() => y.Properties.HasKeyboardFocus.Value, "Tab did not select the Y field.");
        Keyboard.Type("20");
        Keyboard.Press(VirtualKeyShort.SHIFT); Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Release(VirtualKeyShort.SHIFT);
        fixture.WaitUntil(() => x.Properties.HasKeyboardFocus.Value, "Shift+Tab did not return to the X field.");
        Keyboard.Type(VirtualKeyShort.RETURN);
        var diameter = fixture.WaitForElement("DynamicInputDiameter").AsTextBox();
        canvas.Focus(); Keyboard.Type("220");
        fixture.WaitUntil(() => diameter.Text == "220" && diameter.Properties.HasKeyboardFocus.Value,
            "Typing on the canvas did not activate diameter input.");
        var bounds = canvas.BoundingRectangle;
        Mouse.MoveTo((int)bounds.Right - 3, (int)bounds.Bottom - 3);
        fixture.WaitUntil(() => diameter.Text == "220" && diameter.BoundingRectangle.Right <= bounds.Right &&
            diameter.BoundingRectangle.Bottom <= bounds.Bottom, "The locked input moved off the canvas edge.");
        Assert.True(diameter.BoundingRectangle.Width <= 60, "A short number still has an unnecessarily long field.");
        CaptureScreenshot("dynamic-input-diameter-preview.png");
        Mouse.MoveTo((int)(bounds.Left + bounds.Width * .4), (int)(bounds.Top + bounds.Height * .45));
        CaptureScreenshot("dynamic-input-diameter.png");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        diameter.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"Circle\"}", "radius");
        WaitForCircleRadius(output, 110);
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        ExecuteCommandAndWaitForOutput(input, output, "REDO", "Redo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ShowDynamicInputOnCanvas();
        EnterDynamicCoordinates("30", "40");
        diameter = fixture.WaitForElement("DynamicInputDiameter").AsTextBox();
        diameter.Focus(); diameter.Text = "-1"; Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => !string.IsNullOrWhiteSpace(fixture.WaitForElement("DynamicInputError").Name),
            "Invalid diameter did not show an input error.");
        Assert.True(diameter.Properties.HasKeyboardFocus.Value);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Esc did not cancel dynamic input.");
        Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DynamicInputDiameter")) is null or { IsOffscreen: true });
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");

        fixture.WaitForElement("CircleCenterRadiusToolButton").AsToggleButton().Click();
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("50", "60");
        canvas.Focus(); Keyboard.Type("12.5");
        var radius = fixture.WaitForElement("DynamicInputRadius").AsTextBox();
        fixture.WaitUntil(() => radius.Text == "12.5", "Radius did not receive numeric typing.");
        var shortWidth = radius.BoundingRectangle.Width;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A); Keyboard.Type("12.3456789");
        fixture.WaitUntil(() => radius.Text == "12.3456789" && radius.BoundingRectangle.Width > shortWidth,
            "The field did not expand to show a longer number.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A); Keyboard.Type("12.5");
        fixture.WaitUntil(() => radius.Text == "12.5" && radius.BoundingRectangle.Width == shortWidth,
            "The field did not shrink after returning to a short number.");
        // Clicking where the floating field was drawn must reach the canvas and accept its fixed value.
        var radiusBounds = radius.BoundingRectangle;
        Mouse.Click(new Point(radiusBounds.Left + radiusBounds.Width / 2, radiusBounds.Top + radiusBounds.Height / 2));
        fixture.WaitForElement("DynamicInputX");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 2");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"Circle\"}", "radius");
        WaitForCircleRadius(output, 12.5);
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
    }

    private void WaitForCircleRadius(AutomationElement output, double expected) => fixture.WaitUntil(() =>
        System.Text.RegularExpressions.Regex.Matches(output.Properties.HelpText.ValueOrDefault ?? "", "\"radius\"\\s*:\\s*([-+0-9.eE]+)")
            .Any(match => double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var actual) && Math.Abs(actual - expected) < 1e-8),
        $"The circle's radius did not match the confirmed input ({expected}).");

    [Fact][Trait("Category", "UiAutomation")]
    public void RectangleDynamicNumbersStayByTheirCorrespondingEdgesWithoutAPromptPanel()
    {
        CreateNewDocument();
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("RectangleToolButton").AsToggleButton().Click();
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("0", "0");
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.MoveTo(bounds.Left + bounds.Width * 3 / 4, bounds.Top + bounds.Height * 3 / 4);
        canvas.Focus(); Keyboard.Type("200"); Keyboard.Type(VirtualKeyShort.TAB);
        var height = fixture.WaitForElement("DynamicInputHeight").AsTextBox();
        fixture.WaitUntil(() => height.Properties.HasKeyboardFocus.Value, "Tab did not select rectangle height.");
        Keyboard.Type("120");
        var width = fixture.WaitForElement("DynamicInputWidth").AsTextBox();
        fixture.WaitUntil(() => width.Text == "200" && height.Text == "120" &&
            width.BoundingRectangle.Top > height.BoundingRectangle.Bottom &&
            height.BoundingRectangle.Left > width.BoundingRectangle.Right,
            "Width and height did not follow the horizontal and vertical rectangle edges.");
        foreach (var field in new[] { width, height })
        {
            Assert.True(field.BoundingRectangle.Width <= 60 && field.BoundingRectangle.Height <= 26);
            Assert.True(bounds.Contains(field.BoundingRectangle));
        }
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DynamicInputPrompt")));
        try
        {
            fixture.WaitUntil(() => CountDynamicGuidePixels(width.BoundingRectangle) > 6,
                "The rectangle's dotted dimension guide did not render beside its number.");
        }
        finally { CaptureScreenshot("dynamic-input-rectangle.png"); }
        Keyboard.Type(VirtualKeyShort.RETURN);
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
    }

    private static int CountDynamicGuidePixels(Rectangle field)
    {
        using var bitmap = new Bitmap(30, 6);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(field.Left - 35, field.Top + field.Height / 2 - 3, 0, 0, bitmap.Size);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            // A one-DIP line between pixels is antialiased to ~55 on the black canvas.
            // Keep the grid (~33) outside the range while accepting those guide pixels.
            if (color.R is > 40 and < 190 && Math.Abs(color.R - color.G) < 5 && Math.Abs(color.R - color.B) < 5) count++;
        }
        return count;
    }

    [Fact][Trait("Category", "UiAutomation")]
    public void BrokenLinePiecesRemainVisibleAndSelectableAcrossWheelZoomAndUndoRedo()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "LINE", "Line mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "-120,50", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "120,50", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "CANCEL", "Select mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("BreakToolButton").AsToggleButton().Click();
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        Mouse.Click(new Point(center.X - 20, center.Y - 50));
        Mouse.Click(new Point(center.X + 20, center.Y - 50));
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Break did not return to selection.");
        ClearSelection();
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 2");

        void ClearSelection() => ExecuteCommandAndWaitForOutput(input, output, "TOOL clear_selection {}", "selected_entity_ids");
        Rectangle[] Segments(int count)
        {
            Rectangle[] segments = [];
            fixture.WaitUntil(() => (segments = FindHorizontalGreenSegments(bounds)).Length == count,
                $"Expected {count} visible line pieces after break/history.");
            return segments;
        }
        void PickEach(Rectangle[] segments)
        {
            foreach (var segment in segments)
            {
                Mouse.Click(new Point(segment.Left + segment.Width / 2, segment.Top));
                ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Selected: 1");
                ClearSelection();
            }
            var gap = new Point((segments[0].Right + segments[1].Left) / 2, segments[0].Top);
            Mouse.Click(gap);
            ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Selected: 0");
        }

        try
        {
            var pieces = Segments(2);
            foreach (var direction in new[] { 1, 1, -1, -1 })
            {
                var previousWidth = pieces.Sum(p => p.Width);
                Mouse.MoveTo(center); Mouse.Scroll(direction);
                fixture.WaitUntil(() => (pieces = FindHorizontalGreenSegments(bounds)).Length == 2 &&
                    Math.Abs(pieces.Sum(p => p.Width) - previousWidth) > 3,
                    "Wheel zoom lost one of the split entities or did not update the frame.");
                PickEach(pieces);
            }
            Mouse.MoveTo(bounds.Left + 25, bounds.Top - 10);
            ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 2");
            CaptureScreenshot("break-after-wheel-zoom.png");
            ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
            var restored = Assert.Single(Segments(1));
            Mouse.Click(new Point(restored.Left + restored.Width / 2, restored.Top));
            ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Selected: 1");
            ClearSelection();
            ExecuteCommandAndWaitForOutput(input, output, "REDO", "Redo completed.");
            PickEach(Segments(2));
            Mouse.MoveTo(bounds.Left + 25, bounds.Top - 10);
            ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 2");
            CaptureScreenshot("break-after-redo.png");
        }
        catch
        {
            CaptureScreenshot("break-zoom-or-pick-failed.png");
            throw;
        }
    }

    private static Rectangle[] FindHorizontalGreenSegments(Rectangle canvas)
    {
        using var bitmap = new Bitmap(canvas.Width, canvas.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(canvas.Left, canvas.Top, 0, 0, bitmap.Size);
        bool IsGreen(int x, int y)
        {
            var color = bitmap.GetPixel(x, y);
            return color.G > 70 && color.G > color.R + 40 && color.G > color.B + 40;
        }
        var bestRow = 0; var bestCount = 0;
        for (var y = 30; y < bitmap.Height - 30; y++)
        {
            var count = 0;
            for (var x = 30; x < bitmap.Width - 30; x++) if (IsGreen(x, y)) count++;
            if (count > bestCount) { bestRow = y; bestCount = count; }
        }
        var segments = new List<Rectangle>(); var start = -1;
        for (var x = 30; x < bitmap.Width - 29; x++)
        {
            if (x < bitmap.Width - 30 && IsGreen(x, bestRow)) { if (start < 0) start = x; }
            else if (start >= 0)
            {
                if (x - start > 20) segments.Add(new(canvas.Left + start, canvas.Top + bestRow, x - start, 1));
                start = -1;
            }
        }
        return segments.ToArray();
    }

    [Fact][Trait("Category", "UiAutomation")]
    public void SplineDynamicLengthAndAngleRemainCompactByThePointer()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "SPLINE", "Spline mode active.");
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("0", "0");
        EnterDynamicFields(("Length", "200"), ("Angle", "0"));
        var canvas = fixture.WaitForElement("CadCanvas"); var bounds = canvas.BoundingRectangle;
        var pointer = new Point(bounds.Left + bounds.Width * 3 / 5, bounds.Top + bounds.Height * 2 / 5);
        Mouse.MoveTo(pointer); canvas.Focus(); Keyboard.Type("130"); Keyboard.Type(VirtualKeyShort.TAB);
        var angle = fixture.WaitForElement("DynamicInputAngle").AsTextBox();
        fixture.WaitUntil(() => angle.Properties.HasKeyboardFocus.Value, "Tab did not select spline angle.");
        Keyboard.Type("160");
        var length = fixture.WaitForElement("DynamicInputLength").AsTextBox();
        fixture.WaitUntil(() => length.Text == "130" && angle.Text == "160" &&
            Math.Abs(length.BoundingRectangle.Top - angle.BoundingRectangle.Top) <= 1 &&
            angle.BoundingRectangle.Left >= length.BoundingRectangle.Right,
            "Spline's numeric fields did not stay in a compact row beside the pointer.");
        var icon = fixture.WaitForElement("CursorToolModeIcon");
        var iconBounds = icon.BoundingRectangle; var lengthBounds = length.BoundingRectangle;
        Assert.True(iconBounds.Right <= lengthBounds.Left);
        Assert.InRange(Math.Abs(iconBounds.Top + iconBounds.Height / 2.0 - lengthBounds.Top - lengthBounds.Height / 2.0), 0, 2);
        Assert.InRange(lengthBounds.Left - pointer.X, 45, 65);
        Assert.True(length.BoundingRectangle.Height <= 26 && angle.BoundingRectangle.Height <= 26);
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DynamicInputPrompt")));
        Keyboard.Type(VirtualKeyShort.TAB);
        fixture.WaitUntil(() => length.Properties.HasKeyboardFocus.Value, "Tab did not wrap to spline length.");
        CaptureScreenshot("dynamic-input-spline.png");
        Keyboard.Type(VirtualKeyShort.RETURN); Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
    }

    private void ShowDynamicInputOnCanvas()
    {
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.MoveTo((int)(bounds.Left + bounds.Width * .45), (int)(bounds.Top + bounds.Height * .45));
        canvas.Focus();
        fixture.WaitUntil(() => fixture.MainWindow.FindAllDescendants(c => c.ByControlType(ControlType.Edit))
                .Any(element => element.AutomationId.StartsWith("DynamicInput", StringComparison.Ordinal) && !element.IsOffscreen),
            "Dynamic input did not appear on the canvas.");
    }
    private void EnterDynamicCoordinates(string x, string y) => EnterDynamicFields(("X", x), ("Y", y));
    private void EnterDynamicFields(params (string Key, string Value)[] fields)
    {
        TextBox? last = null;
        foreach (var (key, value) in fields)
        {
            last = fixture.WaitForElement("DynamicInput" + key).AsTextBox();
            last.Focus(); last.Text = value;
        }
        last!.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
    }

    private AutomationElement ScrollPropertyIntoView(string automationId)
    {
        var viewer = fixture.WaitForElement("EntityPropertiesScrollViewer");
        var scroll = viewer.Patterns.Scroll.Pattern;
        var element = fixture.WaitForElement(automationId);
        for (var percent = 0; percent <= 100; percent += 2)
        {
            if (scroll.VerticallyScrollable.Value)
                scroll.SetScrollPercent(-1, percent);
            Thread.Sleep(30); // Scroll offsets are laid out after the UI Automation call returns.
            var bounds = element.BoundingRectangle;
            var viewport = viewer.BoundingRectangle;
            if (!element.IsOffscreen && bounds.Top >= viewport.Top && bounds.Bottom <= viewport.Bottom)
                return element;
        }
        throw new TimeoutException($"Property '{automationId}' could not be scrolled fully into view.");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void NewDocumentDropDownSupportsKeyboardAndAllTemplates()
    {
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        var button = fixture.WaitForElement("NewDocumentButton").AsButton();
        button.Focus(); Keyboard.Type(VirtualKeyShort.SPACE);
        Assert.True(fixture.WaitForElement("NewBlankDocumentMenuItem", includePopups: true).IsEnabled);
        CaptureScreenshot("new-document-menu.png");
        Keyboard.Type(VirtualKeyShort.ESC);
        WaitForRibbonMenuClosed("NewDocumentMenu");
        button.Focus(); Keyboard.Type(VirtualKeyShort.DOWN);
        Keyboard.Type(VirtualKeyShort.RETURN);
        WaitForRibbonMenuClosed("NewDocumentMenu");
        Assert.False(fixture.WaitForElement("CadCanvas").IsOffscreen);

        foreach (var (id, title) in new[]
                 {
                     ("NewA4TemplateButton", "A4"),
                     ("NewA3TemplateButton", "A3-100"),
                     ("NewLetterTemplateButton", "Letter")
                 })
        {
            fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
            button.Invoke();
            var item = fixture.WaitForElement(id, includePopups: true).AsMenuItem();
            Assert.True(item.IsEnabled);
            item.Invoke();
            WaitForRibbonMenuClosed("NewDocumentMenu");
            fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByName(title)) is { IsOffscreen: false },
                $"Template '{title}' did not create a document.");
        }
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void OpenAndSaveDropDownsLaunchExistingFileDialogs()
    {
        CreateNewDocument();
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        foreach (var (buttonId, menuId, screenshot, itemIds) in new[]
                 {
                     ("OpenDocumentButton", "OpenDocumentMenu", "open-document-menu.png",
                         new[] { "OpenNativeDocumentMenuItem", "ImportDxfButton" }),
                     ("SaveDocumentButton", "SaveDocumentMenu", "save-document-menu.png",
                         new[] { "SaveDocumentMenuItem", "SaveAsDocumentMenuItem", "SaveCompressedCopyButton" })
                 })
        {
            foreach (var itemId in itemIds)
            {
                fixture.WaitForElement(buttonId).AsButton().Invoke();
                var item = fixture.WaitForElement(itemId, includePopups: true).AsMenuItem();
                Assert.True(item.IsEnabled);
                fixture.WaitUntil(() => !item.IsOffscreen, $"Menu item '{itemId}' did not become visible.");
                Thread.Sleep(200); // Let the native popup finish painting before capturing or clicking it.
                CaptureScreenshot(screenshot);
                item.Click();
                Window? dialog = null;
                fixture.WaitUntil(() => (dialog = fixture.Automation.GetDesktop().FindFirstDescendant(
                    c => c.ByClassName("#32770").And(c.ByProcessId(fixture.Application.ProcessId)))?.AsWindow()) is not null,
                    $"Menu item '{itemId}' did not open its file dialog.");
                dialog!.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
                fixture.WaitUntil(() => fixture.Automation.GetDesktop().FindFirstDescendant(
                    c => c.ByClassName("#32770").And(c.ByProcessId(fixture.Application.ProcessId))) is null,
                    "The file dialog did not cancel.");
                WaitForRibbonMenuClosed(menuId);
            }
        }
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        CaptureScreenshot("file-ribbon-grouped.png", ribbonOnly: true);
        var export = fixture.WaitForElement("ExportDxfButton");
        var save = fixture.WaitForElement("SaveDocumentButton");
        Assert.True(export.IsEnabled);
        Assert.Equal(save.BoundingRectangle.Width, export.BoundingRectangle.Width);
        Assert.Equal(save.BoundingRectangle.Height, export.BoundingRectangle.Height);
        Assert.False(fixture.WaitForElement("CadCanvas").IsOffscreen);
    }

    private void WaitForRibbonMenuClosed(string automationId) =>
        fixture.WaitUntil(() => fixture.Automation.GetDesktop().FindFirstDescendant(
            c => c.ByAutomationId(automationId).And(c.ByProcessId(fixture.Application.ProcessId))) is null or { IsOffscreen: true },
            $"Ribbon menu '{automationId}' did not close.");

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ApplicationLaunchesWithAccessibleMainSurface()
    {
        fixture.EnsureApplicationIsRunning();

        Assert.Equal(
            "Direct2dCad.MainWindow",
            fixture.MainWindow.AutomationId);
        Assert.True(fixture.MainWindow.IsEnabled);
        Assert.NotNull(fixture.WaitForElement("MainRibbon"));
        AssertWelcomePropertiesAreEmpty();
        CaptureScreenshot("welcome-toolboxes.png");

        CreateNewDocument();
        fixture.WaitForElement("AnnotationRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("DimAlignedToolButton").AsToggleButton().Click();
        Assert.False(fixture.WaitForElement("AnnotationParametersSurface").IsOffscreen);
        SelectDocumentTab("Welcome");
        CaptureScreenshot("welcome-after-switch.png");
        AssertWelcomePropertiesAreEmpty();
        CaptureScreenshot("welcome-after-drawing.png");
        SelectDocumentTab("Untitled");
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("AnnotationParametersSurface"))
            is { IsOffscreen: false }, "Returning to the document did not restore annotation properties.");
    }

    private void AssertWelcomePropertiesAreEmpty()
    {
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("EntityPropertiesEmptyHint"))
            is { IsOffscreen: false }, "Welcome did not clear the property toolbox's document context.");
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("ApplyDimensionButton")));
        foreach (var id in new[] { "EditParametersSurface", "AnnotationParametersSurface", "ReassociateDimensionButton", "DetachDimensionButton" })
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId(id)) is null or { IsOffscreen: true },
                $"Welcome still displays document control '{id}'.");
    }

    private void SelectDocumentTab(string name)
    {
        fixture.MainWindow.Focus();
        var matches = fixture.MainWindow.FindAllDescendants(c => c.ByName(name));
        var tab = matches.FirstOrDefault(element => element.ControlType == ControlType.TabItem && !element.IsOffscreen)
            ?? matches.First(element => !element.IsOffscreen);
        tab.Click();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void WindowFileMenuSupportsTemplatesExchangeAndCompressedCopy()
    {
        OpenWindowFileMenu();
        foreach (var id in new[] { "FileMenuNewDocumentItem", "FileMenuTemplatesItem", "FileMenuOpenDocumentItem", "FileMenuOpenDxfItem" })
            Assert.True(fixture.WaitForElement(id, includePopups: true).IsEnabled);
        foreach (var id in new[] { "FileMenuSaveDocumentItem", "FileMenuExportDxfItem", "FileMenuPrintItem" })
            Assert.True(fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId(id)) is null or { IsOffscreen: true });
        fixture.WaitForElement("FileMenuTemplatesItem", includePopups: true).AsMenuItem().Expand();
        foreach (var id in new[] { "FileMenuA4TemplateItem", "FileMenuA3TemplateItem", "FileMenuLetterTemplateItem" })
            Assert.True(fixture.WaitForElement(id, includePopups: true).IsEnabled);
        fixture.WaitForElement("FileMenuA4TemplateItem", includePopups: true).AsMenuItem().Invoke();
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByName("A4")) is { IsOffscreen: false },
            "The window File menu did not create the selected template.");
        fixture.MainWindow.Focus();
        OpenWindowFileMenu();
        foreach (var id in new[] { "FileMenuSaveDocumentItem", "FileMenuSaveAsItem", "FileMenuSaveCompressedCopyItem", "FileMenuExportDxfItem", "FileMenuPrintItem" })
            Assert.True(fixture.WaitForElement(id, includePopups: true).IsEnabled);
        Thread.Sleep(200);
        CaptureScreenshot("window-file-menu.png");
        Keyboard.Type(VirtualKeyShort.ESC);

        foreach (var id in new[] { "FileMenuOpenDxfItem", "FileMenuSaveCompressedCopyItem", "FileMenuExportDxfItem" })
        {
            OpenWindowFileMenu();
            var item = fixture.WaitForElement(id, includePopups: true).AsMenuItem();
            Assert.True(item.IsEnabled);
            Thread.Sleep(200); // Allow the reopened popup to lay out before sending pointer input.
            item.Click();
            Window? dialog = null;
            try
            {
                fixture.WaitUntil(() => (dialog = fixture.Automation.GetDesktop().FindFirstDescendant(
                    c => c.ByClassName("#32770").And(c.ByProcessId(fixture.Application.ProcessId)))?.AsWindow()) is not null,
                    $"File menu item '{id}' did not open its file dialog.");
            }
            catch (TimeoutException)
            {
                CaptureScreenshot($"window-file-menu-{id}-failed.png");
                throw;
            }
            dialog!.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
            fixture.WaitUntil(() => fixture.Automation.GetDesktop().FindFirstDescendant(
                c => c.ByClassName("#32770").And(c.ByProcessId(fixture.Application.ProcessId))) is null,
                "The file dialog did not cancel.");
        }
    }

    private void OpenWindowFileMenu()
    {
        fixture.MainWindow.Focus();
        fixture.WaitForElement("WindowFileMenu").AsMenuItem().Expand();
        fixture.WaitUntil(() => !fixture.WaitForElement("FileMenuNewDocumentItem", includePopups: true).IsOffscreen,
            "The window File menu did not expand.");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void CanvasCursorFollowsSelectionDrawingPanAndCancel()
    {
        CreateNewDocument();
        Assert.Equal(ToggleState.Off, fixture.WaitForElement("GridSnapToggle").AsToggleButton().ToggleState);
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        var point = new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
        Mouse.MoveTo(point);
        WaitForCursor(32512, "Selection did not use the arrow cursor.");
        AssertCursorToolIconHidden();
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("LineToolButton").AsToggleButton().Click();
        Mouse.MoveTo(point);
        WaitForCursor(32512, "Drawing did not retain the arrow cursor.");
        var icon = fixture.WaitForElement("CursorToolModeIcon");
        fixture.WaitUntil(() => !icon.IsOffscreen && icon.Name == "Line", "The line cursor icon did not appear.");
        Assert.True(icon.BoundingRectangle.Left > point.X && icon.BoundingRectangle.Top > point.Y,
            "The tool icon did not appear below and to the right of the pointer.");
        var initialIconBounds = icon.BoundingRectangle;
        Mouse.MoveTo(point.X + 35, point.Y + 30);
        fixture.WaitUntil(() => icon.BoundingRectangle.Left > initialIconBounds.Left + 20,
            "The tool icon did not follow the pointer.");
        CaptureScreenshot("cursor-line-tool.png");
        Mouse.MoveTo((int)bounds.Right - 2, (int)bounds.Bottom - 2);
        fixture.WaitUntil(() => !icon.IsOffscreen && icon.BoundingRectangle.Right <= bounds.Right &&
            icon.BoundingRectangle.Bottom <= bounds.Bottom, "The tool icon was clipped at the canvas edge.");
        Mouse.MoveTo(point);
        try
        {
            Mouse.Down(MouseButton.Right);
            Mouse.MoveTo(point.X + 20, point.Y + 20);
            WaitForCursor(32649, "Panning did not use the hand cursor.");
            AssertCursorToolIconHidden();
        }
        finally
        {
            Mouse.Up(MouseButton.Right);
        }
        WaitForCursor(32512, "Ending the pan did not restore the arrow cursor.");
        fixture.WaitUntil(() => !icon.IsOffscreen, "The tool icon did not return after panning.");

        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("OffsetToolButton").AsToggleButton().Click();
        Mouse.MoveTo(point);
        fixture.WaitUntil(() => !icon.IsOffscreen && icon.Name == "Offset", "The edit-mode icon did not update.");
        WaitForCursor(32512, "Editing did not retain the arrow cursor.");
        fixture.WaitForElement("AnnotationRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("DimAlignedToolButton").AsToggleButton().Click();
        Mouse.MoveTo(point);
        fixture.WaitUntil(() => !icon.IsOffscreen && icon.Name.Contains("Aligned"), "The annotation-mode icon did not update.");
        WaitForCursor(32512, "Annotation did not retain the arrow cursor.");
        CaptureScreenshot("cursor-annotation-tool.png");
        var tabBounds = fixture.WaitForElement("AnnotationRibbonTab").BoundingRectangle;
        Mouse.MoveTo((int)(tabBounds.Left + tabBounds.Width / 2), (int)(tabBounds.Top + tabBounds.Height / 2));
        AssertCursorToolIconHidden();
        Mouse.MoveTo(point);
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        WaitForCursor(32512, "Cancel did not restore the arrow cursor.");
        AssertCursorToolIconHidden();
    }

    private void AssertCursorToolIconHidden() => fixture.WaitUntil(() =>
        fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CursorToolModeIcon")) is null or { IsOffscreen: true },
        "The cursor tool icon did not hide.");

    private void WaitForCursor(int cursorId, string failureMessage)
    {
        var expected = LoadCursor(IntPtr.Zero, new IntPtr(cursorId));
        Assert.NotEqual(IntPtr.Zero, expected);
        fixture.WaitUntil(() =>
        {
            var info = new NativeCursorInfo { Size = Marshal.SizeOf<NativeCursorInfo>() };
            return GetCursorInfo(ref info) && info.Cursor == expected;
        }, failureMessage);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public Point Position;
    }

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref NativeCursorInfo cursorInfo);

    [Fact][Trait("Category","UiAutomation")]
    public void DrawingStepInputAndEditRibbonRemainAccessibleInANarrowWindow()
    {
        CreateNewDocument();fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(900,700);
        fixture.MainWindow.Focus();
        // Keep the pointer away from the snackbar so its hover pause cannot delay the status-bar test.
        var ribbonBounds = fixture.WaitForElement("FileRibbonTab").BoundingRectangle;
        Mouse.MoveTo((int)(ribbonBounds.Left + ribbonBounds.Width / 2), (int)(ribbonBounds.Top + ribbonBounds.Height / 2));
        fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
        var toolbox = fixture.WaitForElement("DrawingAssistantToolbox");
        var statusBar = fixture.WaitForElement("MainStatusBarSurface");
        Assert.True(statusBar.BoundingRectangle.Height < 80, "Status bar grew beyond one compact row.");
        Assert.Null(statusBar.FindFirstDescendant(c => c.ByAutomationId("DrawingStepInput")));
        try
        {
            fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByName("New document created.")) is not { IsOffscreen: false },
                "New-document notification did not clear before interacting with the status bar.");
        }
        catch (TimeoutException)
        {
            CaptureScreenshot("new-document-notification-failed.png");
            throw;
        }
        var gridSnap = fixture.WaitForElement("GridSnapToggle").AsToggleButton();
        var objectSnap = fixture.WaitForElement("ObjectSnapToggle").AsToggleButton();
        Assert.Equal(ToggleState.Off, gridSnap.ToggleState);
        Assert.Equal(ToggleState.On, objectSnap.ToggleState);
        Assert.Equal("Grid snap", gridSnap.Name);
        gridSnap.Click();
        try
        {
            fixture.WaitUntil(() => gridSnap.ToggleState == ToggleState.On, "Grid-snap icon did not enable snapping.");
        }
        catch (TimeoutException exception)
        {
            CaptureScreenshot("grid-snap-click-failed.png");
            throw new TimeoutException($"{exception.Message} Window: {fixture.MainWindow.BoundingRectangle}; " +
                $"toggle: {gridSnap.BoundingRectangle}; enabled: {gridSnap.IsEnabled}; offscreen: {gridSnap.IsOffscreen}.", exception);
        }
        gridSnap.Focus(); Keyboard.Type(VirtualKeyShort.SPACE);
        fixture.WaitUntil(() => gridSnap.ToggleState == ToggleState.Off, "Grid-snap icon did not toggle from the keyboard.");
        var gridType = fixture.WaitForElement("GridTypeSelector").AsComboBox();
        gridType.Select("Dots");
        Assert.Equal("Dots", gridType.SelectedItem?.Text);
        gridType.Select("Lines");
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();fixture.WaitForElement("LineToolButton").AsToggleButton().Click();
        ShowDynamicInputOnCanvas();
        var step=fixture.WaitForElement("DynamicInputX").AsTextBox();Assert.False(step.IsOffscreen);
        Assert.Null(toolbox.FindFirstDescendant(c => c.ByAutomationId("DynamicInputX")));
        Assert.NotNull(statusBar.FindFirstDescendant(c => c.ByAutomationId("GridSnapToggle")));
        Assert.Null(toolbox.FindFirstDescendant(c => c.ByAutomationId("GridSnapToggle")));
        foreach (var id in new[] { "GridTypeSelector", "MajorGridSpacingSelector", "MinorGridSpacingSelector", "GridSnapToggle", "ObjectSnapToggle", "OrthoToggle", "PolarToggle", "ViewSettingsButton" })
        {
            var control = fixture.WaitForElement(id);
            Assert.False(control.IsOffscreen);
            Assert.True(control.BoundingRectangle.Right <= statusBar.BoundingRectangle.Right + 1, $"{id} was clipped at 900 px.");
        }
        Assert.Null(toolbox.FindFirstDescendant(c => c.ByName("DrawingInputHint")));
        Assert.Null(toolbox.FindFirstDescendant(c => c.ByName("SnapSettings")));
        Assert.Null(toolbox.FindFirstDescendant(c => c.ByAutomationId("DrawingViewDetails")));
        var viewSettingsButton = fixture.WaitForElement("ViewSettingsButton");
        viewSettingsButton.Click();
        var polarAngle = fixture.WaitForElement("PolarAngleInput", includePopups: true).AsTextBox();
        Assert.Equal("45", polarAngle.Text);
        polarAngle.Text = "50";
        polarAngle.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
        var perpendicular = fixture.WaitForElement("PerpendicularSnapToggle", includePopups: true).AsToggleButton();
        var tangent = fixture.WaitForElement("TangentSnapToggle", includePopups: true).AsToggleButton();
        perpendicular.Click(); tangent.Click();
        Assert.Equal(ToggleState.On, perpendicular.ToggleState);
        Assert.Equal(ToggleState.On, tangent.ToggleState);
        Assert.True(fixture.WaitForElement("BackgroundColorPicker", includePopups: true).IsEnabled);
        CaptureScreenshot("status-view-settings-900x700.png");
        var originMarker = fixture.WaitForElement("OriginMarkerSelector", includePopups: true).AsComboBox();
        originMarker.Select("Axes"); originMarker.Collapse();
        var snapMarker = fixture.WaitForElement("SnapMarkerSelector", includePopups: true).AsComboBox();
        snapMarker.Select("Square"); snapMarker.Collapse();
        polarAngle.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        viewSettingsButton.Focus(); Keyboard.Type(VirtualKeyShort.SPACE);
        polarAngle = fixture.WaitForElement("PolarAngleInput", includePopups: true).AsTextBox();
        Assert.Equal("50", polarAngle.Text);
        Assert.Equal("Axes", fixture.WaitForElement("OriginMarkerSelector", includePopups: true).AsComboBox().SelectedItem?.Text);
        Assert.Equal("Square", fixture.WaitForElement("SnapMarkerSelector", includePopups: true).AsComboBox().SelectedItem?.Text);
        fixture.WaitForElement("CloseViewSettingsButton", includePopups: true).AsButton().Click();
        fixture.WaitUntil(() => fixture.Automation.GetDesktop().FindFirstDescendant(
            c => c.ByAutomationId("PolarAngleInput").And(c.ByProcessId(fixture.Application.ProcessId))) is null or { IsOffscreen: true },
            "View-settings panel did not close.");
        var screenshotDirectory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
            fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByName("New document created.")) is not { IsOffscreen: false },
                "New-document notification did not clear before the screenshot.");
            fixture.MainWindow.CaptureToFile(Path.Combine(screenshotDirectory, "drawing-assistant-900x700.png"));
        }
        ShowDynamicInputOnCanvas();
        EnterDynamicCoordinates("1.25", "2.75");
        EnterDynamicFields(("Length", "38.75"), ("Angle", "0"));
        var input=GetOrOpenCommandLineInput();var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();fixture.WaitForElement("OffsetToolButton").AsToggleButton().Click();
        fixture.WaitUntil(()=>fixture.WaitForElement("CurrentToolStatusText").Name == "Offset","Offset mode did not appear.");
        CaptureScreenshot("edit-ribbon-active-900x700.png", ribbonOnly: true);
        ShowDynamicInputOnCanvas(); EnterDynamicCoordinates("20", "20");
        var canvas=fixture.WaitForElement("CadCanvas");canvas.Focus();Keyboard.Type(VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        fixture.EnsureApplicationIsRunning();
    }

    private void CaptureScreenshot(string fileName, bool ribbonOnly = false)
    {
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var element = ribbonOnly ? fixture.WaitForElement("MainRibbon") : fixture.MainWindow;
        element.CaptureToFile(Path.Combine(directory, fileName));
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void DrawingAssistantCanBeReopenedWithoutLosingPendingInput()
    {
        CreateNewDocument();
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DrawingAssistantToolbox")) is not { IsOffscreen: false },
            "An empty recovery toolbox should not occupy the canvas at startup.");
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        fixture.WaitForElement("LineToolButton").AsToggleButton().Click();
        ShowDynamicInputOnCanvas();
        fixture.WaitForElement("DynamicInputX").AsTextBox().Text = "1.25";
        fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
        fixture.WaitUntil(() => fixture.WaitForElement("DrawingAssistantToolbox") is { IsOffscreen: false },
            "The recovery toolbox did not open.");
        fixture.MainWindow.Focus();
        ToggleToolboxShortcut(VirtualKeyShort.KEY_D);
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DrawingAssistantToolbox")) is not { IsOffscreen: false },
            "Drawing assistant did not hide.");
        fixture.WaitForElement("ShowDrawingAssistantButton").AsButton().Invoke();
        ShowDynamicInputOnCanvas();
        var restored = fixture.WaitForElement("DynamicInputX").AsTextBox();
        Assert.Equal("1.25", restored.Text);
        EnterDynamicCoordinates("1.25", "2.75");
        Assert.NotNull(fixture.WaitForElement("DynamicInputLength"));
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void TerminalFollowsBatchesButPreservesHistoryScrollUntilFollowIsClicked()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommand(input, "HELP");
        var scroll = output.Patterns.Scroll.Pattern;
        fixture.WaitUntil(() => scroll.VerticallyScrollable.Value && scroll.VerticalScrollPercent.Value >= 99,
            "Batched HELP output did not scroll to the bottom.");
        scroll.SetScrollPercent(-1, 0);
        fixture.WaitUntil(() => scroll.VerticalScrollPercent.Value < 10, "The terminal did not scroll to its history.");
        ExecuteCommand(input, "STATUS");
        fixture.WaitUntil(() => output.Properties.HelpText.ValueOrDefault?.Contains("Entities:") == true,
            "STATUS did not finish.");
        var follow = fixture.WaitForElement("FollowCommandLineOutputButton").AsButton();
        fixture.WaitUntil(() => !follow.IsOffscreen, "New output did not expose the follow button.");
        Assert.True(scroll.VerticalScrollPercent.Value < 50, "New output moved the history reader to the bottom.");
        follow.Invoke();
        fixture.WaitUntil(() => scroll.VerticalScrollPercent.Value >= 99, "Follow did not return to the latest output.");
        ClearCommandOutput(input, output);
        Assert.Equal(0, CountOutputEntries(output));
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void UserSettingsDialog_AppliesAndPersistsIsolatedSettings()
    {
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        var settingsButton = fixture.WaitForElement("UserSettingsButton").AsButton();
        fixture.WaitUntil(
            () => settingsButton.IsEnabled && !settingsButton.IsOffscreen,
            "The user settings button did not become interactive.");
        settingsButton.Click();

        var dialog = fixture.WaitForWindow("UserSettingsDialog");
        Assert.NotNull(dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("ResetUserSettingsButton")));
        var darkTheme = dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("DarkThemeCheckBox"));
        Assert.NotNull(darkTheme);
        var darkThemeCheckBox = darkTheme.AsCheckBox();
        var expectedDarkTheme = darkThemeCheckBox.ToggleState != ToggleState.On;
        darkThemeCheckBox.Click();

        var apply = dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("ApplyUserSettingsButton"));
        Assert.NotNull(apply);
        apply.AsButton().Click();

        var settingsPath = Path.Combine(
            fixture.SettingsDirectory,
            "user-settings.json");
        fixture.WaitUntil(
            () => File.Exists(settingsPath) &&
                  ReadDarkThemeSetting(settingsPath) == expectedDarkTheme,
            "Applying user settings did not persist the isolated JSON file.");

        var cancel = dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("CancelUserSettingsButton"));
        Assert.NotNull(cancel);
        cancel.AsButton().Invoke();

        fixture.WaitUntil(
            () => !fixture.IsWindowOpen("UserSettingsDialog"),
            "The user settings dialog did not close after Cancel.");
        fixture.EnsureApplicationIsRunning();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void NewDocument_LineDrawingAndEscapeCompleteEndToEnd()
    {
        CreateNewDocument();

        var canvas = fixture.WaitForElement("CadCanvas");
        var drawTab = fixture.WaitForElement("DrawRibbonTab").AsTabItem();
        drawTab.Select();
        var lineTool = fixture.WaitForElement("LineToolButton").AsToggleButton();
        lineTool.Click();
        var toolMode = fixture.WaitForElement("CurrentToolStatusText");

        fixture.WaitUntil(
            () => toolMode.Name == "Line",
            "The Line tool did not become active.");

        canvas.Focus();
        var bounds = canvas.BoundingRectangle;
        Assert.True(bounds.Width > 200);
        Assert.True(bounds.Height > 150);
        var first = new Point(
            (int)(bounds.Left + bounds.Width * 0.35),
            (int)(bounds.Top + bounds.Height * 0.45));
        var second = new Point(
            (int)(bounds.Left + bounds.Width * 0.65),
            (int)(bounds.Top + bounds.Height * 0.60));
        Mouse.Click(first);
        Mouse.Click(second);

        fixture.EnsureApplicationIsRunning();
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(
            () => toolMode.Name.StartsWith("Select", StringComparison.Ordinal),
            "Escape did not return the canvas to Select mode.");

        Assert.Equal(
            ToggleState.On,
            fixture.WaitForElement("SelectToolButton").AsToggleButton().ToggleState);

        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 1");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 0");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "REDO", "Redo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 1");

        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "RECTANGLE",
            "Rectangle mode active.");
        ExecuteCommand(commandInput, "0,0");
        ExecuteCommand(commandInput, "20,10");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "SELECTALL",
            "Selected 2 entities.");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "ERASE",
            "Deleted 2 entities.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 0");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        Mouse.Click(new Point(
            (int)(bounds.Left + 12),
            (int)(bounds.Top + 12)));
        Thread.Sleep(100);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(100);
        Keyboard.Type(VirtualKeyShort.DELETE);
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 0");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        drawTab.Select();
        fixture.WaitForElement("DocumentSettingsButton").AsButton().Invoke();
        var documentSettings = fixture.WaitForWindow("DocumentSettingsDialog");
        Assert.NotNull(documentSettings.FindFirstDescendant(
            condition => condition.ByAutomationId("ResetDocumentSettingsButton")));
        var cancelDocumentSettings = documentSettings.FindFirstDescendant(
            condition => condition.ByAutomationId("CancelDocumentSettingsButton"));
        Assert.NotNull(cancelDocumentSettings);
        cancelDocumentSettings.AsButton().Invoke();

        fixture.MainWindow.Close();
        fixture.WaitForElement("MessageDialog");
        ClickWhenEnabled("MessageDialogOkButton");
        fixture.WaitForElement("UnsavedDocumentsDialog");
        var unsavedDocuments = fixture.WaitForElement("UnsavedDocumentsList").AsListBox();
        Assert.NotEmpty(unsavedDocuments.Items);
        ClickWhenEnabled("CancelCloseDocumentsButton");
        fixture.WaitUntil(
            () => fixture.MainWindow.IsEnabled,
            "Cancelling the unsaved-document dialog did not return to the editor.");
        fixture.EnsureApplicationIsRunning();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void MultiSelectionPropertiesAndLockedFrozenLayersStayInSync()
    {
        CreateNewDocument();
        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "LINE", "Line mode active.");
        ExecuteCommand(commandInput, "0,0");
        ExecuteCommand(commandInput, "20,10");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "RECTANGLE", "Rectangle mode active.");
        ExecuteCommand(commandInput, "30,0");
        ExecuteCommand(commandInput, "50,20");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "CANCEL",
            "Select mode active.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 2 entities.");

        var selectionCount = EnsureToolboxElement(
            "MultiEntitySelectionCount",
            "toolbox.entity-properties");
        fixture.WaitUntil(
            () => selectionCount.Name.EndsWith(": 2", StringComparison.Ordinal),
            "The multi-selection property panel did not show two selected entities.");
        Assert.NotNull(fixture.WaitForElement("MultiEntityLayerSelector").AsComboBox());

        var lockToggle = EnsureToolboxElement(
            "LayerLockToggle",
            "toolbox.layers").AsToggleButton();
        lockToggle.Click();
        fixture.WaitUntil(
            () => lockToggle.ToggleState == ToggleState.On,
            "The default layer did not become locked.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 2 entities.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "ERASE", "Nothing is selected.");

        lockToggle = fixture.WaitForElement("LayerLockToggle").AsToggleButton();
        lockToggle.Click();
        fixture.WaitUntil(
            () => lockToggle.ToggleState == ToggleState.Off,
            "The default layer did not become unlocked.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 2 entities.");

        var freezeToggle = fixture.WaitForElement("LayerFreezeToggle").AsToggleButton();
        freezeToggle.Click();
        fixture.WaitUntil(
            () => freezeToggle.ToggleState == ToggleState.On,
            "The default layer did not become frozen.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 0 entities.");
        freezeToggle = fixture.WaitForElement("LayerFreezeToggle").AsToggleButton();
        freezeToggle.Click();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void CadImageAndOleClipboardContentEnterMovablePasteAndCanBePlaced()
    {
        CreateNewDocument();
        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "RECTANGLE", "Rectangle mode active.");
        ExecuteCommand(commandInput, "0,0");
        ExecuteCommand(commandInput, "20,10");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 1 entities.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "COPY", "Copied 1 entity");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "PASTE", "Paste preview active for 1 entity");

        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.55),
            (int)(bounds.Top + bounds.Height * 0.55)));
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        SetClipboardImage();
        canvas.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Thread.Sleep(150);
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.65),
            (int)(bounds.Top + bounds.Height * 0.45)));
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 3");

        var oleSourcePath = SetClipboardOleFile();
        try
        {
            canvas.Focus();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            Thread.Sleep(250);
            Mouse.Click(new Point(
                (int)(bounds.Left + bounds.Width * 0.45),
                (int)(bounds.Top + bounds.Height * 0.40)));
            ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 4");
        }
        finally
        {
            File.Delete(oleSourcePath);
        }
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void LayoutTabsAndPaperModelSpaceSwitchTogether()
    {
        CreateNewDocument();
        var layoutTabs = fixture.WaitForElement("LayoutTabs").AsListBox();
        var initialTabCount = layoutTabs.Items.Length;
        Assert.True(initialTabCount >= 1);

        fixture.WaitForElement("AddLayoutButton").AsButton().Invoke();
        fixture.WaitUntil(
            () => layoutTabs.Items.Length == initialTabCount + 1,
            "Adding a layout did not create a second layout tab.");

        layoutTabs.Items[^1].Select();
        var paperSpace = fixture.WaitForElement("PaperSpaceButton").AsRadioButton();
        var modelSpace = fixture.WaitForElement("LayoutModelSpaceButton").AsRadioButton();
        fixture.WaitUntil(
            () => paperSpace.IsChecked == true,
            "The new layout did not enter paper space.");

        var createViewport = fixture.WaitForElement("CreateLayoutViewportButton").AsButton();
        createViewport.Invoke();
        fixture.WaitUntil(
            () => fixture.WaitForElement("CurrentToolStatusText").Name == "LayoutViewport",
            "Layout viewport creation did not start before the canvas clicks.");
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.25),
            (int)(bounds.Top + bounds.Height * 0.25)));
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.75),
            (int)(bounds.Top + bounds.Height * 0.75)));
        try
        {
            fixture.WaitUntil(
                () => fixture.WaitForElement("LayoutModelSpaceButton").AsRadioButton().IsChecked == true,
                "Creating a layout viewport did not enter its model space.");
        }
        catch
        {
            var screenshotDirectory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                fixture.MainWindow.CaptureToFile(Path.Combine(screenshotDirectory, "layout-viewport-failure.png"));
            }
            throw;
        }

        // Complete the adjustment step before testing the explicit space switches.
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.5),
            (int)(bounds.Top + bounds.Height * 0.5)));
        fixture.WaitUntil(
            () => fixture.WaitForElement("PaperSpaceButton").AsRadioButton().IsChecked == true,
            "Completing the layout viewport did not return to paper space.");

        modelSpace.Click();
        fixture.WaitUntil(
            () => fixture.WaitForElement("LayoutModelSpaceButton")
                .AsRadioButton()
                .IsChecked == true,
            "The layout did not enter model space.");
        paperSpace = fixture.WaitForElement("PaperSpaceButton").AsRadioButton();
        paperSpace.Click();
        fixture.WaitUntil(
            () => fixture.WaitForElement("PaperSpaceButton")
                .AsRadioButton()
                .IsChecked == true,
            "The layout did not return to paper space.");

        layoutTabs.Items[0].Select();
        fixture.WaitUntil(
            () => layoutTabs.Items[0].IsSelected,
            "Selecting the model tab did not update the active layout tab.");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void AiToolCommands_CreateMeasureAndManageGridPreset()
    {
        CreateNewDocument();
        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");

        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0}",
            "created_entity_id");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "TOOL measure_geometry {\"points\":[{\"x\":0,\"y\":0},{\"x\":3,\"y\":4}]}",
            "total_distance_millimeters");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "TOOL manage_grid_presets {\"operation\":\"list\"}",
            "presets");
    }

    private void ExecuteCommand(TextBox commandInput, string command)
    {
        fixture.MainWindow.Focus();
        commandInput.Focus();
        fixture.WaitUntil(() => commandInput.Properties.HasKeyboardFocus.ValueOrDefault,
            "The command input did not receive keyboard focus.");
        commandInput.Text = command;
        // Setting the UIA value can activate autocomplete or a different dock surface.
        // Establish focus again immediately before delivering the keyboard event.
        fixture.MainWindow.Focus();
        commandInput.Focus();
        fixture.WaitUntil(() => commandInput.Properties.HasKeyboardFocus.ValueOrDefault,
            "The command input lost focus after setting its value.");
        Keyboard.Type(VirtualKeyShort.RETURN);

        var autocompleteWait = Stopwatch.StartNew();
        while (autocompleteWait.Elapsed < TimeSpan.FromMilliseconds(500) &&
               !string.IsNullOrWhiteSpace(commandInput.Text))
        {
            Thread.Sleep(20);
        }

        if (!string.IsNullOrWhiteSpace(commandInput.Text))
            Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => string.IsNullOrWhiteSpace(commandInput.Text),
            $"The terminal did not consume command '{command}'.");
    }

    private TextBox GetOrOpenCommandLineInput()
    {
        var existingInput = fixture.MainWindow.FindFirstDescendant(
            condition => condition.ByAutomationId("CommandLineInput"));
        if (existingInput is not null && !existingInput.IsOffscreen)
            return existingInput.AsTextBox();

        fixture.MainWindow.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_3);
        return fixture.WaitForElement("CommandLineInput").AsTextBox();
    }

    private void CreateNewDocument()
    {
        fixture.MainWindow.Focus();
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        var button = fixture.WaitForElement("NewDocumentButton").AsButton();
        fixture.WaitUntil(
            () => button.IsEnabled && !button.IsOffscreen,
            "The new-document button did not become interactive.");
        button.Invoke();
        fixture.WaitForElement("NewBlankDocumentMenuItem", includePopups: true).AsMenuItem().Invoke();
        WaitForRibbonMenuClosed("NewDocumentMenu");
    }

    private AutomationElement EnsureToolboxElement(
        string automationId,
        string toolboxContentId)
    {
        var existing = fixture.MainWindow.FindFirstDescendant(
            condition => condition.ByAutomationId(automationId));
        if (existing is not null && !existing.IsOffscreen)
            return existing;

        var shortcut = toolboxContentId switch
        {
            "toolbox.entity-properties" => VirtualKeyShort.KEY_G,
            "toolbox.layers" => VirtualKeyShort.KEY_L,
            _ => throw new ArgumentOutOfRangeException(
                nameof(toolboxContentId),
                toolboxContentId,
                "No UI shortcut is registered for this toolbox.")
        };

        fixture.MainWindow.Focus();
        ToggleToolboxShortcut(shortcut);
        var activated = WaitForVisibleElement(automationId, TimeSpan.FromSeconds(2));
        if (activated is not null)
            return activated;

        // A visible toolbox is hidden by its toggle shortcut. Toggle once more
        // when the first press found it open but its content had not been in the
        // automation tree at the time of the initial lookup.
        ToggleToolboxShortcut(shortcut);

        return fixture.WaitForElement(automationId);
    }

    private static void ToggleToolboxShortcut(VirtualKeyShort key)
    {
        Keyboard.TypeSimultaneously(
            VirtualKeyShort.CONTROL,
            VirtualKeyShort.SHIFT,
            key);
    }

    private AutomationElement? WaitForVisibleElement(
        string automationId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            fixture.EnsureApplicationIsRunning();
            var element = fixture.MainWindow.FindFirstDescendant(
                condition => condition.ByAutomationId(automationId));
            if (element is not null && !element.IsOffscreen)
                return element;

            Thread.Sleep(50);
        }

        return null;
    }

    private void ClickWhenEnabled(string automationId)
    {
        var button = fixture.WaitForElement(automationId).AsButton();
        fixture.WaitUntil(
            () => button.IsEnabled && !button.IsOffscreen,
            $"Button '{automationId}' did not become interactive.");
        button.Click();
    }

    private void ExecuteCommandAndWaitForOutput(
        TextBox commandInput,
        AutomationElement commandOutput,
        string command,
        string expectedText)
    {
        ClearCommandOutput(commandInput, commandOutput);
        ExecuteCommand(commandInput, command);
        try
        {
            fixture.WaitUntil(
                () => CountOutputMatches(commandOutput, expectedText) > 0,
                $"Command output did not contain '{expectedText}'.");
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{exception.Message} Actual output: {ReadCommandOutput(commandOutput)}", exception);
        }
    }

    private static string ReadCommandOutput(AutomationElement commandOutput) =>
        $"Latest result: {commandOutput.Properties.HelpText.ValueOrDefault}; visible rows: " + string.Join(
            " | ",
            commandOutput.FindAllDescendants(
                    condition => condition.ByControlType(ControlType.ListItem))
                .Select(element => element.Name)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

    private void ClearCommandOutput(
        TextBox commandInput,
        AutomationElement commandOutput)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            ExecuteCommand(commandInput, "CLEAR");
            Thread.Sleep(100);
            if (CountOutputEntries(commandOutput) != 0)
                continue;

            Thread.Sleep(100);
            if (CountOutputEntries(commandOutput) == 0)
                return;
        }

        throw new TimeoutException("The CLEAR command did not empty the terminal output.");
    }

    private static int CountOutputMatches(
        AutomationElement commandOutput,
        string expectedText)
    {
        var matchCount = 0;
        try
        {
            if (commandOutput.Properties.HelpText.ValueOrDefault?.Contains(
                    expectedText,
                    StringComparison.Ordinal) == true)
            {
                return 1;
            }

            foreach (var element in commandOutput.FindAllDescendants())
            {
                try
                {
                    if (element.Name.Contains(expectedText, StringComparison.Ordinal))
                        matchCount++;
                }
                catch (COMException)
                {
                    // Virtualized terminal rows can disappear between query and property access.
                }
            }
        }
        catch (COMException)
        {
            // UIA can invalidate the descendant snapshot while a range update is applied.
        }

        return matchCount;
    }

    private static int CountOutputEntries(AutomationElement commandOutput)
    {
        try
        {
            return commandOutput.FindAllDescendants(
                condition => condition.ByControlType(ControlType.ListItem)).Length;
        }
        catch (COMException)
        {
            return int.MaxValue;
        }
    }

    private static bool ReadDarkThemeSetting(string settingsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("General")
            .GetProperty("IsDarkTheme")
            .GetBoolean();
    }

    private static void SetClipboardImage()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var bitmap = new Bitmap(8, 6);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.LimeGreen);
                System.Windows.Forms.Clipboard.SetImage(bitmap);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private static string SetClipboardOleFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"Direct2dCad-UI-OLE-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "Direct2dCad OLE clipboard UI automation");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var paths = new StringCollection { path };
                System.Windows.Forms.Clipboard.SetFileDropList(paths);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            File.Delete(path);
            throw failure;
        }

        return path;
    }
}
