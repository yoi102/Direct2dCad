namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact]
    [Trait("Category", "UiAutomation")]
    public void TerminalLayoutToolsUsePaperSpaceAndCaptureWithoutPrintingBinaryText()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL create_layout {\"name\":\"Agent sheet\",\"width\":297,\"height\":210}", "layout_id");
        var layoutId = ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("layout_id").GetInt64();
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL activate_space {{\"space\":\"paper\",\"layout_id\":{layoutId}}}", "active_owner_block_id");
        var paperOwner = ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("active_owner_block_id").GetInt64();
        Assert.NotEqual(1, paperOwner);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":60,\"center_y\":60,\"radius\":20}", "created_entity_id");
        var entityId = ReadLatestJson(output).GetProperty("result").GetProperty("created_entity_id").GetInt64();
        ExecuteCommandAndWaitForOutput(input, output, $"TOOL get_entity_geometry {{\"entity_id\":{entityId}}}", "geometry");
        Assert.Equal("Circle", ReadLatestJson(output).GetProperty("result").GetProperty("result").GetProperty("type").GetString());
        ExecuteCommandAndWaitForOutput(input, output, "TOOL capture_view {\"maximum_size\":256}", "mime_type");
        var capture = ReadLatestJson(output);
        Assert.True(capture.GetProperty("success").GetBoolean());
        Assert.Equal("image/png", capture.GetProperty("result").GetProperty("image").GetProperty("mime_type").GetString());
        Assert.DoesNotContain("data_base64", output.Properties.HelpText.ValueOrDefault!);
        CaptureScreenshot("terminal-paper-capture.png");
        fixture.EnsureApplicationIsRunning();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void TerminalInvalidModePreservesPendingPolylineAndInvalidEraseKeepsSelection()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "PL", "Polyline mode active");
        ExecuteCommand(input, "0,0");
        ExecuteCommand(input, "20,0");
        ExecuteCommandAndWaitForOutput(input, output, "CIRCLE BANANA", "Usage: CIRCLE");
        Assert.Contains("Polyline", fixture.WaitForElement("CurrentToolStatusText").Name);
        ExecuteCommand(input, "20,20");
        ExecuteCommandAndWaitForOutput(input, output, "DONE", "Current step accepted");
        ExecuteCommandAndWaitForOutput(input, output, "ALL", "Selected 1");
        ExecuteCommandAndWaitForOutput(input, output, "ERASE 1234", "Usage: ERASE");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "ERASE", "Deleted 1");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        CaptureScreenshot("terminal-invalid-parameters.png");
    }
}
