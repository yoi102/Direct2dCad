using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Application.Platform;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task GridPresetsCanBeUpdatedReorderedAndUndoneWithoutChangingTheirIds()
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Presets").Session.CadEditor.Document;
        var initial = document.ViewSettings.Grid.SpacingPresets.ToArray();
        var id = initial[0].Id;
        await Execute(new(workspace), "manage_grid_presets", new { operation = "update", preset_id = id,
            spacing_x_millimeters = 37, spacing_y_millimeters = 19, link_axes = false });
        Assert.Equal(37, document.ViewSettings.Grid.SpacingPresets.Single(preset => preset.Id == id).SpacingX);
        Assert.Equal(19, document.ViewSettings.Grid.SpacingPresets.Single(preset => preset.Id == id).SpacingY);
        var reversed = initial.Select(preset => preset.Id).Reverse().ToArray();
        await Execute(new(workspace), "manage_grid_presets", new { operation = "reorder", preset_ids = reversed });
        Assert.Equal(reversed, document.ViewSettings.Grid.SpacingPresets.Select(preset => preset.Id));
        await Execute(new(workspace), "undo", new { });
        Assert.Equal(initial.Select(preset => preset.Id), document.ViewSettings.Grid.SpacingPresets.Select(preset => preset.Id));
        await Execute(new(workspace), "undo", new { });
        Assert.Equal(initial, document.ViewSettings.Grid.SpacingPresets);
        await Execute(new(workspace), "manage_grid_presets", new { operation = "reorder", preset_ids = new[] { id, id } }, false);
        Assert.Equal(initial, document.ViewSettings.Grid.SpacingPresets);
    }

    [Fact]
    public async Task BlockBasePointChangesAreUndoableAndRespectReadOnlyDefinitions()
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Blocks").Session.CadEditor.Document;
        var id = document.CreateBlockDefinition("Editable", default);
        var block = document.GetBlock(id);
        await Execute(new(workspace), "set_block_base_point", new { block = "Editable", x = 25, y = -3 });
        Assert.Equal(new CadPointD(25, -3), block.BasePoint);
        await Execute(new(workspace), "undo", new { });
        Assert.Equal(CadPointD.Origin, block.BasePoint);
        block.SetReadOnly(true);
        await Execute(new(workspace), "set_block_base_point", new { block = "Editable", x = 5, y = 4 }, false);
        Assert.Equal(CadPointD.Origin, block.BasePoint);
    }

    [Fact]
    public async Task ClipboardImageUsesTheImageCreationPipelineAndEmptyClipboardDoesNotMutate()
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Clipboard").Session.CadEditor.Document;
        var importer = new ClipboardImageImporter();
        var arguments = new { bounds = new { min_x = 0, min_y = 0, max_x = 20, max_y = 10 } };
        await Execute(new(workspace, importer), "insert_image_from_clipboard", arguments, false);
        Assert.Empty(document.Entities);
        importer.Image = new(1, 1, 4, [0, 0, 0, 255], "image/png", "clipboard");
        await Execute(new(workspace, importer), "insert_image_from_clipboard", arguments);
        var image = Assert.IsType<CadImage>(Assert.Single(document.Entities.Values));
        await Execute(new(workspace), "undo", new { });
        Assert.True(image.IsErased);
    }

    private sealed class ClipboardImageImporter : IImageImportService
    {
        public CadImageImportData? Image { get; set; }
        public CadImageImportData? LoadFromClipboard() => Image;
        public CadImageImportData LoadFromFile(string path) => throw new NotSupportedException();
        public string CreatePngDataUrl(CadImageImportData image) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    public async Task TransferUsesClipboardGeometryAndIndependentUndo(string operation)
    {
        using var workspace = new HeadlessWorkspace();
        var source = workspace.CreateDocument("Source");
        var target = workspace.CreateDocument("Target");
        var line = source.Session.CadEditor.Document.AddLine(default, new(10, 0));
        await Execute(new(workspace), "transfer_entities", new
        {
            source_document_id = source.DocumentId, target_document_id = target.DocumentId, operation,
            entity_ids = new[] { line.Id.Value }, delta_x = 25, delta_y = 8, confirm = true
        });
        var pasted = Assert.IsType<CadLine>(Assert.Single(target.Session.CadEditor.Document.Entities.Values));
        Assert.Equal(new CadPointD(25, 8), pasted.Start);
        Assert.Equal(new CadPointD(35, 8), pasted.End);
        Assert.Equal(operation == "move", line.IsErased);
        await Execute(new(workspace), "undo", new { document_id = target.DocumentId });
        Assert.True(pasted.IsErased);
        await Execute(new(workspace), "redo", new { document_id = target.DocumentId });
        Assert.False(pasted.IsErased);
        if (operation == "move")
        {
            await Execute(new(workspace), "undo", new { document_id = source.DocumentId });
            Assert.False(line.IsErased);
            Assert.False(pasted.IsErased);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task FailedMoveLeavesBothDocumentsAndHistoriesUntouched(bool confirm, bool locked)
    {
        using var workspace = new HeadlessWorkspace();
        var source = workspace.CreateDocument("Source");
        var target = workspace.CreateDocument("Target");
        var line = source.Session.CadEditor.Document.AddLine(default, new(10, 0));
        line.SetLocked(locked);
        var sourceHistory = source.Session.CadEditor.CreateDocumentHistorySnapshot();
        var targetHistory = target.Session.CadEditor.CreateDocumentHistorySnapshot();
        await Execute(new(workspace), "transfer_entities", new
        {
            source_document_id = source.DocumentId, target_document_id = target.DocumentId,
            operation = "move", entity_ids = new[] { line.Id.Value }, confirm
        }, false);
        Assert.False(line.IsErased);
        Assert.Empty(target.Session.CadEditor.Document.Entities);
        Assert.True(source.Session.CadEditor.DocumentHistoryEquals(sourceHistory));
        Assert.True(target.Session.CadEditor.DocumentHistoryEquals(targetHistory));
    }

    [Fact]
    public async Task TransferRespectsDestinationLayerAndActiveBlockSpace()
    {
        using var workspace = new HeadlessWorkspace();
        var source = workspace.CreateDocument("Source");
        var target = workspace.CreateDocument("Target");
        var line = source.Session.CadEditor.Document.AddLine(default, new(10, 0));
        var blockId = target.Session.CadEditor.Document.CreateBlockDefinition("Destination", default);
        target.Session.EditBlockDefinition(blockId);
        var destinationLayer = target.Session.CadEditor.Document.GetLayer(Direct2dCad.Db.LayerId.Default);
        destinationLayer.SetLocked(true);
        var arguments = new { source_document_id = source.DocumentId, target_document_id = target.DocumentId,
            operation = "copy", entity_ids = new[] { line.Id.Value } };
        await Execute(new(workspace), "transfer_entities", arguments, false);
        Assert.Empty(target.Session.CadEditor.Document.Entities);
        destinationLayer.SetLocked(false);
        await Execute(new(workspace), "transfer_entities", arguments);
        Assert.Equal(blockId, Assert.Single(target.Session.CadEditor.Document.Entities.Values).OwnerBlockId);
    }

    [Fact]
    public async Task TemplateDiscoveryMatchesEverySourceTemplate()
    {
        using var workspace = new HeadlessWorkspace();
        var result = await Execute(new(workspace), "list_document_templates", new { });
        Assert.Equal(CadEngineeringTemplates.Keys, result.GetProperty("result").GetProperty("templates")
            .EnumerateArray().Select(item => item.GetProperty("key").GetString()));
    }
}
