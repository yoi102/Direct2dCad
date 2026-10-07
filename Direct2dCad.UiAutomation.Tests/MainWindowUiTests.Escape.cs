using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
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
