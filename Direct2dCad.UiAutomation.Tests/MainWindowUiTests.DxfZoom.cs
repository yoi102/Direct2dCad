using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [DxfSampleFact, Trait("Category", "UiAutomation")]
    public void SuppliedDxfRemainsVisibleDuringConnectorWheelZoomAndSelection()
    {
        var source = Path.GetFullPath(Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_DXF_SAMPLE")!);
        CreateNewDocument();
        fixture.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        fixture.MainWindow.Patterns.Transform.Pattern.Resize(1300, 900);
        fixture.MainWindow.Patterns.Transform.Pattern.Move(40, 40);
        var input = GetOrOpenCommandLineInput(); var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL open_dxf " + JsonSerializer.Serialize(new
        { file_path = source, source_unit = "Millimeter" }), "imported");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1681");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL set_viewport {\"operation\":\"fit\",\"padding\":32}", "visible_bounds");
        var fitted = ReadCommandOutput(output);
        var zoom = Value("zoom");
        var offsetX = Value("x"); var offsetY = Value("y");
        var canvas = fixture.WaitForElement("CadCanvas"); var bounds = canvas.BoundingRectangle;
        var anchor = new Point((int)Math.Round(bounds.Left + 64 * zoom + offsetX),
            (int)Math.Round(bounds.Top - 40 * zoom + offsetY));
        Assert.True(bounds.Contains(anchor));
        var fps = fixture.MainWindow.FindFirstDescendant(c => c.ByControlType(ControlType.Text).And(c.ByName("FPS", FlaUI.Core.Definitions.PropertyConditionFlags.MatchSubstring)));
        Assert.NotNull(fps);
        var samples = new List<object>();
        CaptureScreenshot("dxf-fit.png");
        canvas.Focus(); Mouse.MoveTo(anchor);
        foreach (var direction in new[] { 1, -1 })
        {
            for (var step = 1; step <= 32; step++)
            {
                Mouse.Scroll(direction);
                samples.Add(new { direction, step, displayed_render_estimate = fps!.Name });
                if (direction == 1 && step is 16 or 32) CaptureScreenshot($"dxf-wheel-{step}.png");
            }
        }
        ExecuteCommandAndWaitForOutput(input, output, "RENDERSTATS", "Geometry realization");
        var stats = ReadCommandOutput(output);
        ExecuteCommandAndWaitForOutput(input, output, "TOOL select_entities {\"entity_ids\":[1642]}", "success");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL set_viewport {\"operation\":\"zoom\",\"factor\":4.594972986357221,\"anchor_x\":64,\"anchor_y\":40}", "visible_bounds");
        canvas.Focus(); Mouse.MoveTo(anchor); Mouse.Scroll(1);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Selected: 1");
        CaptureScreenshot("dxf-long-outline-selected.png");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL clear_selection {}", "selected_entity_ids");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL set_viewport {\"operation\":\"fit\"}", "visible_bounds");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1681");
        fixture.EnsureApplicationIsRunning();
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
            File.WriteAllText(Path.Combine(directory, "ui-wheel.json"), JsonSerializer.Serialize(new
            {
                source, canvas = new { bounds.Left, bounds.Top, bounds.Width, bounds.Height }, anchor,
                fitted_zoom = zoom, samples, final_statistics = stats,
                limitation = "Real WPF mouse wheel and D3DImage presentation exercised. Displayed FPS is a rolling render-cost estimate, not monitor refresh FPS or an input-latency measurement."
            }, new JsonSerializerOptions { WriteIndented = true }));

        double Value(string key)
        {
            var match = Regex.Match(fitted, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?(?:[Ee][+-]?[0-9]+)?)");
            Assert.True(match.Success, "Missing viewport " + key + ": " + fitted);
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}

public sealed class DxfSampleFactAttribute : FactAttribute
{
    public DxfSampleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_DXF_SAMPLE")))
            Skip = "Set DIRECT2DCAD_UI_DXF_SAMPLE to the Arduino DXF to run the external-sample wheel trace.";
    }
}
