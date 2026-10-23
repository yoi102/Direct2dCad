using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Theory]
    [InlineData("Field")]
    [InlineData("Canvas")]
    [InlineData("Panel")]
    [Trait("Category", "UiAutomation")]
    public void EscapeUnlocksTypedSplineValuesThenExitsDrawing(string focus)
        => VerifySplineEscape(focus, controlPoints: false);

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void NurbsCvEscapeUnlocksTypedValuesThenExitsDrawing()
        => VerifySplineEscape("Field", controlPoints: true);

    private void VerifySplineEscape(string focus, bool controlPoints)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, controlPoints ? "SPLINECV" : "SPLINE", controlPoints ? "SplineCv mode active." : "Spline mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "0,0", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "30,20", "Point accepted:");
        ShowDynamicInputOnCanvas();
        var canvas = fixture.WaitForElement("CadCanvas");
        canvas.Focus(); Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("75");
        Keyboard.Type(VirtualKeyShort.TAB); Keyboard.Type("15");
        var length = fixture.WaitForElement("DynamicInputLength").AsTextBox();
        var angle = fixture.WaitForElement("DynamicInputAngle").AsTextBox();
        fixture.WaitUntil(() => length.Text == "75" && angle.Text == "15", "Tab input did not lock both spline values.");
        if (focus == "Canvas") canvas.Focus();
        else if (focus == "Panel") { input.Text = ""; input.Focus(); }

        Keyboard.Type(VirtualKeyShort.ESC);

        fixture.WaitUntil(() => canvas.Properties.HasKeyboardFocus.ValueOrDefault, "Unlock did not return canvas focus.");
        Assert.Equal(controlPoints ? "CV Spline (Control Points)" : "Spline", fixture.WaitForElement("CurrentToolStatusText").Name);
        Mouse.MoveTo(EditCanvasPoint(canvas, 100, 75));
        fixture.WaitUntil(() => length.Text != "75" && angle.Text != "15", "Escape did not restore pointer-driven values.");
        CaptureScreenshot((controlPoints ? "spline-cv" : "spline") + "-escape-unlocked-" + focus.ToLowerInvariant() + ".png");
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Second Escape did not exit spline drawing.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        AssertCurveBindingsClean((controlPoints ? "spline-cv" : "spline") + "-escape-" + focus.ToLowerInvariant());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "UiAutomation")]
    public void EscapeWithoutLockedValuesExitsDrawingImmediately(bool fieldFocus)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "SPLINE", "Spline mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "0,0", "Point accepted:");
        ShowDynamicInputOnCanvas();
        fixture.WaitForElement("CadCanvas").Focus();
        if (fieldFocus) Keyboard.Type(VirtualKeyShort.TAB);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Unlocked drawing required more than one Escape.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
    }

    [Theory]
    [InlineData("Terminal")]
    [InlineData("AI")]
    [InlineData("Layers")]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEscapeFromPanelsCancelsDrawingAndReturnsToCanvas(string panel)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        ClickVisibleElement(fixture.WaitForElement("LineToolButton"));
        TextBox? draft = null;
        if (panel == "Terminal")
        {
            draft = input; FocusShortcutDraft(input);
        }
        else if (panel == "AI")
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
            draft = fixture.WaitForElement("AiPromptInput").AsTextBox();
            draft.Text = "unsubmitted shortcut draft"; draft.Focus();
        }
        else
        {
            var layers = fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("AddLayerButton"));
            if (layers is null || layers.IsOffscreen)
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_L);
            fixture.WaitForElement("AddLayerButton").Focus();
        }
        CaptureScreenshot($"escape-{panel.ToLowerInvariant()}-before.png");
        Assert.Equal("Line", fixture.WaitForElement("CurrentToolStatusText").Name);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Panel Escape did not cancel drawing.");
        fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
            "Panel Escape did not return focus to the canvas.");
        if (draft is not null) Assert.Equal("unsubmitted shortcut draft", draft.Text);
        CaptureScreenshot($"escape-{panel.ToLowerInvariant()}.png");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEscapeDismissesTerminalSuggestionsBeforeCancellingDrawing()
    {
        CreateNewDocument(); var input = GetOrOpenCommandLineInput();
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        ClickVisibleElement(fixture.WaitForElement("LineToolButton"));
        input.Focus(); input.Text = "CIR";
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => input.Properties.HasKeyboardFocus.ValueOrDefault, "Suggestion Escape moved focus.");
        Assert.Equal("Line", fixture.WaitForElement("CurrentToolStatusText").Name);
        Assert.Equal("CIR", input.Text);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Second Escape did not cancel drawing.");
        Assert.Equal("CIR", input.Text);
    }

    [Theory]
    [InlineData("EntityNameInput", "discarded name")]
    [InlineData("EntityRotationInput", "invalid angle")]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEscapeDiscardsPropertyTextBeforeClearingSelection(string id, string draft)
    {
        PrepareEscapePropertySelection();
        var field = ScrollPropertyIntoView(id).AsTextBox();
        var original = field.Text;
        field.Focus(); field.Text = draft;
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => field.Text == original, "Escape did not restore the unsubmitted property value.");
        fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
            "Property Escape did not return canvas focus.");
        Assert.False(field.IsOffscreen);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("EntityNameInput"))
            is null or { IsOffscreen: true }, "Second Escape did not clear selection.");
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        Assert.Equal(original, ScrollPropertyIntoView(id).AsTextBox().Text);
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEscapeClosesPropertyDropDownWithoutClearingSelection()
    {
        PrepareEscapePropertySelection();
        var selector = ScrollPropertyIntoView("EntityLayerSelector").AsComboBox();
        selector.Focus(); selector.Expand();
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => selector.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value ==
            FlaUI.Core.Definitions.ExpandCollapseState.Collapsed, "Escape did not close the layer dropdown.");
        Assert.False(fixture.WaitForElement("EntityNameInput").IsOffscreen);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("EntityNameInput"))
            is null or { IsOffscreen: true }, "Second Escape did not clear the selection.");
    }

    private void PrepareEscapePropertySelection()
    {
        CreateNewDocument(); var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "RECTANGLE", "Rectangle mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "0,0", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "100,60", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "CANCEL", "Select mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        var property = fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("EntityPropertiesToolbox"));
        if (property is null || property.IsOffscreen)
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_G);
        fixture.WaitForElement("EntityNameInput");
    }

}
