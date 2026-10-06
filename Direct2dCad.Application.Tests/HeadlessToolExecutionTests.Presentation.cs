using Direct2dCad.Application.Tools;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task LayoutToolsCreatePaperEntitiesAndViewportModelEntitiesInTheirExplicitSpaces()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Layout flow").Session;
        var executor = new CadWorkspaceToolExecutor(workspace);
        var created = await Execute(executor, "create_layout", new { name = "Sheet", width = 297, height = 210 });
        var layoutId = created.GetProperty("result").GetProperty("result").GetProperty("layout_id").GetInt64();
        await Execute(executor, "activate_space", new { space = "paper", layout_id = layoutId });
        await Execute(executor, "add_line", new { x1 = 10, y1 = 10, x2 = 50, y2 = 10 });
        var paperEntity = Assert.Single(session.CadEditor.Document.Entities.Values);
        Assert.Equal(session.CadEditor.Document.GetLayout(new(layoutId)).PaperSpaceBlockId, paperEntity.OwnerBlockId);
        var viewport = await Execute(executor, "add_layout_viewport", new
        {
            layout_id = layoutId, x = 10, y = 20, width = 250, height = 160, model_center_x = 0, model_center_y = 0, scale = 0.1
        });
        var viewportId = viewport.GetProperty("result").GetProperty("result").GetProperty("viewport_id").GetInt64();
        await Execute(executor, "activate_space", new { space = "viewport", layout_id = layoutId, viewport_id = viewportId });
        await Execute(executor, "add_circle", new { center_x = 0, center_y = 0, radius = 12 });
        Assert.Equal(BlockId.ModelSpace, session.CadEditor.Document.Entities.Values.OfType<CadCircle>().Single().OwnerBlockId);
        await Execute(executor, "delete_layout_viewport", new { layout_id = layoutId, viewport_id = viewportId });
        Assert.True(session.IsPaperSpaceActive);
        Assert.DoesNotContain(session.CadEditor.Document.GetLayout(new(layoutId)).Viewports, viewport => viewport.Id.Value == viewportId);
        // One request has one undo batch per document, including layout/content operations.
        await Execute(executor, "undo", new { });
        Assert.False(session.CadEditor.Document.Layouts.ContainsKey(new(layoutId)));
        Assert.True(session.IsModelSpaceActive);
        Assert.DoesNotContain(session.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        await Execute(executor, "redo", new { });
        Assert.Equal(new BlockId(session.CadEditor.Document.GetLayout(new(layoutId)).PaperSpaceBlockId.Value), paperEntity.OwnerBlockId);
        Assert.False(paperEntity.IsErased);
    }

    [Fact]
    public async Task LayoutFailuresDoNotSwitchSpaceOrChangeHistoryAndLocksRequireExplicitUnlock()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Validation").Session;
        var executor = new CadWorkspaceToolExecutor(workspace);
        var layout = session.CadEditor.Document.Layouts.Values.Single();
        await Execute(executor, "delete_layout", new { layout_id = layout.Id.Value, confirm = true }, false);
        await Execute(executor, "activate_space", new { space = "paper", layout_id = layout.Id.Value, viewport_id = 12 }, false);
        Assert.True(session.IsModelSpaceActive);
        Assert.False(session.CadEditor.DocumentCommands.CanUndo);
        var prior = layout.PaperWidth;
        await Execute(executor, "set_layout_paper", new { layout_id = layout.Id.Value, width = 5 }, false);
        Assert.Equal(prior, layout.PaperWidth);
        Assert.False(session.CadEditor.DocumentCommands.CanUndo);
        var viewportId = session.CadEditor.Document.AddLayoutViewport(layout.Id, new(10, 10, 100, 100), default, 1);
        var viewport = layout.GetViewport(viewportId);
        viewport.SetLocked(true);
        await Execute(executor, "set_layout_viewport", new { layout_id = layout.Id.Value, viewport_id = viewportId.Value, scale = 2 }, false);
        Assert.Equal(1, viewport.Scale);
        await Execute(executor, "set_layout_viewport", new { layout_id = layout.Id.Value, viewport_id = viewportId.Value, scale = 2, locked = false });
        Assert.Equal(2, viewport.Scale);
        Assert.False(viewport.IsLocked);
        await Execute(executor, "undo", new { });
        Assert.True(viewport.IsLocked);
        Assert.Equal(1, viewport.Scale);
    }

    [Fact]
    public async Task DimensionReassociationPreservesAppearanceAndTracksNewGeometryWithUndo()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Reassociate").Session;
        var first = session.CadEditor.AddLine(default, new(10, 0));
        var second = session.CadEditor.AddLine(default, new(25, 0));
        var setup = new CadWorkspaceToolExecutor(workspace);
        await Execute(setup, "add_dimension", new { kind = "Aligned", source_entity_id = first.Value, x = 5, y = 4, text_height = 6 });
        var dimension = session.CadEditor.Document.Entities.Values.OfType<CadDimension>().Single();
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "reassociate_dimension", new { entity_id = dimension.Id.Value, source_entity_id = second.Value });
        Assert.Equal(25, dimension.Measurement);
        Assert.Equal(CadAssociationState.Valid, dimension.AssociationState);
        Assert.Equal(6, dimension.Definition.Style.TextHeight);
        Assert.Equal(4, dimension.Definition.Placement.Y);
        Assert.All(dimension.Definition.Anchors, anchor => Assert.Equal(second.Value, anchor.Reference!.EntityId));
        await Execute(executor, "undo", new { });
        Assert.Equal(10, dimension.Measurement);
        Assert.All(dimension.Definition.Anchors, anchor => Assert.Equal(first.Value, anchor.Reference!.EntityId));
        dimension.SetLocked(true);
        await Execute(executor, "reassociate_dimension", new { entity_id = dimension.Id.Value, source_entity_id = second.Value }, false);
        Assert.Equal(10, dimension.Measurement);
    }

    [Fact]
    public async Task ExplicitDimensionReferencesRejectWrongFeatureBeforeChangingTheDimension()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("References").Session;
        var line = session.CadEditor.AddLine(default, new(10, 0));
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "add_dimension", new { kind = "Aligned", source_entity_id = line.Value, x = 5, y = 3 });
        var dimension = session.CadEditor.Document.Entities.Values.OfType<CadDimension>().Single();
        var version = session.CadEditor.DocumentChangeVersion;
        await Execute(executor, "reassociate_dimension", new { entity_id = dimension.Id.Value,
            references = new[] { new { entity_id = line.Value, feature = "CircleCenter" }, new { entity_id = line.Value, feature = "LineEnd" } } }, false);
        Assert.Equal(version, session.CadEditor.DocumentChangeVersion);
        Assert.Equal(10, dimension.Measurement);
        await Execute(executor, "reassociate_dimension", new { entity_id = dimension.Id.Value,
            references = new[] { new { entity_id = line.Value, feature = "LineStart" }, new { entity_id = line.Value, feature = "LineEnd" } } });
        Assert.Equal(CadAssociationState.Valid, dimension.AssociationState);
    }

    [Fact]
    public async Task HostsWithoutRenderingOrPrintingReturnExplicitFailure()
    {
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Headless");
        var executor = new CadWorkspaceToolExecutor(workspace);
        var capture = await Execute(executor, "capture_view", new { }, false);
        Assert.Contains("unavailable", capture.GetProperty("error").GetString());
        await Execute(executor, "print_document", new { }, false);
    }

    [Fact]
    public async Task LateCaptureCannotDescribeAChangedDocumentAsCurrent()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Changed capture").Session;
        var image = new CadToolImage([137, 80, 78, 71], "image/png", 128, 128);
        workspace.CaptureHandler = (_, _, _) =>
        {
            session.CadEditor.AddLine(default, new(10, 0));
            return Task.FromResult(image);
        };
        var result = await Execute(new(workspace), "capture_view", new { }, false);
        Assert.Contains("changed", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrintResultSeparatesCancelledAndSubmittedFromPhysicalCompletion(bool submitted)
    {
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Print result");
        workspace.PrintHandler = (_, _) => Task.FromResult(submitted);
        var result = await Execute(new(workspace), "print_document", new { }, submitted);
        if (submitted)
        {
            Assert.True(result.GetProperty("result").GetProperty("submitted").GetBoolean());
            Assert.False(result.GetProperty("result").GetProperty("completed").GetBoolean());
        }
        else Assert.Equal("cancelled", result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancellationRacingWithSubmittedPrintPreservesTheSubmissionReceipt()
    {
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Print race");
        using var cancellation = new CancellationTokenSource();
        workspace.PrintHandler = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(true);
        };
        var result = await new CadWorkspaceToolExecutor(workspace).ExecuteAsync(new("print", "print_document", "{}"), cancellation.Token);
        using var json = System.Text.Json.JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("result").GetProperty("submitted").GetBoolean());
    }
}
