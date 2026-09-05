using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Tools;
using static Direct2dCad.ViewModels.Tests.ToolExecutionWorkspace;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AgentExecutionContractTests
{
    public static IEnumerable<object[]> Creations => new (string Type, string Json)[]
    {
        ("line", "{\"x1\":0,\"y1\":0,\"x2\":20,\"y2\":30}"),
        ("circle", "{\"center_x\":0,\"center_y\":0,\"radius\":20}"),
        ("rectangle", "{\"min_x\":0,\"min_y\":0,\"max_x\":20,\"max_y\":30,\"corner_radius\":2}"),
        ("arc", "{\"center_x\":0,\"center_y\":0,\"radius\":20,\"start_angle_degrees\":30,\"sweep_angle_degrees\":120}"),
        ("ellipse", "{\"center_x\":0,\"center_y\":0,\"radius_x\":20,\"radius_y\":10}"),
        ("ellipse_arc", "{\"center_x\":0,\"center_y\":0,\"radius_x\":20,\"radius_y\":10,\"start_angle_degrees\":30,\"sweep_angle_degrees\":120}"),
        ("text", "{\"text\":\"CAD\",\"x\":0,\"y\":0,\"height\":10,\"rotation_degrees\":30}"),
        ("shape_text", "{\"text\":\"CAD\",\"x\":0,\"y\":0,\"height\":10,\"rotation_degrees\":30}"),
        ("polyline", "{\"points\":[{\"x\":0,\"y\":0},{\"x\":20,\"y\":0},{\"x\":20,\"y\":30}]}"),
        ("polygon", "{\"points\":[{\"x\":0,\"y\":0},{\"x\":20,\"y\":0},{\"x\":20,\"y\":30}]}"),
        ("spline", "{\"fit_points\":[{\"x\":0,\"y\":0},{\"x\":20,\"y\":30},{\"x\":40,\"y\":0}]}"),
        ("composite_path", "{\"start\":{\"x\":0,\"y\":0},\"segments\":[{\"type\":\"line\",\"end\":{\"x\":20,\"y\":0}},{\"type\":\"arc\",\"center\":{\"x\":20,\"y\":10},\"sweep_angle_degrees\":90}],\"closed\":false}")
    }.Select(item => new object[] { item.Type, item.Json });

    [Theory]
    [MemberData(nameof(Creations))]
    public async Task StyledCreationAndGeometryQueryRoundTripUndoRedo(string type, string geometry)
    {
        using var workspace = new ToolExecutionWorkspace();
        var tab = workspace.CreateDocument("Creation");
        var editor = tab.DocumentViewModel.CadEditor;
        var history = editor.CreateDocumentHistorySnapshot();
        var executor = new CadWorkspaceToolExecutor(workspace);
        var input = JsonNode.Parse(geometry)!.AsObject();
        input["name"] = "Styled";
        input["line_weight"] = 0.7;
        input["color"] = "#FF0000";
        input["z_index"] = 42;
        await Execute(executor, $"add_{type}", input.ToJsonString());
        var entity = Assert.Single(editor.Document.Entities.Values, item => !item.IsErased);
        Assert.Equal("Styled", entity.Name);
        Assert.Equal(42, entity.ZIndex);
        Assert.Equal(new CadLineWeight(0.7), entity.LineWeight);
        Assert.Equal(CadColorSource.Explicit, entity.ColorSource);
        Assert.Contains(entity.Id, editor.Selection.EntityIds);
        await Execute(executor, "get_entity_geometry", new { entity_id = entity.Id.Value });
        editor.Undo();
        Assert.True(entity.IsErased);
        Assert.True(editor.DocumentHistoryEquals(history));
        editor.Redo();
        Assert.False(entity.IsErased);
        Assert.Equal(42, entity.ZIndex);
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public async Task CommonPropertiesAndMoveWorkForEveryEntity(TestEntityKind kind)
    {
        using var workspace = new ToolExecutionWorkspace();
        var tab = workspace.CreateDocument("Modify");
        var editor = tab.DocumentViewModel.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var name = entity.Name;
        var bounds = entity.Bounds;
        var layer = editor.Document.CreateLayer("Destination", CadColor.Blue, CadLineWeight.Default);
        var history = editor.CreateDocumentHistorySnapshot();
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "set_entity_common_properties", new { entity_ids = new[] { entity.Id.Value },
            name = "Updated", layer = "Destination", z_index = 15, line_weight = 0.5 });
        Assert.Equal("Updated", entity.Name);
        Assert.Equal(layer, entity.LayerId);
        Assert.Equal(15, entity.ZIndex);
        await Execute(executor, "move_entities", new { entity_ids = new[] { entity.Id.Value }, delta_x = 10, delta_y = 20 });
        Assert.Equal(bounds.MinX + 10, entity.Bounds.MinX, 6);
        Assert.Equal(bounds.MinY + 20, entity.Bounds.MinY, 6);
        editor.Undo();
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Equal(name, entity.Name);
        Assert.Equal(bounds, entity.Bounds);
        editor.Redo();
        Assert.Equal("Updated", entity.Name);
        Assert.Equal(layer, entity.LayerId);
    }

    [Fact]
    public async Task BulkMixedCreationIsAtomicAndOneUndoGroup()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Batch").DocumentViewModel.CadEditor;
        var history = editor.CreateDocumentHistorySnapshot();
        var executor = new CadWorkspaceToolExecutor(workspace);
        var items = new JsonArray(Creations.Select(row =>
        {
            var item = JsonNode.Parse((string)row[1])!.AsObject();
            item["type"] = (string)row[0];
            return (JsonNode)item;
        }).ToArray());
        await Execute(executor, "add_entities", new JsonObject { ["entities"] = items.DeepClone() }.ToJsonString());
        Assert.Equal(12, editor.Document.Entities.Values.Count(item => !item.IsErased));
        editor.Undo();
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.DoesNotContain(editor.Document.Entities.Values, item => !item.IsErased);
        items.Add(JsonNode.Parse("{\"type\":\"circle\",\"center_x\":0,\"center_y\":0,\"radius\":-1}"));
        await Execute(new(workspace), "add_entities", new JsonObject { ["entities"] = items }.ToJsonString(), success: false);
        Assert.True(editor.DocumentHistoryEquals(history));
        editor.Redo();
        Assert.Equal(12, editor.Document.Entities.Values.Count(item => !item.IsErased));
    }

    [Fact]
    public async Task FailedSpecificEditRollsBackEarlierPropertiesAndPreservesRedo()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Rollback").DocumentViewModel.CadEditor;
        var text = editor.Document.AddText("Original", new(0, 0), 10);
        editor.SetEntityZIndex(text.Id, 7);
        editor.Undo();
        var history = editor.CreateDocumentHistorySnapshot();
        await Execute(new(workspace), "set_entity_specific_properties",
            new { entity_ids = new[] { text.Id.Value }, text = "Changed", inverted_margin_factor = -1 }, success: false);
        Assert.Equal("Original", text.Text);
        Assert.True(editor.DocumentHistoryEquals(history));
        editor.Redo();
        Assert.Equal(7, text.ZIndex);
    }

    [Fact]
    public async Task DefaultDocumentStaysPinnedUntilExplicitActivationAndClosedDocumentIsRejected()
    {
        using var workspace = new ToolExecutionWorkspace();
        var one = workspace.CreateDocument("One");
        var executor = new CadWorkspaceToolExecutor(workspace);
        var two = workspace.CreateDocument("Two");
        var line = "{\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":10}";
        await Execute(executor, "add_line", line);
        Assert.Single(one.DocumentViewModel.CadEditor.Document.Entities);
        Assert.Empty(two.DocumentViewModel.CadEditor.Document.Entities);
        await Execute(executor, "activate_document", new { document_id = two.DocumentId });
        await Execute(executor, "add_line", line);
        Assert.Single(two.DocumentViewModel.CadEditor.Document.Entities);
        await Execute(executor, "rename_document", new { name = "Renamed" });
        Assert.Equal("Renamed", two.EditorTab.DocumentName);
        await Execute(executor, "save_document", new { file_path = "saved.d2cad" });
        Assert.False(two.EditorTab.IsModified);
        workspace.AllowClose = false;
        await Execute(executor, "close_document", new { });
        Assert.Equal(2, workspace.GetDocuments().Count);
        workspace.AllowClose = true;
        await Execute(executor, "close_document", new { });
        Assert.Single(workspace.GetDocuments());
        await Execute(executor, "get_document_summary", new { document_id = two.DocumentId }, success: false);
        await Execute(executor, "add_line", line);
        Assert.Equal(2, one.DocumentViewModel.CadEditor.Document.Entities.Count);
    }

    [Theory]
    [InlineData("create_document")]
    [InlineData("add_line")]
    [InlineData("get_document_summary")]
    public async Task CancelledInvocationDoesNotStartAnyOperation(string name)
    {
        using var workspace = new ToolExecutionWorkspace();
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(new("1", name, "{}"), new(true)));
        Assert.Empty(workspace.GetDocuments());
    }
}
