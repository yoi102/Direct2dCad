using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Theory]
    [InlineData(20, 30)]
    [InlineData(-20, -30)]
    [InlineData(300, 200)]
    public async Task ViewportToolDimensionsSurvivePropertyOnlyUpdatesAndUndo(double x, double y)
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Viewport geometry").Session;
        var layout = session.CadEditor.Document.Layouts.Values.Single();
        var add = await Execute(new(workspace), "add_layout_viewport", new
        { layout_id = layout.Id.Value, x, y, width = 200, height = 120, model_center_x = 0, model_center_y = 0, scale = 1 });
        var id = add.GetProperty("result").GetProperty("result").GetProperty("viewport_id").GetInt64();
        var viewport = layout.GetViewport(new(id));
        var bounds = CadRectD.FromXYWH(x, y, 200, 120);
        Assert.Equal(bounds, viewport.Bounds);
        var updates = new object[]
        {
            new { layout_id = layout.Id.Value, viewport_id = id, locked = true },
            new { layout_id = layout.Id.Value, viewport_id = id, locked = false },
            new { layout_id = layout.Id.Value, viewport_id = id, visible = false },
            new { layout_id = layout.Id.Value, viewport_id = id, visible = true },
            new { layout_id = layout.Id.Value, viewport_id = id, scale = 2 }
        };
        foreach (var update in updates)
        {
            var tool = new CadWorkspaceToolExecutor(workspace);
            await Execute(tool, "set_layout_viewport", update);
            Assert.Equal(bounds, viewport.Bounds);
            await Execute(tool, "undo", new { });
            Assert.Equal(bounds, viewport.Bounds);
            await Execute(tool, "redo", new { });
            Assert.Equal(bounds, viewport.Bounds);
        }
        var move = new CadWorkspaceToolExecutor(workspace);
        await Execute(move, "set_layout_viewport", new { layout_id = layout.Id.Value, viewport_id = id, x = x + 50, y = y + 40 });
        Assert.Equal(CadRectD.FromXYWH(x + 50, y + 40, 200, 120), viewport.Bounds);
        await Execute(move, "undo", new { });
        Assert.Equal(bounds, viewport.Bounds);
    }

    [Fact]
    public async Task FailedScaleToolKeepsGeometryAndQueryVersionCurrent()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Scale rejection").Session;
        var doc = session.CadEditor.Document;
        var line = doc.AddLine(new(100, 100), new(200, 100));
        var definition = doc.CreateBlockDefinition("Tiny", default);
        var block = doc.AddBlockReference(definition, new(100, 100), scaleX: 1e-8, scaleY: 1e-8);
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "transform_entities", new
        { entity_ids = new[] { line.Id.Value, block.Id.Value }, operation = "scale", pivot_x = 0, pivot_y = 0, factor = 0.01 }, false);
        Assert.Equal(new CadPointD(100, 100), line.Start);
        Assert.Equal(new CadPointD(100, 100), block.Position);
        Assert.Equal(1e-8, block.ScaleX);
        Assert.False(session.CadEditor.DocumentCommands.CanUndo);
        Assert.Equal(0, session.CadEditor.DocumentChangeVersion);
        var query = await Execute(executor, "list_entities", new { expected_document_version = 0 });
        Assert.Contains("100", query.ToString());
    }

    [Fact]
    public async Task InvalidLayoutToolDoesNotAccumulateSystemBlocks()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Layout rejection").Session;
        var doc = session.CadEditor.Document;
        var blocks = doc.Blocks.Keys.ToArray();
        for (var i = 0; i < 3; i++)
            await Execute(new(workspace), "create_layout", new { name = "Small", width = 10, height = 10 }, false);
        Assert.Equal(blocks, doc.Blocks.Keys);
        Assert.Single(doc.Layouts);
        Assert.Equal(0, session.CadEditor.DocumentChangeVersion);
        Assert.False(session.CadEditor.DocumentCommands.CanUndo);
    }
}
