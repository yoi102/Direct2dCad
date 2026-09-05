using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Tests;

public sealed class IncrementalBlockTransformTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TransformAndUndoResolveOnlyTheirDependenciesBeforeDispatch(int mode)
    {
        var document = CadDocument.Create("Transforms");
        var inner = document.CreateBlockDefinition("Inner", CadPointD.Origin);
        var outer = document.CreateBlockDefinition("Outer", CadPointD.Origin);
        var other = document.CreateBlockDefinition("Other", CadPointD.Origin);
        var child = document.AddLine(new(2, 3), new(9, 8));
        var otherChild = document.AddLine(new(100, 100), new(110, 110));
        document.MoveEntityToBlock(child.Id, inner);
        document.MoveEntityToBlock(otherChild.Id, other);
        var nested = document.AddBlockReference(inner, CadPointD.Origin);
        document.MoveEntityToBlock(nested.Id, outer);
        var top = document.AddBlockReference(outer, CadPointD.Origin);
        var unrelated = document.AddBlockReference(other, CadPointD.Origin);
        document.RefreshBlockReferenceBounds();
        var original = child.Bounds;
        var unrelatedBounds = unrelated.Bounds;
        // Deliberately leave an unrelated definition stale to detect a global traversal.
        otherChild.SetGeometry(new(200, 200), new(210, 210));
        ICadCommand command = mode switch
        {
            0 => new MoveEntitiesCommand([child.Id], new(12, 7)),
            1 => new RotateEntitiesCommand([child.Id], CadPointD.Origin, Math.PI / 2),
            2 => new ScaleEntitiesCommand([child.Id], CadPointD.Origin, 2),
            _ => new MirrorEntitiesCommand([child.Id], CadPointD.Origin, 0)
        };
        var dispatcher = new CadDocumentChangeDispatcher(document, new DirtySet());
        CadDocumentChangeSet? published = null;
        dispatcher.DocumentChanged += (_, result) => published = result;
        var result = command.Execute(document);
        Assert.True(result.HasResolvedBlockReferenceChanges);
        Assert.Equal(child.Bounds, nested.Bounds);
        Assert.Equal(child.Bounds, top.Bounds);
        Assert.Equal(unrelatedBounds, unrelated.Bounds);
        Assert.True(new HashSet<EntityId> { child.Id, nested.Id, top.Id }.SetEquals(
            result.EntityChanges.Select(change => change.EntityId)));
        dispatcher.Publish(result);
        Assert.Same(result, published);
        var undo = command.Undo(document);
        dispatcher.Publish(undo);
        Assert.Same(undo, published);
        Assert.Equal(original.Left, top.Bounds.Left, 8);
        Assert.Equal(original.Right, top.Bounds.Right, 8);
        Assert.Equal(original.Top, top.Bounds.Top, 8);
        Assert.Equal(original.Bottom, top.Bounds.Bottom, 8);
        Assert.Equal(unrelatedBounds, unrelated.Bounds);
    }

    [Fact]
    public void MixedChangeSetsMustNotSkipUnresolvedDependencies()
    {
        var resolved = new CadDocumentChangeSet([new(new EntityId(1), CadEntityChangeKind.Geometry)])
        {
            HasResolvedBlockReferenceChanges = true
        };
        var unresolved = CadDocumentChangeSet.ForEntity(new EntityId(2), CadEntityChangeKind.Geometry);
        Assert.True(CadDocumentChangeSet.Combine([resolved, CadDocumentChangeSet.Empty]).HasResolvedBlockReferenceChanges);
        Assert.False(CadDocumentChangeSet.Combine([resolved, unresolved]).HasResolvedBlockReferenceChanges);
        Assert.False(resolved.WithDocumentStructureChanged().HasResolvedBlockReferenceChanges);
    }
}
