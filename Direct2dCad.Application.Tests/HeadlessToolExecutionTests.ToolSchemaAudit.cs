using Direct2dCad.Application.Tools;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public void EveryPublishedArrayHasAnItemSchema()
    {
        static void Inspect(System.Text.Json.JsonElement node, string path)
        {
            if (node.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (node.TryGetProperty("type", out var type) && type.ValueKind == System.Text.Json.JsonValueKind.String && type.GetString() == "array")
                    Assert.True(node.TryGetProperty("items", out _) || node.TryGetProperty("prefixItems", out _), path);
                foreach (var property in node.EnumerateObject())
                    Inspect(property.Value, path + "." + property.Name);
            }
            else if (node.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in node.EnumerateArray())
                    Inspect(item, $"{path}[{index++}]");
            }
        }
        foreach (var tool in CadWorkspaceToolExecutor.ToolDefinitions)
            Inspect(tool.Parameters, tool.Name);
    }

    [Fact]
    public async Task CompositeGeometryUpdatePreservesLegacySegmentCasing()
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Composite casing audit").Session.CadEditor.Document;
        var path = document.AddCompositePath(default, [new CadCompositeLineSegment(new(10, 0))], false);
        await Execute(new(workspace), "set_entity_geometry", new
        {
            entity_id = path.Id.Value, start = new { x = 0, y = 0 }, closed = false,
            segments = new[] { new { type = "LINE", end = new { x = 20, y = 0 } } }
        });
        Assert.Equal(new CadPointD(20, 0), path.EndPoint);
    }

    [Fact]
    public async Task CompositeGeometryUpdateRejectsUnknownSegmentProperties()
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Composite schema audit").Session.CadEditor.Document;
        var path = document.AddCompositePath(default, [new CadCompositeLineSegment(new(10, 0))], false);
        await Execute(new(workspace), "set_entity_geometry", new
        {
            entity_id = path.Id.Value, start = new { x = 0, y = 0 }, closed = false,
            segments = new[] { new { type = "line", end = new { x = 20, y = 0 }, typo = true } }
        }, false);
        Assert.Equal(new CadPointD(10, 0), path.EndPoint);
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("hatch")]
    [InlineData("gradient")]
    public void FillStyleSchemaRequiresTheParametersForItsMode(string mode)
    {
        var arguments = CadJson.SerializeToElement(new { name = "Incomplete fill", mode });
        Assert.Throws<ArgumentException>(() => CadToolSchemaValidator.NormalizeAndValidate(
            "create_fill_style", arguments, CadWorkspaceToolExecutor.ToolDefinitions));
    }

    [Fact]
    public void HatchPatternSchemaDescribesLineCoordinatesAndDashes()
    {
        var schema = CadWorkspaceToolExecutor.ToolDefinitions.Single(tool => tool.Name == "create_hatch_pattern").Parameters;
        var line = schema.GetProperty("properties").GetProperty("lines").GetProperty("items");
        foreach (var field in new[] { "angle_degrees", "origin", "offset", "dash_pattern" })
            Assert.True(line.GetProperty("properties").TryGetProperty(field, out _));
        Assert.False(line.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task HatchPatternToolRejectsUnknownLinePropertiesBeforeCreation()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Hatch schema audit").Session;
        var originalCount = session.CadEditor.Document.HatchPatterns.Count;
        await Execute(new(workspace), "create_hatch_pattern", new
        {
            name = "Invalid hatch",
            lines = new[] { new { angle_degrees = 45, origin = new { x = 0, y = 0 }, offset = new { x = 0, y = 10 }, typo = true } }
        }, false);
        Assert.Equal(originalCount, session.CadEditor.Document.HatchPatterns.Count);
    }

    [Fact]
    public async Task FillStyleToolRejectsUnknownStopPropertiesBeforeCreation()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Gradient schema audit").Session;
        var originalCount = session.CadEditor.Document.Styles.Count;
        await Execute(new(workspace), "create_fill_style", new
        {
            name = "Invalid gradient", mode = "gradient",
            stops = new[] { new { offset = 0, color = "#000000", typo = true }, new { offset = 1, color = "#FFFFFF", typo = true } }
        }, false);
        Assert.Equal(originalCount, session.CadEditor.Document.Styles.Count);
    }

    [Fact]
    public async Task StyleCatalogIncludesGradientPlacementAndHatchLineDefinitions()
    {
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Style inspection audit");
        var executor = new CadWorkspaceToolExecutor(workspace);
        var creation = await Execute(executor, "create_fill_style", new
        {
            name = "Placed gradient", mode = "gradient", angle_degrees = 30, scale = 2,
            origin_x = 12, origin_y = -4, centered = false,
            stops = new[] { new { offset = 0, color = "#000000" }, new { offset = 1, color = "#FFFFFF" } }
        });
        var id = creation.GetProperty("result").GetProperty("result").GetProperty("style_id").GetInt64();
        await Execute(executor, "create_hatch_pattern", new
        {
            name = "Source hatch",
            lines = new[]
            {
                new { angle_degrees = 45, origin = new { x = 0, y = 0 }, offset = new { x = 0, y = 10 }, dash_pattern = new[] { 5, -2 } }
            }
        });
        var catalog = (await Execute(executor, "list_styles", new { })).GetProperty("result").GetProperty("result");
        var properties = catalog.GetProperty("styles").EnumerateArray().Single(style => style.GetProperty("id").GetInt64() == id)
            .GetProperty("properties");
        Assert.Equal(30, properties.GetProperty("angle_degrees").GetDouble(), 8);
        Assert.Equal(2, properties.GetProperty("scale").GetDouble());
        Assert.Equal(12, properties.GetProperty("origin_x").GetDouble());
        Assert.Equal(-4, properties.GetProperty("origin_y").GetDouble());
        Assert.False(properties.GetProperty("centered").GetBoolean());
        Assert.All(catalog.GetProperty("hatch_patterns").EnumerateArray(), pattern =>
            Assert.Equal(pattern.GetProperty("line_count").GetInt32(), pattern.GetProperty("lines").GetArrayLength()));
        var pattern = catalog.GetProperty("hatch_patterns")[0];
        await Execute(executor, "create_hatch_pattern", new { name = "Pattern roundtrip", lines = pattern.GetProperty("lines") });
    }
}
