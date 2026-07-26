using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class EmbeddedEntityCommandTests
{
    [Fact]
    public void ImageGeometryCommands_ExecuteUndoAndRedoPreserveBoundsAndRotation()
    {
        var document = CadDocument.Create("Image commands");
        var originalFrame = CadRectD.FromXYWH(1, 2, 10, 4);
        var updatedFrame = CadRectD.FromXYWH(20, 30, 18, 8);
        var image = document.AddImage(
            originalFrame,
            1,
            1,
            4,
            [0x20, 0x80, 0xE0, 0xFF],
            rotationRadians: 0.1);
        var boundsCommand = new SetImageBoundsCommand(image.Id, updatedFrame);

        AssertSingleChange(
            boundsCommand.Execute(document),
            image.Id,
            CadEntityChangeKind.Geometry);
        Assert.Equal(updatedFrame, image.FrameBounds);

        AssertSingleChange(
            boundsCommand.Undo(document),
            image.Id,
            CadEntityChangeKind.Geometry);
        Assert.Equal(originalFrame, image.FrameBounds);

        boundsCommand.Execute(document);
        Assert.Equal(updatedFrame, image.FrameBounds);

        var beforeRotationBounds = image.Bounds;
        var rotationCommand = new SetImageRotationCommand(image.Id, Math.PI / 3);

        AssertSingleChange(
            rotationCommand.Execute(document),
            image.Id,
            CadEntityChangeKind.Rotation);
        Assert.Equal(Math.PI / 3, image.RotationRadians);
        Assert.NotEqual(beforeRotationBounds, image.Bounds);

        AssertSingleChange(
            rotationCommand.Undo(document),
            image.Id,
            CadEntityChangeKind.Rotation);
        Assert.Equal(0.1, image.RotationRadians);

        rotationCommand.Execute(document);
        Assert.Equal(Math.PI / 3, image.RotationRadians);
    }

    [Fact]
    public void OleCommands_ExecuteUndoAndRedoRestoreBoundsAndClonedStorage()
    {
        var document = CadDocument.Create("OLE commands");
        var originalBounds = CadRectD.FromXYWH(1, 2, 10, 4);
        var updatedBounds = CadRectD.FromXYWH(20, 30, 18, 8);
        var ole = document.AddOleObject(
            originalBounds,
            [1, 2, 3, 4],
            contentType: "application/original",
            sourceName: "original.ole");
        var boundsCommand = new SetOleObjectBoundsCommand(ole.Id, updatedBounds);

        AssertSingleChange(
            boundsCommand.Execute(document),
            ole.Id,
            CadEntityChangeKind.Geometry);
        Assert.Equal(updatedBounds, ole.Bounds);

        boundsCommand.Undo(document);
        Assert.Equal(originalBounds, ole.Bounds);
        boundsCommand.Execute(document);
        Assert.Equal(updatedBounds, ole.Bounds);

        var replacement = new byte[] { 9, 8, 7, 6 };
        var dataCommand = new SetOleObjectDataCommand(
            ole.Id,
            replacement,
            "application/updated",
            "updated.ole");
        replacement[0] = 0;
        var expectedKinds = CadEntityChangeKind.Appearance | CadEntityChangeKind.EmbeddedData;

        AssertSingleChange(dataCommand.Execute(document), ole.Id, expectedKinds);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, ole.CopyOleBytes());
        Assert.Equal("application/updated", ole.ContentType);
        Assert.Equal("updated.ole", ole.SourceName);

        AssertSingleChange(dataCommand.Undo(document), ole.Id, expectedKinds);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ole.CopyOleBytes());
        Assert.Equal("application/original", ole.ContentType);
        Assert.Equal("original.ole", ole.SourceName);

        dataCommand.Execute(document);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, ole.CopyOleBytes());
        Assert.Equal("application/updated", ole.ContentType);
    }

    private static void AssertSingleChange(
        CadDocumentChangeSet changes,
        EntityId entityId,
        CadEntityChangeKind expectedKind)
    {
        var change = Assert.Single(changes.EntityChanges);
        Assert.Equal(entityId, change.EntityId);
        Assert.Equal(expectedKind, change.Kind);
    }
}
