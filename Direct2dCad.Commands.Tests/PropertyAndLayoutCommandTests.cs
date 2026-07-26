using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class PropertyAndLayoutCommandTests
{
    [Fact]
    public void SetLineWeight_ByLayerKeepsStoredExplicitValueAndUndoRestoresSource()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        line.SetLineWeight(new CadLineWeight(2.5));
        var command = new SetEntityLineWeightCommand([line.Id], CadLineWeight.ByLayer);

        command.Execute(document);

        Assert.True(line.UseLayerLineWeight);
        Assert.Equal(new CadLineWeight(2.5), line.LineWeight);

        command.Undo(document);

        Assert.False(line.UseLayerLineWeight);
        Assert.Equal(new CadLineWeight(2.5), line.LineWeight);
    }

    [Fact]
    public void SetFillStyle_WithUnsupportedEntityDoesNotPartiallyMutateBatch()
    {
        var document = CadDocument.Create("Test");
        var circle = document.AddCircle(CadPointD.Origin, 5);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var fillStyleId = document.CreateSolidFillStyle("Solid", CadColor.Green);
        var command = new SetEntityFillStyleCommand([circle.Id, line.Id], fillStyleId);

        Assert.Throws<NotSupportedException>(() => command.Execute(document));
        Assert.Null(circle.FillStyleId);
    }

    [Fact]
    public void SetOpacity_UpdatesImageAndOleAndUndoRestoresBoth()
    {
        var document = CadDocument.Create("Test");
        var image = document.AddImage(
            CadRectD.FromXYWH(0, 0, 2, 2),
            1,
            1,
            4,
            [1, 2, 3, 4],
            opacity: 0.8);
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(10, 10, 2, 2),
            [1, 2, 3],
            opacity: 0.6);
        var command = new SetEntityOpacityCommand([image.Id, ole.Id], 0.25);

        var execute = command.Execute(document);
        Assert.Equal(0.25, image.Opacity);
        Assert.Equal(0.25, ole.Opacity);
        Assert.All(
            execute.EntityChanges,
            change => Assert.Equal(CadEntityChangeKind.Opacity, change.Kind));

        var undo = command.Undo(document);
        Assert.Equal(0.8, image.Opacity);
        Assert.Equal(0.6, ole.Opacity);
        Assert.All(
            undo.EntityChanges,
            change => Assert.Equal(CadEntityChangeKind.Opacity, change.Kind));

        command.Execute(document);
        Assert.Equal(0.25, image.Opacity);
        Assert.Equal(0.25, ole.Opacity);
    }

    [Fact]
    public void SetLayerState_UndoRestoresVisibilityLockAndFrozenState()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer("Layer", CadColor.Green, CadLineWeight.Default);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), layerId);
        var command = new SetLayerStateCommand(
            layerId,
            isVisible: false,
            isLocked: true,
            isFrozen: true);

        var executeResult = command.Execute(document);
        var layer = document.GetLayer(layerId);

        Assert.False(layer.IsVisible);
        Assert.True(layer.IsLocked);
        Assert.True(layer.IsFrozen);
        Assert.Contains(executeResult.EntityChanges, change => change.EntityId == line.Id);
        Assert.True(executeResult.AffectsDocumentStructure);

        command.Undo(document);
        Assert.True(layer.IsVisible);
        Assert.False(layer.IsLocked);
        Assert.False(layer.IsFrozen);
    }

    [Fact]
    public void SetViewSettings_UndoRestoresGridPresetsAndOrigin()
    {
        var document = CadDocument.Create("Test");
        var original = CadViewSettingsSnapshot.From(document.ViewSettings);
        var targetSettings = new CadViewSettings();
        targetSettings.BackgroundColor = CadColor.FromArgb(255, 10, 20, 30);
        targetSettings.Grid.Type = CadGridType.Cross;
        targetSettings.Grid.SpacingX = 25;
        targetSettings.Grid.SpacingY = 25;
        targetSettings.Grid.MinorSpacingX = 2.5;
        targetSettings.Grid.MinorSpacingY = 2.5;
        targetSettings.Grid.EnsurePresetSelections();
        targetSettings.Origin.Position = new CadPointD(100, -50);
        targetSettings.Origin.MarkerType = CadOriginMarkerType.Square;
        var command = new SetViewSettingsCommand(targetSettings);

        command.Execute(document);

        Assert.Equal(CadGridType.Cross, document.ViewSettings.Grid.Type);
        Assert.Equal(25, document.ViewSettings.Grid.SpacingX);
        Assert.Equal(new CadPointD(100, -50), document.ViewSettings.Origin.Position);
        Assert.Equal(CadOriginMarkerType.Square, document.ViewSettings.Origin.MarkerType);

        command.Undo(document);

        var restored = CadViewSettingsSnapshot.From(document.ViewSettings);
        Assert.Equal(original with { GridSpacingPresets = restored.GridSpacingPresets }, restored);
        Assert.Equal(original.GridSpacingPresets, restored.GridSpacingPresets);
    }

    [Fact]
    public void CreateLayout_UndoAndRedoReuseLayoutAndPaperSpaceIds()
    {
        var document = CadDocument.Create("Test");
        var command = new CreateLayoutCommand("Sheet A", 594, 420);

        command.Execute(document);
        var layoutId = Assert.IsType<LayoutId>(command.CreatedLayoutId);
        var paperSpaceBlockId = document.GetLayout(layoutId).PaperSpaceBlockId;

        command.Undo(document);
        Assert.False(document.Layouts.ContainsKey(layoutId));
        Assert.True(document.Blocks.ContainsKey(paperSpaceBlockId));

        command.Execute(document);
        Assert.Equal(layoutId, command.CreatedLayoutId);
        Assert.Equal(paperSpaceBlockId, document.GetLayout(layoutId).PaperSpaceBlockId);
    }

    [Fact]
    public void SetLayoutViewport_UndoRestoresAllViewportProperties()
    {
        var document = CadDocument.Create("Test");
        var layout = document.GetLayout(LayoutId.Default);
        var viewport = layout.Viewports[0];
        var original = CadLayoutViewportSnapshot.From(viewport);
        var target = new CadLayoutViewportSnapshot(
            CadRectD.FromXYWH(20, 30, 100, 80),
            new CadPointD(250, 175),
            2.5,
            0.25,
            IsVisible: false,
            IsLocked: true);
        var command = new SetLayoutViewportCommand(layout.Id, viewport.Id, target);

        command.Execute(document);
        Assert.Equal(target, CadLayoutViewportSnapshot.From(viewport));

        command.Undo(document);
        Assert.Equal(original, CadLayoutViewportSnapshot.From(viewport));
    }
}
