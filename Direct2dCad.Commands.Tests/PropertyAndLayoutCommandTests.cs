using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
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
    public void SetEntityLocked_AllowsUndoableLockAndUnlock()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var command = new SetEntityLockedCommand([line.Id], true);

        command.Execute(document);
        Assert.True(line.IsLocked);
        Assert.Throws<InvalidOperationException>(() => new SetEntityLineWeightCommand(
            [line.Id],
            new CadLineWeight(0.5)).Execute(document));

        command.Undo(document);
        Assert.False(line.IsLocked);

        var unlock = new SetEntityLockedCommand([line.Id], false);
        unlock.Execute(document);
        Assert.False(line.IsLocked);
    }

    [Fact]
    public void SetTextStyleProperties_UpdatesAllReferencesAndUndoRestoresBoundsMeasurement()
    {
        var document = CadDocument.Create("Test");
        var styleId = document.CreateTextStyle("Notes", "Arial", 1.0);
        var first = document.AddText("A", CadPointD.Origin, 10, textStyleId: styleId);
        var second = document.AddText("BBBB", new CadPointD(20, 0), 10, textStyleId: styleId);
        first.SetLocalBounds(CadRectD.FromXYWH(0, 0, 5, 10));
        second.SetLocalBounds(CadRectD.FromXYWH(0, 0, 20, 10));

        var command = new SetTextStylePropertiesCommand(
            styleId,
            fontFamily: "Meiryo",
            textHeight: 2.0,
            widthFactor: 0.8,
            obliqueAngle: 0.25,
            isBold: true,
            isItalic: true);

        var execute = command.Execute(document);
        var style = Assert.IsType<CadTextStyle>(document.Styles[styleId]);
        Assert.Equal("Meiryo", style.FontFamily);
        Assert.Equal(2.0, style.TextHeight);
        Assert.True(style.IsBold);
        Assert.True(first.RequiresBoundsMeasurement);
        Assert.Contains(execute.EntityChanges, change => change.EntityId == first.Id);
        Assert.Contains(execute.EntityChanges, change => change.EntityId == second.Id);

        command.Undo(document);
        Assert.Equal("Arial", style.FontFamily);
        Assert.Equal(1.0, style.TextHeight);
        Assert.False(style.IsBold);
        Assert.True(execute.AffectsDocumentStructure);
    }

    [Fact]
    public void CreateTextStyle_UndoRemovesStyleAndRedoReusesItsId()
    {
        var document = CadDocument.Create("Test");
        var command = new CreateTextStyleCommand("AI Font", "Arial");

        command.Execute(document);
        var styleId = Assert.IsType<StyleId>(command.CreatedStyleId);
        Assert.True(document.Styles.ContainsKey(styleId));

        command.Undo(document);
        Assert.False(document.Styles.ContainsKey(styleId));

        command.Execute(document);
        Assert.Equal(styleId, command.CreatedStyleId);
        Assert.True(document.Styles.ContainsKey(styleId));
    }

    [Fact]
    public void SetEntityColor_UndoRemovesGeneratedStyleAndRedoRestoresIt()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var command = new SetEntityColorCommand([line.Id], CadColor.Red);

        command.Execute(document);
        var generatedStyleId = line.GraphicStyleId;
        Assert.NotNull(generatedStyleId);
        Assert.True(document.Styles.ContainsKey(generatedStyleId.Value));

        command.Undo(document);
        Assert.Null(line.GraphicStyleId);
        Assert.False(document.Styles.ContainsKey(generatedStyleId.Value));

        command.Execute(document);
        Assert.Equal(generatedStyleId, line.GraphicStyleId);
        Assert.True(document.Styles.ContainsKey(generatedStyleId.Value));
    }

    [Fact]
    public void CreateFillStyle_UndoRemovesStyleAndRedoReusesItsId()
    {
        var document = CadDocument.Create("Test");
        var command = CreateFillStyleCommand.Solid("AI Solid", CadColor.Blue);

        command.Execute(document);
        var styleId = Assert.IsType<StyleId>(command.CreatedStyleId);
        Assert.True(document.Styles.ContainsKey(styleId));

        command.Undo(document);
        Assert.False(document.Styles.ContainsKey(styleId));

        command.Execute(document);
        Assert.Equal(styleId, command.CreatedStyleId);
    }

    [Fact]
    public void SetGraphicStyleProperties_UpdatesSharedReferencesAndUndoRestoresStyle()
    {
        var document = CadDocument.Create("Test");
        var styleId = document.CreateGraphicStyle(
            "Dashed",
            CadColor.Red,
            new CadLineWeight(0.2),
            LineTypeId.Continuous);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), graphicStyleId: styleId);
        var command = new SetGraphicStylePropertiesCommand(
            styleId,
            CadColor.Blue,
            new CadLineWeight(0.6),
            new LineTypeId(7));

        command.Execute(document);
        var style = Assert.IsType<CadGraphicStyle>(document.Styles[styleId]);
        Assert.Equal(CadColor.Blue, style.StrokeColor);
        Assert.Equal(new CadLineWeight(0.6), style.LineWeight);
        Assert.Equal(new LineTypeId(7), style.LineTypeId);
        Assert.Contains(line.Id, document.Entities.Keys);

        command.Undo(document);
        Assert.Equal(CadColor.Red, style.StrokeColor);
        Assert.Equal(new CadLineWeight(0.2), style.LineWeight);
        Assert.Equal(LineTypeId.Continuous, style.LineTypeId);
    }

    [Fact]
    public void LineTypeCommands_KeepCustomPatternAcrossUndoRedoAndProtectReferences()
    {
        var document = CadDocument.Create("Line types");
        var create = new CreateLineTypeCommand("Dashed custom", [4, -2, 1, -2], "AI pattern");

        create.Execute(document);
        var lineTypeId = Assert.IsType<LineTypeId>(create.CreatedLineTypeId);
        Assert.Equal([4, -2, 1, -2], document.GetLineType(lineTypeId).DashPattern);

        var styleId = document.CreateGraphicStyle(
            "Uses custom",
            CadColor.Red,
            CadLineWeight.Default,
            lineTypeId);
        var delete = new DeleteLineTypeCommand(lineTypeId);
        Assert.Throws<InvalidOperationException>(() => delete.Execute(document));

        new DeleteStyleCommand(styleId).Execute(document);
        delete.Execute(document);
        Assert.False(document.LineTypes.ContainsKey(lineTypeId));
        delete.Undo(document);
        Assert.Equal([4, -2, 1, -2], document.GetLineType(lineTypeId).DashPattern);
    }

    [Fact]
    public void DeleteHatchPatternCommand_ProtectsReferencedPattern()
    {
        var document = CadDocument.Create("Hatches");
        var patternId = document.CreateHatchPattern(
            "Brick",
            [new CadHatchLineDefinition(0, CadPointD.Origin, new CadVectorD(10, 0))]);
        var styleId = document.CreateHatchFillStyle("Brick fill", patternId, CadColor.Red);
        var delete = new DeleteHatchPatternCommand(patternId);

        Assert.Throws<InvalidOperationException>(() => delete.Execute(document));

        new DeleteStyleCommand(styleId).Execute(document);
        delete.Execute(document);
        Assert.False(document.HatchPatterns.ContainsKey(patternId));
        delete.Undo(document);
        Assert.True(document.HatchPatterns.ContainsKey(patternId));
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
    public void SetLayerAppearance_PreservesDefaultGraphicStyle()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer("Layer", CadColor.Green, CadLineWeight.Default);
        document.SetLayerDefaultGraphicStyle(layerId, StyleId.DefaultGraphic);
        var command = new SetLayerAppearanceCommand(
            layerId,
            CadColor.Red,
            new CadLineWeight(0.5));

        command.Execute(document);

        var layer = document.GetLayer(layerId);
        Assert.Equal(StyleId.DefaultGraphic, layer.DefaultGraphicStyleId);
        Assert.Equal(CadColor.Red, layer.Color);
        Assert.Equal(new CadLineWeight(0.5), layer.LineWeight);

        command.Undo(document);
        Assert.Equal(StyleId.DefaultGraphic, layer.DefaultGraphicStyleId);
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
