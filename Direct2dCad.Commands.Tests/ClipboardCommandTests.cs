using Direct2dCad.Commands.Clipboard;
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

    private static BlockId CreateLineBlock(CadDocument document, string name)
    {
        var blockId = document.CreateBlockDefinition(name, CadPointD.Origin);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        document.MoveEntityToBlock(line.Id, blockId);
        return blockId;
    }
}
