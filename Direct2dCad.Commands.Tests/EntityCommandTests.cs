using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class EntityCommandTests
{
    [Fact]
    public void AddLineCommand_UndoAndRedoReuseEntityId()
    {
        var document = CadDocument.Create("Test");
        var command = new AddLineCommand(
            new CadPointD(1, 2),
            new CadPointD(8, 13),
            name: "Line1");

        command.Execute(document);
        var entityId = Assert.IsType<EntityId>(command.CreatedEntityId);
        var line = Assert.IsType<CadLine>(document.GetEntity(entityId));
        Assert.False(line.IsErased);

        command.Undo(document);
        Assert.True(line.IsErased);

        command.Execute(document);
        Assert.Equal(entityId, command.CreatedEntityId);
        Assert.Same(line, document.GetEntity(entityId));
        Assert.False(line.IsErased);
    }

    [Fact]
    public void MoveEntitiesCommand_MovesAllEntitiesAndUndoRestoresGeometry()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 0));
        var second = document.AddCircle(new CadPointD(5, 5), 3);
        var command = new MoveEntitiesCommand([first.Id, second.Id], new CadVectorD(7, -4));

        command.Execute(document);

        Assert.Equal(new CadPointD(7, -4), first.Start);
        Assert.Equal(new CadPointD(12, 1), second.Center);

        command.Undo(document);

        Assert.Equal(new CadPointD(0, 0), first.Start);
        Assert.Equal(new CadPointD(10, 0), first.End);
        Assert.Equal(new CadPointD(5, 5), second.Center);
    }

    [Fact]
    public void MoveEntitiesCommand_OnLockedLayerLeavesGeometryUnchanged()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer("Locked", CadColor.Green, CadLineWeight.Default);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), layerId);
        document.GetLayer(layerId).SetLocked(true);
        var command = new MoveEntitiesCommand([line.Id], new CadVectorD(3, 4));

        Assert.Throws<InvalidOperationException>(() => command.Execute(document));
        Assert.Equal(CadPointD.Origin, line.Start);
        Assert.Equal(new CadPointD(10, 0), line.End);
    }

    [Fact]
    public void DeleteEntitiesCommand_UndoRestoresEntities()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddLine(CadPointD.Origin, new CadPointD(1, 0));
        var second = document.AddCircle(new CadPointD(5, 5), 2);
        var command = new DeleteEntitiesCommand([first.Id, second.Id]);

        command.Execute(document);
        Assert.True(first.IsErased);
        Assert.True(second.IsErased);

        command.Undo(document);
        Assert.False(first.IsErased);
        Assert.False(second.IsErased);
    }
}
