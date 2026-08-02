using Direct2dCad.Commands;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class LayerAndBlockCommandTests
{
    [Fact]
    public void DeleteLayerCommand_UndoRestoresLayerEntitiesAndPriority()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer("Annotations", CadColor.Green, CadLineWeight.Default);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(layerId, 37);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), layerId);
        var command = new DeleteLayerCommand(layerId);

        command.Execute(document);

        Assert.False(document.Layers.ContainsKey(layerId));
        Assert.True(line.IsErased);
        Assert.False(document.DocumentSettings.LayerDrawingPriority.Priorities.ContainsKey(layerId));

        command.Undo(document);

        Assert.Equal("Annotations", document.GetLayer(layerId).Name);
        Assert.False(line.IsErased);
        Assert.Equal(37, document.DocumentSettings.LayerDrawingPriority.GetPriority(layerId));
    }

    [Fact]
    public void CreateBlockCommand_UndoAndRedoReuseDefinitionAndReferenceIds()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 5));
        var command = new CreateBlockCommand(
            [line.Id],
            "Valve",
            new CadPointD(2, 1),
            BlockId.ModelSpace,
            LayerId.Default);

        command.Execute(document);
        var blockId = Assert.IsType<BlockId>(command.CreatedBlockId);
        var referenceId = Assert.IsType<EntityId>(command.CreatedReferenceId);

        Assert.Equal(blockId, line.OwnerBlockId);
        Assert.IsType<CadBlockReference>(document.GetEntity(referenceId));

        command.Undo(document);

        Assert.Equal(BlockId.ModelSpace, line.OwnerBlockId);
        Assert.False(document.Blocks.ContainsKey(blockId));
        Assert.True(document.GetEntity(referenceId).IsErased);

        command.Execute(document);

        Assert.Equal(blockId, command.CreatedBlockId);
        Assert.Equal(referenceId, command.CreatedReferenceId);
        Assert.True(document.Blocks.ContainsKey(blockId));
        Assert.False(document.GetEntity(referenceId).IsErased);
        Assert.Equal(blockId, line.OwnerBlockId);
    }

    [Fact]
    public void SetEntityColorSourceCommand_UndoRestoresByLayerState()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var command = new SetEntityColorSourceCommand([line.Id], CadColorSource.Explicit);

        command.Execute(document);

        Assert.Equal(CadColorSource.Explicit, line.ColorSource);
        Assert.NotNull(line.GraphicStyleId);

        command.Undo(document);

        Assert.Equal(CadColorSource.ByLayer, line.ColorSource);
        Assert.Null(line.GraphicStyleId);
    }

    [Fact]
    public void CreateLayerCommandWithoutPriorityPlacesLayerBelowExistingLayersAndRedoKeepsIt()
    {
        var document = CadDocument.Create("Test");
        var command = new CreateLayerCommand(
            "Annotations",
            CadColor.Green,
            CadLineWeight.Default);

        command.Execute(document);

        var layerId = Assert.IsType<LayerId>(command.LayerId);
        Assert.Equal(-1, document.DocumentSettings.LayerDrawingPriority.GetPriority(layerId));

        command.Undo(document);
        command.Execute(document);

        Assert.Equal(-1, document.DocumentSettings.LayerDrawingPriority.GetPriority(layerId));
    }

    [Fact]
    public void SetLayerDrawingPrioritiesCommandPreservesDefaultPriorityWhenOmitted()
    {
        var document = CadDocument.Create("Test");
        document.DocumentSettings.LayerDrawingPriority.SetDefaultPriority(42);
        var command = new SetLayerDrawingPrioritiesCommand(
            new Dictionary<LayerId, int> { [LayerId.Default] = 10 });

        command.Execute(document);

        Assert.Equal(42, document.DocumentSettings.LayerDrawingPriority.DefaultPriority);
        command.Undo(document);
        Assert.Equal(42, document.DocumentSettings.LayerDrawingPriority.DefaultPriority);
    }

    [Fact]
    public void SetBlockReferenceTransformCommand_ExecuteUndoAndRedoRestoreTransformAndBounds()
    {
        var document = CadDocument.Create("Block transform");
        var blockId = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        var child = document.AddLine(CadPointD.Origin, new CadPointD(10, 5));
        document.MoveEntityToBlock(child.Id, blockId);
        var reference = document.AddBlockReference(blockId, new CadPointD(20, 30));
        var originalBounds = reference.Bounds;
        var command = new SetBlockReferenceTransformCommand(
            reference.Id,
            new CadPointD(100, 80),
            rotationRadians: Math.PI / 4,
            scaleX: -2,
            scaleY: 3);
        var expectedKinds = CadEntityChangeKind.Geometry | CadEntityChangeKind.Rotation;

        var execute = command.Execute(document);

        Assert.Equal(expectedKinds, Assert.Single(execute.EntityChanges).Kind);
        Assert.Equal(new CadPointD(100, 80), reference.Position);
        Assert.Equal(Math.PI / 4, reference.RotationRadians);
        Assert.Equal(-2, reference.ScaleX);
        Assert.Equal(3, reference.ScaleY);
        Assert.NotEqual(originalBounds, reference.Bounds);

        command.Undo(document);

        Assert.Equal(new CadPointD(20, 30), reference.Position);
        Assert.Equal(0, reference.RotationRadians);
        Assert.Equal(1, reference.ScaleX);
        Assert.Equal(1, reference.ScaleY);
        Assert.True(originalBounds.NearEquals(reference.Bounds));

        command.Execute(document);
        Assert.Equal(new CadPointD(100, 80), reference.Position);
        Assert.Equal(Math.PI / 4, reference.RotationRadians);
        Assert.Equal(-2, reference.ScaleX);
        Assert.Equal(3, reference.ScaleY);
    }

    [Fact]
    public void RenameBlockCommand_UndoAndRedoRestoreDefinitionName()
    {
        var document = CadDocument.Create("Block names");
        var blockId = document.CreateBlockDefinition("Original", CadPointD.Origin);
        var command = new RenameBlockCommand(blockId, "Renamed");

        command.Execute(document);
        Assert.Equal("Renamed", document.GetBlock(blockId).Name);

        command.Undo(document);
        Assert.Equal("Original", document.GetBlock(blockId).Name);

        command.Execute(document);
        Assert.Equal("Renamed", document.GetBlock(blockId).Name);
    }

    [Fact]
    public void DeleteBlockDefinitionCommand_RejectsReferencedBlockAndRestoresUnreferencedBlock()
    {
        var document = CadDocument.Create("Block deletion");
        var blockId = document.CreateBlockDefinition("Symbol", CadPointD.Origin);
        var child = document.AddLine(CadPointD.Origin, new CadPointD(5, 0));
        document.MoveEntityToBlock(child.Id, blockId);
        var reference = document.AddBlockReference(blockId, new CadPointD(10, 10));
        var command = new DeleteBlockDefinitionCommand(blockId);

        Assert.Throws<InvalidOperationException>(() => command.Execute(document));
        reference.Erase();

        command.Execute(document);
        Assert.False(document.Blocks.ContainsKey(blockId));
        Assert.False(document.TryGetEntity(child.Id, out _));

        command.Undo(document);
        Assert.True(document.Blocks.ContainsKey(blockId));
        Assert.True(document.TryGetEntity(child.Id, out var restored));
        Assert.False(restored!.IsErased);
        Assert.Equal(blockId, restored.OwnerBlockId);
    }
}
