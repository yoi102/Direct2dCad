using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RegionQueriesAcceptSingleAndMultipleTypeFilters(bool throughWorkspace, bool multipleTypes)
    {
        using var workspace = new HeadlessWorkspace();
        var host = workspace.CreateDocument("Region queries");
        var document = host.Session.CadEditor.Document;
        var region = document.AddRegion([new([CadPlanarPrimitive.Arc(default, 10, 0, Math.Tau)])]);
        document.AddCircle(default, 5);
        object filter = multipleTypes ? new { types = new[] { "Region" } } : new { type = "region" };

        var page = await Query("list_entities");
        Assert.Equal(1, page.GetProperty("total_matches").GetInt32());
        var entity = Assert.Single(page.GetProperty("entities").EnumerateArray());
        Assert.Equal(region.Id.Value, entity.GetProperty("id").GetInt64());
        Assert.Equal("Region", entity.GetProperty("type").GetString());
        Assert.Equal(-10, entity.GetProperty("bounds").GetProperty("min_x").GetDouble());
        Assert.Equal(10, entity.GetProperty("bounds").GetProperty("max_x").GetDouble());

        var statistics = await Query("get_entity_statistics");
        Assert.Equal(1, statistics.GetProperty("matching").GetProperty("entity_count").GetInt32());
        Assert.False(host.Session.CadEditor.DocumentCommands.CanUndo);

        async Task<JsonElement> Query(string name)
        {
            var call = new AiToolCall(Guid.NewGuid().ToString(), name, JsonSerializer.Serialize(filter));
            var text = throughWorkspace
                ? await new CadWorkspaceToolExecutor(workspace).ExecuteAsync(call, default)
                : new CadDocumentToolExecutor(host.Session, document.Id.Value).Execute(call);
            using var json = JsonDocument.Parse(text);
            var result = json.RootElement;
            Assert.True(result.GetProperty("success").GetBoolean(), text);
            result = result.GetProperty("result");
            return result.Clone();
        }
    }
}
