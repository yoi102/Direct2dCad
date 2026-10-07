using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.Globalization;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Theory]
    [InlineData("CIRCLE", "Radius", "40")]
    [InlineData("RECTANGLE", "Width", "80,40")]
    [InlineData("ELLIPSE CENTER", "Radius X", "40,40")]
    [Trait("Category", "UiAutomation")]
    public void CompactPropertiesKeepGeometryVisibleAndMetadataReachable(string tool, string fieldName, string secondPoint)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommand(input, tool); ExecuteCommand(input, "0,0"); ExecuteCommand(input, secondPoint);
        if (tool.StartsWith("ELLIPSE", StringComparison.Ordinal)) ExecuteCommand(input, "-10,10");
        ExecuteCommandAndWaitForOutput(input, output, "CANCEL", "Select mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        var propertyPanel = fixture.WaitForElement("EntityPropertiesToolbox");
        fixture.WaitUntil(() => propertyPanel.FindAllDescendants(c => c.ByName(fieldName))
                .Any(e => !e.IsOffscreen && e.ControlType != ControlType.Text &&
                    fixture.WaitForElement("EntityPropertiesScrollViewer").BoundingRectangle.Contains(e.BoundingRectangle)),
            "The principal geometry editor must be visible without scrolling.");
        var advanced = fixture.WaitForElement("EntityAdvancedSettings");
        Assert.Equal(ExpandCollapseState.Collapsed, advanced.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);
        CaptureScreenshot("compact-properties-" + tool.Split(' ')[0].ToLowerInvariant() + ".png");
        advanced.Patterns.ExpandCollapse.Pattern.Expand();
        var id = ScrollPropertyIntoView("EntityIdDisplay").AsTextBox();
        Assert.False(string.IsNullOrWhiteSpace(id.Text));
        advanced.Patterns.ExpandCollapse.Pattern.Collapse();
        fixture.WaitForElement("CadCanvas").Focus();
        ToggleToolboxShortcut(VirtualKeyShort.KEY_E);
        var scroll = fixture.WaitForElement("EntityPropertiesScrollViewer");
        if (scroll.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            scroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);

        var numeric = propertyPanel.FindAllDescendants(c => c.ByName(fieldName))
            .First(e => e.ControlType != ControlType.Text);
        var textElement = numeric.FindFirstDescendant(c => c.ByControlType(ControlType.Edit));
        Assert.NotNull(textElement);
        var editor = textElement.AsTextBox();
        var original = double.Parse(editor.Text, CultureInfo.InvariantCulture);
        var increase = numeric.FindFirstDescendant(c => c.ByAutomationId("PART_NumericUp"));
        Assert.NotNull(increase);
        increase.AsButton().Click();
        fixture.WaitUntil(() => double.Parse(editor.Text, CultureInfo.InvariantCulture) == original + 1,
            "The compact increment button did not update the geometry.");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        fixture.WaitUntil(() => Math.Abs(double.Parse(editor.Text, CultureInfo.InvariantCulture) - original) < 0.001,
            "Undo did not restore the geometry edited by the compact button.");
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        var inputTrace = new List<string>();
        foreach (var character in "55.5")
        {
            Keyboard.Type(character.ToString());
            Thread.Sleep(100);
            inputTrace.Add($"{character}: {editor.Text}; focused={editor.Properties.HasKeyboardFocus.ValueOrDefault}");
        }
        var screenshotDirectory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            File.WriteAllLines(Path.Combine(screenshotDirectory, "decimal-" + tool.Split(' ')[0].ToLowerInvariant() + ".txt"), inputTrace);
        CaptureScreenshot("decimal-" + tool.Split(' ')[0].ToLowerInvariant() + ".png");
        Assert.Equal("55.5", editor.Text);
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL clear_selection {}", "selected_entity_ids");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        numeric = propertyPanel.FindAllDescendants(c => c.ByName(fieldName))
            .First(e => e.ControlType != ControlType.Text);
        textElement = numeric.FindFirstDescendant(c => c.ByControlType(ControlType.Edit));
        Assert.NotNull(textElement);
        editor = textElement.AsTextBox();
        Assert.Equal(55.5, double.Parse(editor.Text, CultureInfo.InvariantCulture), precision: 3);
        CaptureInspector("inspector-" + tool.Split(' ')[0].ToLowerInvariant() + ".png");
        AssertCurveBindingsClean("compact-properties-" + tool.Split(' ')[0].ToLowerInvariant());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "UiAutomation")]
    public void CompactInspectorUpdatesThemeAndChineseLabelsWithoutBreakingEditors(bool dark)
    {
        PrepareEscapePropertySelection();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL create_layer {\"name\":\"Inspector layer\"}", "Inspector layer");
        fixture.WaitForElement("UserSettingsButton").AsButton().Click();
        var dialog = fixture.WaitForWindow("UserSettingsDialog");
        var checkboxElement = dialog.FindFirstDescendant(c => c.ByAutomationId("DarkThemeCheckBox"));
        Assert.NotNull(checkboxElement);
        var checkbox = checkboxElement.AsCheckBox();
        if ((checkbox.ToggleState == ToggleState.On) != dark) checkbox.Click();
        var language = Assert.Single(dialog.FindAllDescendants(c => c.ByControlType(ControlType.ComboBox))).AsComboBox();
        language.Select("中文"); language.Collapse();
        var apply = dialog.FindFirstDescendant(c => c.ByAutomationId("ApplyUserSettingsButton"));
        Assert.NotNull(apply);
        apply.AsButton().Click();
        var cancel = dialog.FindFirstDescendant(c => c.ByAutomationId("CancelUserSettingsButton"));
        Assert.NotNull(cancel);
        cancel.AsButton().Invoke();
        fixture.WaitUntil(() => !fixture.IsWindowOpen("UserSettingsDialog"), "The settings dialog did not close.");
        fixture.WaitForElement("CadCanvas").Focus();
        ToggleToolboxShortcut(VirtualKeyShort.KEY_E);
        var panel = fixture.WaitForElement("EntityPropertiesToolbox");
        var scroll = fixture.WaitForElement("EntityPropertiesScrollViewer");
        if (scroll.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            scroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);
        fixture.WaitUntil(() => panel.FindAllDescendants(c => c.ByName("宽度"))
                .Any(e => e.ControlType != ControlType.Text && scroll.BoundingRectangle.Contains(e.BoundingRectangle)),
            "The translated width editor was clipped or failed to update after changing the theme.");
        var name = fixture.WaitForElement("EntityNameInput").AsTextBox(); name.Focus();
        var selector = fixture.WaitForElement("EntityLayerSelector").AsComboBox();
        for (var click = 0; click < 3; click++)
        {
            selector.Click();
            fixture.WaitUntil(() => selector.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded,
                "A single click did not open the MD layer dropdown.", TimeSpan.FromSeconds(3));
            Keyboard.Type(VirtualKeyShort.ESC);
            fixture.WaitUntil(() => selector.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed,
                "Escape did not close the MD layer dropdown.");
        }
        selector.Click();
        fixture.WaitUntil(() => selector.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded,
            "The MD layer dropdown failed to reopen with the mouse.");
        CaptureScreenshot("inspector-chinese-" + (dark ? "dark" : "light") + "-dropdown.png");
        selector.Items.Single(item => item.Text == "Inspector layer").Click();
        fixture.WaitUntil(() => selector.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed,
            "Clicking an MD layer option did not close the dropdown.");
        Assert.Contains(selector.FindAllDescendants(c => c.ByName("Inspector layer")), element => !element.IsOffscreen);
        Assert.False(name.IsOffscreen);
        CaptureInspector("inspector-chinese-" + (dark ? "dark" : "light") + ".png");
        AssertCurveBindingsClean("inspector-chinese-" + (dark ? "dark" : "light"));
    }

    private void CaptureInspector(string name)
    {
        CaptureScreenshot(name);
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
            fixture.WaitForElement("EntityPropertiesToolbox").CaptureToFile(Path.Combine(directory, "panel-" + name));
    }
}
