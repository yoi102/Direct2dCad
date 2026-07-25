using Direct2dCad.Commands;
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
}
