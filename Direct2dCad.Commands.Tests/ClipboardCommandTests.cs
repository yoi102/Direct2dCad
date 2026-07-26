using Direct2dCad.Commands.Clipboard;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class ClipboardCommandTests
{
    [Fact]
    public void PasteBlockReference_InSameDocumentReusesDefinition()
    {
        var document = CadDocument.Create("Source");
        var blockId = CreateLineBlock(document, "Valve");
        var sourceReference = document.AddBlockReference(blockId, new CadPointD(10, 20));
        var snapshot = Assert.IsType<CadClipboardSnapshot>(
            CadClipboardSnapshotFactory.Create(document, [sourceReference.Id]));
        var blockCount = document.Blocks.Count;
        var command = new PasteEntitiesCommand(snapshot, new CadVectorD(5, -3));

        command.Execute(document);

        var pasted = Assert.IsType<CadBlockReference>(
            document.GetEntity(Assert.Single(command.CreatedEntityIds)));
        Assert.Equal(blockId, pasted.DefinitionBlockId);
        Assert.Equal(new CadPointD(15, 17), pasted.Position);
        Assert.Equal(blockCount, document.Blocks.Count);
    }

    [Fact]
    public void PasteBlockReference_AcrossDocumentsImportsDefinitionOnce()
    {
        var source = CadDocument.Create("Source");
        var sourceBlockId = CreateLineBlock(source, "Valve");
        var sourceReference = source.AddBlockReference(sourceBlockId, CadPointD.Origin);
        var snapshot = Assert.IsType<CadClipboardSnapshot>(
            CadClipboardSnapshotFactory.Create(source, [sourceReference.Id]));

        var target = CadDocument.Create("Target");
        target.CreateBlockDefinition("Valve", CadPointD.Origin);
        var initialBlockCount = target.Blocks.Count;

        var firstPaste = new PasteEntitiesCommand(snapshot, new CadVectorD(10, 0));
        firstPaste.Execute(target);
        var firstReference = Assert.IsType<CadBlockReference>(
            target.GetEntity(Assert.Single(firstPaste.CreatedEntityIds)));
        Assert.Equal(initialBlockCount + 1, target.Blocks.Count);
        Assert.NotEqual("Valve", target.GetBlock(firstReference.DefinitionBlockId).Name);

        var secondPaste = new PasteEntitiesCommand(snapshot, new CadVectorD(20, 0));
        secondPaste.Execute(target);
        var secondReference = Assert.IsType<CadBlockReference>(
            target.GetEntity(Assert.Single(secondPaste.CreatedEntityIds)));

        Assert.Equal(firstReference.DefinitionBlockId, secondReference.DefinitionBlockId);
        Assert.Equal(initialBlockCount + 1, target.Blocks.Count);
    }

    [Fact]
    public void PasteEntities_TargetLayerOverridesSnapshotLayer()
    {
        var source = CadDocument.Create("Source");
        var sourceLayerId = source.CreateLayer("SourceLayer", CadColor.Green, CadLineWeight.Default);
        var sourceLine = source.AddLine(
            CadPointD.Origin,
            new CadPointD(10, 0),
            sourceLayerId);
        var snapshot = Assert.IsType<CadClipboardSnapshot>(
            CadClipboardSnapshotFactory.Create(source, [sourceLine.Id]));

        var target = CadDocument.Create("Target");
        var targetLayerId = target.CreateLayer("TargetLayer", CadColor.Green, CadLineWeight.Default);
        var command = new PasteEntitiesCommand(
            snapshot,
            CadVectorD.Zero,
            targetLayerId);

        command.Execute(target);

        var pasted = target.GetEntity(Assert.Single(command.CreatedEntityIds));
        Assert.Equal(targetLayerId, pasted.LayerId);
        Assert.DoesNotContain(target.Layers.Values, layer => layer.Name == "SourceLayer");
    }

    [Fact]
    public void PasteBlockReference_UndoAndRedoRestoreSameEntitiesAndDefinition()
    {
        var source = CadDocument.Create("Source");
        var sourceBlockId = CreateLineBlock(source, "Valve");
        var sourceReference = source.AddBlockReference(sourceBlockId, CadPointD.Origin);
        var snapshot = Assert.IsType<CadClipboardSnapshot>(
            CadClipboardSnapshotFactory.Create(source, [sourceReference.Id]));
        var target = CadDocument.Create("Target");
        var command = new PasteEntitiesCommand(snapshot, new CadVectorD(10, 5));

        command.Execute(target);
        var referenceId = Assert.Single(command.CreatedEntityIds);
        var reference = Assert.IsType<CadBlockReference>(target.GetEntity(referenceId));
        var importedBlockId = reference.DefinitionBlockId;
        var importedEntityIds = target.GetBlock(importedBlockId).EntityIds.ToArray();

        command.Undo(target);

        Assert.True(reference.IsErased);
        Assert.False(target.Blocks.ContainsKey(importedBlockId));
        Assert.All(importedEntityIds, id => Assert.False(target.Entities.ContainsKey(id)));

        command.Execute(target);

        Assert.False(reference.IsErased);
        Assert.Equal(referenceId, Assert.Single(command.CreatedEntityIds));
        Assert.True(target.Blocks.ContainsKey(importedBlockId));
        Assert.Equal(importedEntityIds, target.GetBlock(importedBlockId).EntityIds);
    }

    [Fact]
    public void PasteMixedEntities_AcrossDocumentsPreservesIsolatedDataStylesAndRedoChanges()
    {
        var source = CadDocument.Create("Source");
        var sourceLayerId = source.CreateLayer(
            "Assets",
            CadColor.FromRgb(20, 180, 90),
            new CadLineWeight(0.5));
        var fillStyleId = source.CreateSolidFillStyle(
            "Asset fill",
            CadColor.FromRgb(30, 90, 210));
        var sourcePixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var sourceOleBytes = new byte[] { 1, 3, 5, 7, 9 };
        var image = source.AddImage(
            CadRectD.FromXYWH(0, 0, 4, 2),
            2,
            2,
            8,
            sourcePixels,
            sourceLayerId,
            contentType: "image/test",
            sourceName: "source.img",
            name: "Image1",
            opacity: 0.4,
            rotationRadians: 0.25);
        var ole = source.AddOleObject(
            CadRectD.FromXYWH(10, 5, 6, 3),
            sourceOleBytes,
            sourceLayerId,
            contentType: "application/test",
            sourceName: "source.ole",
            name: "Ole1",
            opacity: 0.6);
        var polyline = source.AddPolyline(
            [new CadPointD(20, 0), new CadPointD(25, 0), new CadPointD(23, 4)],
            isClosed: true,
            layerId: sourceLayerId,
            fillStyleId: fillStyleId,
            name: "Polyline1");
        var spline = source.AddSpline(
            [
                new CadPointD(30, 0),
                new CadPointD(35, 5),
                new CadPointD(40, 0),
                new CadPointD(35, -3)
            ],
            closed: true,
            layerId: sourceLayerId,
            fillStyleId: fillStyleId,
            name: "Spline1");
        var snapshot = Assert.IsType<CadClipboardSnapshot>(
            CadClipboardSnapshotFactory.Create(
                source,
                [image.Id, ole.Id, polyline.Id, spline.Id]));

        image.SetImageData(2, 2, 8, Enumerable.Repeat((byte)0xEE, 16).ToArray());
        ole.SetOleData([0xFF], "application/changed", "changed.ole");

        var target = CadDocument.Create("Target");
        var delta = new CadVectorD(100, -50);
        var command = new PasteEntitiesCommand(snapshot, delta);
        var expectedKinds =
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.Fill |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.DrawOrder |
            CadEntityChangeKind.EmbeddedData |
            CadEntityChangeKind.Opacity |
            CadEntityChangeKind.Rotation;

        var execute = command.Execute(target);
        var createdIds = command.CreatedEntityIds.ToArray();
        var pastedEntities = createdIds.Select(target.GetEntity).ToArray();
        var pastedImage = Assert.Single(pastedEntities.OfType<CadImage>());
        var pastedOle = Assert.Single(pastedEntities.OfType<CadOleObject>());
        var pastedPolyline = Assert.Single(pastedEntities.OfType<CadPolyline>());
        var pastedSpline = Assert.Single(pastedEntities.OfType<CadSpline>());
        var importedLayer = Assert.Single(target.Layers.Values, layer => layer.Name == "Assets");

        Assert.Equal(4, execute.EntityChanges.Count);
        Assert.All(execute.EntityChanges, change => Assert.Equal(expectedKinds, change.Kind));
        Assert.False(execute.AffectsDocumentStructure);
        Assert.All(pastedEntities, entity => Assert.Equal(importedLayer.Id, entity.LayerId));
        Assert.Equal(CadRectD.FromXYWH(100, -50, 4, 2), pastedImage.FrameBounds);
        Assert.Equal(sourcePixels, pastedImage.CopyPixels());
        Assert.Equal("image/test", pastedImage.ContentType);
        Assert.Equal("source.img", pastedImage.SourceName);
        Assert.Equal(0.4, pastedImage.Opacity);
        Assert.Equal(0.25, pastedImage.RotationRadians);
        Assert.Equal(CadRectD.FromXYWH(110, -45, 6, 3), pastedOle.Bounds);
        Assert.Equal(sourceOleBytes, pastedOle.CopyOleBytes());
        Assert.Equal("application/test", pastedOle.ContentType);
        Assert.Equal("source.ole", pastedOle.SourceName);
        Assert.Equal(0.6, pastedOle.Opacity);
        Assert.NotNull(pastedPolyline.FillStyleId);
        Assert.Equal(pastedPolyline.FillStyleId, pastedSpline.FillStyleId);

        var undo = command.Undo(target);

        Assert.All(pastedEntities, entity => Assert.True(entity.IsErased));
        Assert.All(
            undo.EntityChanges,
            change => Assert.Equal(
                CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility,
                change.Kind));

        var redo = command.Execute(target);

        Assert.Equal(createdIds, command.CreatedEntityIds);
        Assert.All(pastedEntities, entity => Assert.False(entity.IsErased));
        Assert.Equal(4, redo.EntityChanges.Count);
        Assert.All(redo.EntityChanges, change => Assert.Equal(expectedKinds, change.Kind));
        Assert.Equal(sourcePixels, pastedImage.CopyPixels());
        Assert.Equal(sourceOleBytes, pastedOle.CopyOleBytes());
    }

    private static BlockId CreateLineBlock(CadDocument document, string name)
    {
        var blockId = document.CreateBlockDefinition(name, CadPointD.Origin);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        document.MoveEntityToBlock(line.Id, blockId);
        return blockId;
    }
}
