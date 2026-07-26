using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class CompositePathCommandTests
{
    [Fact]
    public void AddAndTransformCompositePath_AreUndoable()
    {
        var document = CadDocument.Create("Composite");
        var add = new AddCompositePathCommand(
            new CadPointD(0, 0),
            [
                new CadCompositeLineSegment(new CadPointD(10, 0)),
                new CadCompositeArcSegment(new CadPointD(10, 5), Math.PI / 2),
                new CadCompositeSplineSegment([new CadPointD(18, 8), new CadPointD(20, 0)])
            ],
            closed: true);

        add.Execute(document);
        var id = Assert.IsType<EntityId>(add.CreatedEntityId);
        var path = Assert.IsType<CadCompositePath>(document.GetEntity(id));
        var originalBounds = path.Bounds;

        var move = new MoveEntitiesCommand([id], new CadVectorD(25, -4));
        move.Execute(document);
        Assert.True(path.Bounds.NearEquals(originalBounds.Translate(new CadVectorD(25, -4))));

        move.Undo(document);
        Assert.True(path.Bounds.NearEquals(originalBounds));

        add.Undo(document);
        Assert.True(path.IsErased);
        add.Execute(document);
        Assert.False(path.IsErased);
        Assert.Equal(id, add.CreatedEntityId);
    }

    [Fact]
    public void ClipboardSnapshot_PastesCompositePathAcrossDocuments()
    {
        var source = CadDocument.Create("Source");
        var sourcePath = source.AddCompositePath(
            CadPointD.Origin,
            [
                new CadCompositeLineSegment(new CadPointD(10, 0)),
                new CadCompositeArcSegment(new CadPointD(10, 5), Math.PI)
            ],
            closed: true);
        var snapshot = Assert.IsType<CadClipboardSnapshot>(CadClipboardSnapshotFactory.Create(source, [sourcePath.Id]));
        var destination = CadDocument.Create("Destination");
        var delta = new CadVectorD(100, 50);
        var command = new PasteEntitiesCommand(snapshot, delta);

        command.Execute(destination);

        var pasted = Assert.IsType<CadCompositePath>(destination.GetEntity(Assert.Single(command.CreatedEntityIds)));
        Assert.Equal(sourcePath.StartPoint + delta, pasted.StartPoint);
        Assert.Equal(sourcePath.Segments.Count, pasted.Segments.Count);
        Assert.True(pasted.Closed);
    }
}
