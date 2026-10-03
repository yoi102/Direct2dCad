using System.Drawing;
using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact][Trait("Category","UiAutomation")]
    public void OffsetMouseDistanceTracksBothSidesAndCtrlDeleteUnlocksTypedDistance()
    {
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":-120,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement("OffsetToolButton").AsToggleButton().Click();
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,80,0));
        AssertNoEditActionButtons(); Mouse.MoveTo(EditCanvasPoint(canvas,0,50));
        var distance=fixture.WaitForElement("EditDistanceInput").AsTextBox();
        try
        {
            fixture.WaitUntil(()=>double.TryParse(distance.Text,out var value) && Math.Abs(value-50)<=0.51 &&
                CountEditPreviewPixels(EditCanvasPoint(canvas,0,50))>8,"Mouse movement did not set the offset distance and actual preview.");
        }
        catch(TimeoutException error)
        {
            CaptureScreenshot("offset-mouse-distance-failure.png");
            throw new InvalidOperationException($"{error.Message} D: {distance.Text}; prompt: {fixture.WaitForElement("EditStepPrompt").Name}; pixels: {CountEditPreviewPixels(EditCanvasPoint(canvas,0,50))}.",error);
        }
        // UIA and native mouse positions round the fractional WPF origin.
        // Subsequent assertions compare mouse displacement from this first value.
        var firstDistance=double.Parse(distance.Text,CultureInfo.InvariantCulture);
        fixture.WaitUntil(()=>ReadCommandOutput(output).Contains("Move to set D"),"The mouse-started tool prompt was not printed to Terminal.");
        Mouse.MoveTo(EditCanvasPoint(canvas,0,-70));
        fixture.WaitUntil(()=>double.TryParse(distance.Text,out var value) && Math.Abs(value-(120-firstDistance))<=0.011 &&
            CountEditPreviewPixels(EditCanvasPoint(canvas,0,-70))>8,"Crossing the source did not update distance and side.");
        distance.Text="30"; distance.Focus(); Keyboard.Type(VirtualKeyShort.RETURN);
        Mouse.MoveTo(EditCanvasPoint(canvas,0,80));
        fixture.WaitUntil(()=>distance.Text=="30" && CountEditPreviewPixels(EditCanvasPoint(canvas,0,30))>8,"A typed distance did not stay locked.");
        canvas.Focus(); Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL,VirtualKeyShort.DELETE);
        fixture.WaitUntil(()=>double.TryParse(distance.Text,out var value) && Math.Abs(value-(firstDistance+30))<=0.011 &&
            CountEditPreviewPixels(EditCanvasPoint(canvas,0,80))>8,"Ctrl+Delete did not restore mouse-driven distance.");
        CaptureScreenshot("offset-mouse-distance.png"); Mouse.Click(EditCanvasPoint(canvas,0,80));
        PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        AssertCurveBindingsClean("offset-mouse-distance");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void BreakFirstPointCanSplitWithEnterAndItsPromptIsPrintedToTerminal()
    {
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":-120,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement("BreakToolButton").AsToggleButton().Click();
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,80,0)); Mouse.Click(EditCanvasPoint(canvas,-40,0));
        AssertNoEditActionButtons(); Assert.Contains("Enter",fixture.WaitForElement("EditStepPrompt").Name);
        fixture.WaitUntil(()=>ReadCommandOutput(output).Contains("split at first"),"The break-point step prompt was not printed to Terminal.");
        CaptureScreenshot("break-keyboard-prompt.png"); PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        PressEditCanvasKey(canvas,VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        AssertCurveBindingsClean("break-keyboard-prompt");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void OffsetCursorInputRefreshesPreviewWhileEditingAndBlocksInvalidText()
    {
        CreateNewDocument();
        var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":-120,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement("OffsetToolButton").AsToggleButton().Click();
        var canvas=fixture.WaitForElement("CadCanvas"); var bounds=canvas.BoundingRectangle;
        var toolbar=fixture.WaitForElement("EditParametersSurface");
        Assert.True(bounds.Contains(toolbar.BoundingRectangle));
        AssertNoEditActionButtons();
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c=>c.ByAutomationId("DynamicInputX")));
        Mouse.MoveTo(EditCanvasPoint(canvas,0,50));
        var distance=fixture.WaitForElement("EditDistanceInput").AsTextBox();
        Assert.Null(toolbar.FindFirstDescendant(c=>c.ByAutomationId("EditDistanceInput")));
        Mouse.Click(distance.GetClickablePoint()); Keyboard.Type("30");
        fixture.WaitUntil(()=>distance.Text=="30" && CountEditPreviewPixels(EditCanvasPoint(canvas,0,30))>8,"Changing the canvas distance did not refresh the actual green preview while editing.");
        CaptureScreenshot("offset-canvas-toolbar.png");
        // Commit invalid text through UIA: keyboard letters may remain in the host's IME composition.
        distance.Text="bad";
        fixture.WaitUntil(()=>distance.Text=="bad" && !string.IsNullOrEmpty(fixture.WaitForElement("EditFeedback").Name),"Invalid committed text did not display feedback.");
        Keyboard.Type(VirtualKeyShort.RETURN); Mouse.Click(EditCanvasPoint(canvas,0,50));
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        distance=fixture.WaitForElement("EditDistanceInput").AsTextBox(); distance.Text="30";
        try { fixture.WaitUntil(()=>CountEditPreviewPixels(EditCanvasPoint(canvas,0,30))>8,"A corrected value did not restore the preview."); }
        catch(TimeoutException error)
        {
            CaptureScreenshot("offset-correction-failure.png");
            throw new InvalidOperationException($"{error.Message} D: {distance.Text}; prompt: {fixture.WaitForElement("EditStepPrompt").Name}; feedback: {fixture.WaitForElement("EditFeedback").Name}; canvas: {canvas.BoundingRectangle}.",error);
        }
        Mouse.Click(EditCanvasPoint(canvas,0,50)); canvas.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        AssertCurveBindingsClean("offset-canvas");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void CornerToolsPreviewTheHoveredSecondCurveAndKeepParametersOnCanvas()
    {
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":0,\"y2\":120}","created_entity_id");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select();
        foreach(var mode in new[]{"Fillet","Chamfer"})
        {
            fixture.WaitForElement(mode+"ToolButton").AsToggleButton().Click();
            var canvas=fixture.WaitForElement("CadCanvas"); Mouse.MoveTo(EditCanvasPoint(canvas,100,0));
            fixture.WaitForElement("EditDistanceInput").AsTextBox().Text="30";
            if(mode=="Chamfer") fixture.WaitForElement("EditSecondDistanceInput").AsTextBox().Text="40";
            Mouse.Click(EditCanvasPoint(canvas,100,0)); Mouse.MoveTo(EditCanvasPoint(canvas,0,100));
            fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>20,"Hovering over the second curve did not display the corner preview.");
            Mouse.MoveTo(EditCanvasPoint(canvas,60,60));
            fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>20,"Moving off the second curve discarded the corner preview.");
            CaptureScreenshot(mode.ToLowerInvariant()+"-retained-preview.png");
            AssertNoEditActionButtons();
            Mouse.Click(EditCanvasPoint(canvas,0,100));
            fixture.WaitUntil(()=>fixture.WaitForElement("EditStepPrompt").Name.Contains("confirm",StringComparison.OrdinalIgnoreCase),"Selecting two curves did not advance the prompt.");
            var distance=fixture.WaitForElement("EditDistanceInput"); Mouse.MoveTo(distance.GetClickablePoint());
            fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>20,"The preview disappeared over the parameter toolbar.");
            CaptureScreenshot(mode.ToLowerInvariant()+"-canvas-preview.png");
            PressEditCanvasKey(canvas,mode=="Fillet" ? VirtualKeyShort.ESC : VirtualKeyShort.RETURN);
        }
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 3");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        AssertCurveBindingsClean("corner-canvas");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void ArrayCursorFieldsStayCompactInANarrowCanvasAndCommitOnlyCorrectedParameters()
    {
        fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(900,700);
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":10,\"y1\":-50,\"x2\":40,\"y2\":-50}","created_entity_id");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement("RectArrayToolButton").AsToggleButton().Click();
        var canvas=fixture.WaitForElement("CadCanvas"); var bounds=canvas.BoundingRectangle;
        Mouse.Click(EditCanvasPoint(canvas,25,-50));
        fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>30,"The first source click did not immediately preview the rectangular array.");
        Assert.DoesNotContain("next",fixture.WaitForElement("EditStepPrompt").Name,StringComparison.OrdinalIgnoreCase);
        Mouse.MoveTo(EditCanvasPoint(canvas,-170,0));
        var toolbarBounds=fixture.WaitForElement("EditParametersSurface").BoundingRectangle;
        Assert.False(toolbarBounds.IntersectsWith(fixture.WaitForElement("FitCanvasButton").BoundingRectangle),"Fit button covered the parameter toolbar.");
        Assert.False(toolbarBounds.IntersectsWith(fixture.WaitForElement("RenderPerformanceText").BoundingRectangle),"Performance text covered the parameter toolbar.");
        AssertNoEditActionButtons();
        foreach(var id in new[]{"EditArrayRowsInput","EditArrayColumnsInput","EditArraySpacingXInput","EditArraySpacingYInput"})
            Assert.True(bounds.Contains(fixture.WaitForElement(id).BoundingRectangle),id+" was clipped in the narrow canvas.");
        var fields=new[]{"EditArrayRowsInput","EditArrayColumnsInput","EditArraySpacingXInput","EditArraySpacingYInput"}.Select(id=>fixture.WaitForElement(id)).ToArray();
        Assert.All(fields,field=>Assert.InRange(field.BoundingRectangle.Width,56,128));
        Assert.All(fields,field=>Assert.InRange(Math.Abs(field.BoundingRectangle.Top-fields[0].BoundingRectangle.Top),0,2));
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("2");
        Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("4");
        Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("60");
        Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("20");
        fixture.WaitUntil(()=>fields.Select(field=>field.AsTextBox().Text).SequenceEqual(new[]{"2","4","60","20"}),
            "Tab did not cycle through all four rectangular-array parameters.");
        Mouse.MoveTo(fixture.WaitForElement("EditArraySpacingYInput").GetClickablePoint());
        CaptureScreenshot("rect-array-canvas-diagnostic.png");
        fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>30,"Array copies were not visible while editing parameters.");
        CaptureScreenshot("rect-array-canvas-900x700.png");
        fixture.WaitForElement("EditArrayRowsInput").AsTextBox().Text="0";
        PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        fixture.WaitForElement("EditArrayRowsInput").AsTextBox().Text="2";
        Mouse.Click(EditCanvasPoint(canvas,-170,0));
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 8");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        AssertCurveBindingsClean("array-canvas");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void BoundaryBreakAndJoinToolsHaveVisibleStagesMarkersAndPreviews()
    {
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        void AddLine(int x1,int y1,int x2,int y2) => ExecuteCommandAndWaitForOutput(input,output,
            $"TOOL add_line {{\"x1\":{x1},\"y1\":{y1},\"x2\":{x2},\"y2\":{y2}}}","created_entity_id");
        void Start(string mode)
        { fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement(mode+"ToolButton").AsToggleButton().Click(); }
        AddLine(0,-100,0,100); AddLine(-120,0,120,0); Start("Trim");
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,0,80));
        AssertNoEditActionButtons();
        PressEditCanvasKey(canvas,VirtualKeyShort.RETURN); Mouse.MoveTo(EditCanvasPoint(canvas,80,0));
        fixture.WaitUntil(()=>CountRemovedEditPixels(EditCanvasPoint(canvas,60,0))>8,"Trim did not visibly mark the removed interval.");
        CaptureScreenshot("trim-canvas-preview.png"); Mouse.Click(EditCanvasPoint(canvas,80,0));
        PressEditCanvasKey(canvas,VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");

        CreateNewDocument(); AddLine(80,-100,80,100); AddLine(10,0,40,0); Start("Extend");
        canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,80,70));
        PressEditCanvasKey(canvas,VirtualKeyShort.RETURN); Mouse.MoveTo(EditCanvasPoint(canvas,30,0));
        fixture.WaitUntil(()=>CountEditPreviewPixels(EditCanvasPoint(canvas,60,0))>8,"Extend did not visibly preview the added segment.");
        CaptureScreenshot("extend-canvas-preview.png"); Mouse.Click(EditCanvasPoint(canvas,30,0));
        PressEditCanvasKey(canvas,VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");

        CreateNewDocument(); AddLine(-120,0,120,0);
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities."); Start("Break");
        canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,-40,0)); Mouse.MoveTo(EditCanvasPoint(canvas,40,0));
        AssertNoEditActionButtons();
        try { fixture.WaitUntil(()=>CountRemovedEditPixels(EditCanvasPoint(canvas,0,0))>8,
            "Break did not preserve the first point and preview the removed interval."); }
        catch(TimeoutException error)
        {
            CaptureScreenshot("break-interval-failure.png");
            throw new InvalidOperationException($"{error.Message} Prompt: {fixture.WaitForElement("EditStepPrompt").Name}; feedback: {fixture.WaitForElement("EditFeedback").Name}.",error);
        }
        CaptureScreenshot("break-canvas-preview.png"); Mouse.Click(EditCanvasPoint(canvas,40,0));
        PressEditCanvasKey(canvas,VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");

        CreateNewDocument(); AddLine(-120,0,0,0); AddLine(0,0,120,0); Start("Join");
        canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,-60,0)); Mouse.MoveTo(EditCanvasPoint(canvas,60,0));
        fixture.WaitUntil(()=>CountEditPreviewPixels(EditCanvasPoint(canvas,60,0))>8,"Join did not preview the hovered compatible curve.");
        Mouse.Click(EditCanvasPoint(canvas,60,0)); AssertNoEditActionButtons();
        Assert.Contains("Enter",fixture.WaitForElement("EditStepPrompt").Name);
        CaptureScreenshot("join-canvas-preview.png"); PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        AssertCurveBindingsClean("boundary-break-join-canvas");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void PolarArrayOnlyShowsTwoCursorFieldsAndNextCanvasClickCommitsAtItsCenter()
    {
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":80,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement("PolarArrayToolButton").AsToggleButton().Click();
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,100,0)); Mouse.MoveTo(EditCanvasPoint(canvas,0,0));
        fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>30,"Polar array did not preview immediately after the source click.");
        var count=fixture.WaitForElement("EditArrayCountInput").AsTextBox();
        var sweep=fixture.WaitForElement("EditArraySweepInput").AsTextBox();
        Assert.StartsWith("N:",count.Name); Assert.StartsWith("A:",sweep.Name);
        Assert.InRange(Math.Abs(count.BoundingRectangle.Top-sweep.BoundingRectangle.Top),0,2);
        Assert.InRange(count.BoundingRectangle.Width,56,128); Assert.InRange(sweep.BoundingRectangle.Width,56,128);
        Assert.Null(fixture.MainWindow.FindFirstDescendant(c=>c.ByAutomationId("EditRotateCopiesToggle")));
        Assert.Null(fixture.WaitForElement("EditParametersSurface").FindFirstDescendant(c=>c.ByAutomationId("EditArrayCountInput")));
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("5");
        Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("40");
        fixture.WaitUntil(()=>count.Text=="5" && sweep.Text=="40","Tab did not switch between the two cursor parameters.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT,VirtualKeyShort.TAB);
        fixture.WaitUntil(()=>count.Properties.HasKeyboardFocus.Value,"Shift+Tab did not return to count.");
        Keyboard.Type(VirtualKeyShort.RETURN);
        AssertNoEditActionButtons();
        Assert.Contains("center",fixture.WaitForElement("EditStepPrompt").Name);
        fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>30,
            "Polar array parameters did not keep the preview active.");
        CaptureScreenshot("polar-array-canvas-preview.png"); Mouse.Click(EditCanvasPoint(canvas,0,0));
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 5");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1");
        AssertCurveBindingsClean("polar-array-canvas");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void SwitchingBetweenEditToolsCannotCommitAnInvalidVisibleParameter()
    {
        CreateNewDocument(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":0,\"y2\":120}","created_entity_id");
        fixture.WaitForElement("EditRibbonTab").AsTabItem().Select(); fixture.WaitForElement("OffsetToolButton").AsToggleButton().Click();
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,100,0));
        var distance=fixture.WaitForElement("EditDistanceInput").AsTextBox(); distance.Text="bad";
        fixture.WaitUntil(()=>!string.IsNullOrEmpty(fixture.WaitForElement("EditFeedback").Name),"Invalid offset text was accepted.");
        fixture.WaitForElement("FilletToolButton").AsToggleButton().Click(); Mouse.Click(EditCanvasPoint(canvas,0,100));
        distance=fixture.WaitForElement("EditDistanceInput").AsTextBox();
        fixture.WaitUntil(()=>distance.Text!="bad" || !string.IsNullOrEmpty(fixture.WaitForElement("EditFeedback").Name),
            "Switching tools discarded validation while the invalid text remained visible.");
        distance.Text="30";
        PressEditCanvasKey(canvas,VirtualKeyShort.KEY_R); Assert.Equal("30",distance.Text);
        fixture.WaitUntil(()=>fixture.WaitForElement("EditStepPrompt").Name.Contains("first"),"R did not restart source selection.");
        Mouse.Click(EditCanvasPoint(canvas,100,0)); Mouse.Click(EditCanvasPoint(canvas,0,100));
        PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 3");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 2");
        AssertCurveBindingsClean("switch-edit-tools-canvas");
    }

    private void AssertNoEditActionButtons()
    {
        foreach(var id in new[]{"EditActionButton","EditReselectButton","EditCancelButton"})
            Assert.Null(fixture.MainWindow.FindFirstDescendant(condition=>condition.ByAutomationId(id)));
    }

    private void PressEditCanvasKey(AutomationElement canvas, VirtualKeyShort key)
    {
        canvas.Focus();
        fixture.WaitUntil(()=>canvas.Properties.HasKeyboardFocus.ValueOrDefault,"Canvas did not receive keyboard focus.");
        Keyboard.Type(key);
    }

    private static Point EditCanvasPoint(AutomationElement canvas,int x,int y)
    { var bounds=canvas.BoundingRectangle; return new(bounds.Left+bounds.Width/2+x,bounds.Top+bounds.Height/2-y); }
    private static int CountEditPreviewPixels(Point point)
    {
        using var bitmap=new Bitmap(24,12);
        using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(point.X-12,point.Y-6,0,0,bitmap.Size);
        return CountGreenEditPixels(bitmap);
    }
    private static int CountRemovedEditPixels(Point point)
    {
        using var bitmap=new Bitmap(24,12);
        using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(point.X-12,point.Y-6,0,0,bitmap.Size);
        var count=0;
        for(var y=0;y<bitmap.Height;y++) for(var x=0;x<bitmap.Width;x++)
        { var color=bitmap.GetPixel(x,y); if(color.R>170 && color.G<150 && color.B<150) count++; }
        return count;
    }
    private static int CountAllEditPreviewPixels(AutomationElement canvas)
    {
        var bounds=canvas.BoundingRectangle; using var bitmap=new Bitmap(bounds.Width,bounds.Height);
        using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bitmap.Size);
        return CountGreenEditPixels(bitmap);
    }
    private static int CountGreenEditPixels(Bitmap bitmap)
    {
        var count=0;
        for(var y=0;y<bitmap.Height;y++) for(var x=0;x<bitmap.Width;x++)
        { var color=bitmap.GetPixel(x,y); if(color.G>170 && color.R>40 && color.R<130 && color.B>90 && color.B<190) count++; }
        return count;
    }
}
