using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact][Trait("Category","UiAutomation")]
    public void PolylineCornersCanPreviewAdjacentSegmentsAndTheWholeClosedPath()
    {
        CreateNewDocument(); FocusPathCornerWindow(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_polyline {\"points\":[{\"x\":-120,\"y\":-60},{\"x\":120,\"y\":-60},{\"x\":120,\"y\":100},{\"x\":-120,\"y\":100}],\"closed\":true}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"FILLET","Fillet mode active.");
        var canvas=fixture.WaitForElement("CadCanvas");
        Mouse.MoveTo(EditCanvasPoint(canvas,200,20));
        var radius=fixture.WaitForElement("EditDistanceInput").AsTextBox(); radius.Text="20";
        Mouse.Click(EditCanvasPoint(canvas,0,-60)); Mouse.MoveTo(EditCanvasPoint(canvas,120,30));
        fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>100,"Adjacent segments of one polyline did not preview.");
        CapturePathCornerScreenshot("polyline-local-fillet-preview.png");
        Mouse.Click(EditCanvasPoint(canvas,120,30)); PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"TOOL list_entities {\"type\":\"CompositePath\"}","CompositePath");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"CANCEL","Current interaction cancelled.");
        ExecuteCommandAndWaitForOutput(input,output,"CHAMFER","Chamfer mode active.");
        fixture.WaitForElement("EditWholePolylineToggle").AsToggleButton().Click();
        Mouse.Click(EditCanvasPoint(canvas,0,-60));
        fixture.WaitForElement("EditDistanceInput").AsTextBox().Text="20";
        fixture.WaitForElement("EditSecondDistanceInput").AsTextBox().Text="15";
        fixture.WaitUntil(()=>CountAllEditPreviewPixels(canvas)>100,"Whole polyline corner preview was missing.");
        CapturePathCornerScreenshot("polyline-all-chamfer-preview.png");
        Mouse.Click(EditCanvasPoint(canvas,0,0));
        ExecuteCommandAndWaitForOutput(input,output,"TOOL list_entities {\"type\":\"Polyline\"}","Polyline");
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        AssertCurveBindingsClean("polyline-corners");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void LineGripUsesTabForLengthAndAngleAndKeepsPreviewOutsideCanvas()
    {
        CreateNewDocument(); FocusPathCornerWindow(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":120,\"y2\":0}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,120,0));
        Mouse.MoveTo(EditCanvasPoint(canvas,160,40)); PressEditCanvasKey(canvas,VirtualKeyShort.TAB);
        var length=fixture.WaitForElement("DynamicInputLength").AsTextBox();
        fixture.WaitUntil(()=>length.Properties.HasKeyboardFocus.Value,"Tab did not enter grip length.");
        Keyboard.Type("180"); Keyboard.Type(VirtualKeyShort.TAB);
        var angle=fixture.WaitForElement("DynamicInputAngle").AsTextBox();
        fixture.WaitUntil(()=>angle.Properties.HasKeyboardFocus.Value,"Tab did not enter grip angle."); Keyboard.Type("30");
        fixture.WaitUntil(()=>length.Text=="180" && angle.Text=="30","Grip fields did not keep their typed values.");
        Assert.False(length.BoundingRectangle.IntersectsWith(angle.BoundingRectangle));
        CapturePathCornerScreenshot("line-grip-numeric-preview.png");
        var mid=EditCanvasPoint(canvas,78,45);
        fixture.WaitUntil(()=>CountBrightGeometry(mid)>4,"The numeric grip preview did not show the new line.");
        Mouse.MoveTo(new Point(canvas.BoundingRectangle.Left-15,canvas.BoundingRectangle.Top+50));
        fixture.WaitUntil(()=>CountBrightGeometry(mid)>4,"The active grip disappeared when the pointer left the canvas.");
        CapturePathCornerScreenshot("line-grip-outside-canvas.png");
        PressEditCanvasKey(canvas,VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"TOOL list_entities {\"type\":\"Line\"}","end");
        WaitForCurveParameter(output,"length",180);
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL list_entities {\"type\":\"Line\"}","end"); WaitForCurveParameter(output,"length",120);
        AssertCurveBindingsClean("line-grip-input");
    }

    [Fact][Trait("Category","UiAutomation")]
    public void CircleGripAcceptsRadiusAndDrawingPreviewSurvivesCanvasLeave()
    {
        CreateNewDocument(); FocusPathCornerWindow(); var input=GetOrOpenCommandLineInput(); var output=fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input,output,"TOOL add_circle {\"center_x\":0,\"center_y\":0,\"radius\":80}","created_entity_id");
        ExecuteCommandAndWaitForOutput(input,output,"SELECTALL","Selected 1 entities.");
        var canvas=fixture.WaitForElement("CadCanvas"); Mouse.Click(EditCanvasPoint(canvas,80,0));
        PressEditCanvasKey(canvas,VirtualKeyShort.TAB); var radius=fixture.WaitForElement("DynamicInputRadius").AsTextBox();
        fixture.WaitUntil(()=>radius.Properties.HasKeyboardFocus.Value,"Circle grip radius was not keyboard editable.");
        Keyboard.Type("125");
        fixture.WaitUntil(()=>radius.Text=="125" && CountBrightGeometry(EditCanvasPoint(canvas,125,0))>4,
            "The typed radius did not update the circle preview.");
        CapturePathCornerScreenshot("circle-grip-radius-input.png"); Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input,output,"TOOL list_entities {\"type\":\"Circle\"}","radius"); WaitForCircleRadius(output,125);
        ExecuteCommandAndWaitForOutput(input,output,"UNDO","Undo completed.");
        var baseline=CountGripGeometryPixels(canvas);
        ExecuteCommandAndWaitForOutput(input,output,"LINE","Line mode active.");
        ExecuteCommandAndWaitForOutput(input,output,"0,0","Point accepted"); Mouse.MoveTo(EditCanvasPoint(canvas,120,60));
        var mid=EditCanvasPoint(canvas,60,30);
        CapturePathCornerScreenshot("drawing-preview-before-leave.png");
        fixture.WaitUntil(()=>CountBrightGeometry(mid)>4,"The unlocked drawing preview was not visible.");
        Mouse.MoveTo(new Point(canvas.BoundingRectangle.Left-15,canvas.BoundingRectangle.Top+50));
        fixture.WaitUntil(()=>CountGripGeometryPixels(canvas)>baseline+40,"The drawing preview disappeared outside the canvas.");
        CapturePathCornerScreenshot("drawing-preview-outside-canvas.png"); PressEditCanvasKey(canvas,VirtualKeyShort.ESC);
        ExecuteCommandAndWaitForOutput(input,output,"STATUS","Entities: 1"); AssertCurveBindingsClean("circle-grip-and-drawing-leave");
    }

    private int CountBrightGeometry(Point point)
    {
        AssertPathCornerForeground();
        using var bitmap=new Bitmap(24,12);
        using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(point.X-12,point.Y-6,0,0,bitmap.Size);
        var count=0;
        for(var y=0;y<bitmap.Height;y++) for(var x=0;x<bitmap.Width;x++)
        { var c=bitmap.GetPixel(x,y); if(c.G>120 && c.G>c.R*1.3 && c.G>c.B*1.3) count++; }
        return count;
    }
    private int CountGripGeometryPixels(AutomationElement canvas)
    {
        AssertPathCornerForeground();
        var bounds=canvas.BoundingRectangle;
        using var bitmap=new Bitmap(bounds.Width,bounds.Height);
        using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bitmap.Size);
        var count=0;
        for(var y=0;y<bitmap.Height;y++) for(var x=0;x<bitmap.Width;x++)
        { var c=bitmap.GetPixel(x,y); if(c.G>120 && c.G>c.R*1.3 && c.G>c.B*1.3) count++; }
        return count;
    }
    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")]
    private static extern IntPtr GetPathCornerForegroundWindow();
    private void AssertPathCornerForeground()=>Assert.Equal((IntPtr)fixture.MainWindow.Properties.NativeWindowHandle.Value,
        GetPathCornerForegroundWindow());
    private void FocusPathCornerWindow() { fixture.MainWindow.Focus(); AssertPathCornerForeground(); }
    private void CapturePathCornerScreenshot(string name) { AssertPathCornerForeground(); CaptureScreenshot(name); }
}
