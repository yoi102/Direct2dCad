using System.Text.Json;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact]
    [Trait("Category", "UiAutomation")]
    public void TerminalShortcutsAndNewToolsExecuteThroughTheRealWindow()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "HELP", "SUBTRACT subject_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":0,\"center_y\":0,\"radius\":10}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":10,\"center_y\":0,\"radius\":10}", "created_entity_id");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 2 entities");
        ExecuteCommandAndWaitForOutput(input, output, "UNION", "result_entity_id");
        var regionId = ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("result_entity_id").GetInt64();
        ExecuteCommandAndWaitForOutput(input, output, "MOVE 5,0", "transform_entities");
        fixture.WaitUntil(() => output.FindAllDescendants(c => c.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).Length > 0,
            "The MOVE output rows were not flushed.");
        if (output.Patterns.Scroll.Pattern.VerticallyScrollable.Value) output.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);
        try
        {
            fixture.WaitUntil(() => output.FindAllDescendants(c => c.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))
                .Any(row => row.Name.Contains("[Document]") && row.Name.Contains("Move")),
                "The executed document command did not appear in the terminal history.");
        }
        catch (TimeoutException exception)
        {
            CaptureScreenshot("terminal-move-failure.png");
            throw new TimeoutException($"{exception.Message} {ReadCommandOutput(output)}", exception);
        }
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL get_entity_geometry {{\"entity_id\":{regionId}}}", "contours");
        var region = ReadLatestJson(output).GetProperty("result").GetProperty("result");
        Assert.Equal("Region", region.GetProperty("type").GetString());
        Assert.Equal(-5, region.GetProperty("bounds").GetProperty("min_x").GetDouble(), 6);
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":100,\"y2\":0}", "created_entity_id");
        var lineId = ReadLatestJson(output).GetProperty("result").GetProperty("created_entity_id").GetInt64();
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL add_dimension {{\"kind\":\"Aligned\",\"source_entity_id\":{lineId},\"x\":50,\"y\":20}}", "entity_id");
        var dimensionId = ReadLatestJson(output).GetProperty("result").GetProperty("entity_id").GetInt64();
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL set_dimension {{\"entity_id\":{dimensionId},\"shape_font\":\"simplex\",\"arrow\":\"Closed\",\"text_override\":\"CHECK\"}}", "CHECK");
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL detach_dimension {{\"entity_id\":{dimensionId}}}", "Detached");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed");
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL get_entity_geometry {{\"entity_id\":{dimensionId}}}", "anchor_references");
        Assert.Equal("Valid", ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("geometry").GetProperty("association").GetString());
        fixture.WaitUntil(() => output.FindAllDescendants(c => c.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).Length > 0,
            "Terminal result rows were not rendered.");
        Thread.Sleep(100); // Let the flushed output repaint before recording visual evidence.
        CaptureScreenshot("terminal-tools-and-shortcuts.png");
        fixture.EnsureApplicationIsRunning();
    }

    private static JsonElement ReadLatestJson(FlaUI.Core.AutomationElements.AutomationElement output)
    {
        var text = output.Properties.HelpText.ValueOrDefault!;
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(text[text.IndexOf('{')..]));
        using var json = JsonDocument.ParseValue(ref reader);
        return json.RootElement.Clone();
    }
}
