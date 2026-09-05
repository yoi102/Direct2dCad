using System.Text.Json.Nodes;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Tools;
using static Direct2dCad.ViewModels.Tests.ToolExecutionWorkspace;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AgentEntityMutationTests
{
    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public async Task ExactGeometryCanBeReadRestoredAndUndoneForEveryEntity(TestEntityKind kind)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Geometry").DocumentViewModel.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var original = entity.Bounds;
        var read = await Execute(new(workspace), "get_entity_geometry", new { entity_id = entity.Id.Value });
        var payload = read.GetProperty("result").GetProperty("result");
        var geometry = JsonNode.Parse(payload.GetProperty("geometry").GetRawText())!.AsObject();
        var fields = CadWorkspaceToolExecutor.ToolDefinitions.Single(tool => tool.Name == "set_entity_geometry")
            .Parameters.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToHashSet();
        foreach (var key in geometry.Select(property => property.Key).Where(key => !fields.Contains(key)).ToArray())
            geometry.Remove(key);
        geometry["entity_id"] = entity.Id.Value;
        geometry["entity_type"] = payload.GetProperty("type").GetString();
        await Execute(new(workspace), "transform_entities", new { entity_ids = new[] { entity.Id.Value },
            operation = "move", delta_x = 50, delta_y = 60 });
        var moved = entity.Bounds;
        await Execute(new(workspace), "set_entity_geometry", geometry.ToJsonString());
        AssertBounds(original, entity.Bounds);
        editor.Undo();
        AssertBounds(moved, entity.Bounds);
        editor.Redo();
        AssertBounds(original, entity.Bounds);
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public async Task DuplicateKeepsSourceTypeAppearanceAndUndoDoesNotEraseSource(TestEntityKind kind)
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Duplicate").DocumentViewModel;
        var editor = vm.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var original = entity.Bounds;
        await Execute(new(workspace), "set_entity_common_properties", new { entity_ids = new[] { entity.Id.Value },
            name = "Source", z_index = 12, line_weight = 0.3,
            color = kind is TestEntityKind.Image or TestEntityKind.Ole ? null : "#0000FF" });
        await Execute(new(workspace), "duplicate_entities", new { entity_ids = new[] { entity.Id.Value }, delta_x = 15, delta_y = 25 });
        var copyId = Assert.Single(editor.Selection.EntityIds);
        var copy = editor.Document.GetEntity(copyId);
        Assert.NotEqual(entity.Id, copyId);
        Assert.Equal(entity.GetType(), copy.GetType());
        Assert.Equal(entity.ZIndex, copy.ZIndex);
        Assert.Equal(entity.LineWeight, copy.LineWeight);
        Assert.Equal(entity.ColorSource, copy.ColorSource);
        Assert.Equal(original.MinX + 15, copy.Bounds.MinX, 6);
        Assert.Equal(original.MinY + 25, copy.Bounds.MinY, 6);
        editor.Undo();
        Assert.True(copy.IsErased);
        Assert.False(entity.IsErased);
        AssertBounds(original, entity.Bounds);
        editor.Redo();
        Assert.False(copy.IsErased);
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public async Task WrongTypePropertiesAreRejectedWithoutCreatingHistory(TestEntityKind kind)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Validation").DocumentViewModel.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var history = editor.CreateDocumentHistorySnapshot();
        var input = new JsonObject { ["entity_ids"] = new JsonArray(entity.Id.Value) };
        if (kind is TestEntityKind.Text or TestEntityKind.ShapeText)
            input["opacity"] = 0.5;
        else
            input["text"] = "Not text";
        await Execute(new(workspace), "set_entity_specific_properties", input.ToJsonString(), success: false);
        await Execute(new(workspace), "set_entity_geometry", new { entity_id = entity.Id.Value, entity_type = "WrongType" }, success: false);
        Assert.True(editor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData(TestEntityKind.Circle)]
    [InlineData(TestEntityKind.Ellipse)]
    [InlineData(TestEntityKind.Rectangle)]
    [InlineData(TestEntityKind.Polygon)]
    [InlineData(TestEntityKind.Spline)]
    [InlineData(TestEntityKind.CompositePath)]
    public async Task ClosedEntitiesSupportAllFillModesAndResourceRollback(TestEntityKind kind)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Fill").DocumentViewModel.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        if (entity is CadSpline spline) spline.SetClosed(true);
        if (entity is CadCompositePath path) path.ReplaceGeometry(path.StartPoint, path.Segments, true);
        StyleId? Fill() => entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline polyline => polyline.FillStyleId,
            CadSpline spline => spline.FillStyleId,
            CadCompositePath path => path.FillStyleId,
            _ => throw new InvalidOperationException()
        };
        var history = editor.CreateDocumentHistorySnapshot();
        var initialStyles = editor.Document.Styles.Count;
        var executor = new CadWorkspaceToolExecutor(workspace);
        var ids = new[] { entity.Id.Value };
        await Execute(executor, "set_entity_fill", new { entity_ids = ids, mode = "solid", color = "#FF0000" });
        var solid = Fill();
        Assert.NotNull(solid);
        await Execute(executor, "set_entity_fill", new { entity_ids = ids, mode = "hatch", pattern = "ANSI31", color = "#00FF00", scale = 2, angle_degrees = 30 });
        Assert.NotEqual(solid, Fill());
        await Execute(executor, "set_entity_fill", new { entity_ids = ids, mode = "gradient", gradient_kind = "Linear",
            stops = new[] { new { offset = 0, color = "#FF0000" }, new { offset = 1, color = "#0000FF" } } });
        var gradient = Fill();
        Assert.NotNull(gradient);
        await Execute(executor, "set_entity_fill", new { entity_ids = ids, mode = "none" });
        Assert.Null(Fill());
        editor.Undo();
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Equal(initialStyles, editor.Document.Styles.Count);
        Assert.Null(Fill());
        editor.Redo();
        Assert.Null(Fill());
        Assert.True(editor.Document.Styles.ContainsKey(gradient!.Value));
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public async Task StrokeStyleAcceptsOnlySupportedEntityTypes(TestEntityKind kind)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Stroke").DocumentViewModel.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var supportsStroke = kind is not (TestEntityKind.Text or TestEntityKind.ShapeText or TestEntityKind.Image or TestEntityKind.Ole or TestEntityKind.Block);
        var before = entity.StrokeStyle;
        await Execute(new(workspace), "set_entity_stroke_style", new { entity_ids = new[] { entity.Id.Value },
            dash_style = "DashDot", dash_cap = "Round" }, success: supportsStroke);
        if (!supportsStroke)
        {
            Assert.Equal(before, entity.StrokeStyle);
            return;
        }
        Assert.Equal(CadStrokeDashStyle.DashDot, entity.StrokeStyle.DashStyle);
        Assert.Equal(CadStrokeCap.Round, entity.StrokeStyle.DashCap);
        editor.Undo();
        Assert.Equal(before, entity.StrokeStyle);
        editor.Redo();
        Assert.Equal(CadStrokeDashStyle.DashDot, entity.StrokeStyle.DashStyle);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("frozen")]
    [InlineData("other_space")]
    [InlineData("erased")]
    public async Task UneditableTargetsRejectMutationsWithoutSideEffects(string restriction)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Access").DocumentViewModel.CadEditor;
        var entity = editor.Document.AddCircle(new(0, 0), 5);
        var layer = editor.Document.GetLayer(entity.LayerId);
        switch (restriction)
        {
            case "locked": editor.SetLayerState(layer.Id, isVisible: true, isLocked: true, isFrozen: false); break;
            case "frozen": editor.SetLayerState(layer.Id, isVisible: true, isLocked: false, isFrozen: true); break;
            case "other_space": editor.Document.MoveEntityToBlock(entity.Id, BlockId.PaperSpace); break;
            case "erased": editor.DeleteEntities([entity.Id]); break;
        }
        var history = editor.CreateDocumentHistorySnapshot();
        var styles = editor.Document.Styles.Count;
        await Execute(new(workspace), "set_entity_fill", new { entity_ids = new[] { entity.Id.Value }, mode = "solid", color = "#123456" }, success: false);
        await Execute(new(workspace), "set_entity_geometry", new { entity_id = entity.Id.Value, center = new { x = 50, y = 50 }, radius = 10 }, success: false);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Equal(styles, editor.Document.Styles.Count);
        Assert.Equal(5, entity.Radius);
    }

    private static void AssertBounds(Direct2dCad.Db.Geometry.CadRectD expected, Direct2dCad.Db.Geometry.CadRectD actual)
    {
        Assert.Equal(expected.MinX, actual.MinX, 6);
        Assert.Equal(expected.MinY, actual.MinY, 6);
        Assert.Equal(expected.MaxX, actual.MaxX, 6);
        Assert.Equal(expected.MaxY, actual.MaxY, 6);
    }
}
