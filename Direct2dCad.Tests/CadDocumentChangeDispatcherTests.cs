using Direct2dCad.ChangeTracking;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Tests;

public sealed class CadDocumentChangeDispatcherTests
{
    [Theory]
    [InlineData(CadEntityChangeKind.Appearance)]
    [InlineData(CadEntityChangeKind.Visibility)]
    [InlineData(CadEntityChangeKind.Layer)]
    [InlineData(CadEntityChangeKind.DrawOrder)]
    [InlineData(CadEntityChangeKind.Fill)]
    [InlineData(CadEntityChangeKind.EmbeddedData)]
    [InlineData(CadEntityChangeKind.Opacity)]
    public void VisualBlockChildChange_AlsoInvalidatesItsReferences(
        CadEntityChangeKind changeKind)
    {
        var document = CadDocument.Create("Block invalidation");
        var child = document.AddLine(
            new CadPointD(0, 0),
            new CadPointD(20, 0));
        var definitionId = document.CreateBlockDefinition(
            "Visual changes",
            CadPointD.Origin);
        document.MoveEntityToBlock(child.Id, definitionId);
        var reference = document.AddBlockReference(
            definitionId,
            new CadPointD(500, 300));
        var dispatcher = new CadDocumentChangeDispatcher(
            document,
            new DirtySet());
        CadDocumentChangeSet? published = null;
        dispatcher.DocumentChanged += (_, changes) => published = changes;

        dispatcher.Publish(CadDocumentChangeSet.ForEntity(child.Id, changeKind));

        Assert.NotNull(published);
        var referenceChange = Assert.Single(
            published.EntityChanges,
            change => change.EntityId.Equals(reference.Id));
        Assert.True(referenceChange.Kind.HasFlag(CadEntityChangeKind.Geometry));
    }
}
