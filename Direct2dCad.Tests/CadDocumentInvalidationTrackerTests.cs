using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadDocumentInvalidationTrackerTests
{
    [Fact]
    public void GeometryChange_InvalidatesOldAndNewLocationsSeparately()
    {
        var document = CadDocument.Create("Dirty regions");
        var line = document.AddLine(
            new CadPointD(20, 20),
            new CadPointD(60, 20));
        var viewport = CreateViewport();
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, viewport);
        tracker.Reset(document, calculator);

        line.SetGeometry(
            new CadPointD(700, 700),
            new CadPointD(740, 700));
        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(
                line.Id,
                CadEntityChangeKind.Geometry),
            calculator);

        Assert.False(invalidation.IsFull);
        Assert.Equal(2, invalidation.DirtyScreenRects.Count);
        Assert.Contains(
            invalidation.DirtyScreenRects,
            rect => rect.X < 100 && rect.Y > 900);
        Assert.Contains(
            invalidation.DirtyScreenRects,
            rect => rect.X > 650 && rect.Y < 350);
    }

    [Fact]
    public void DeleteAndUndo_InvalidateTheVisibleLocation()
    {
        var document = CadDocument.Create("Dirty regions");
        var line = document.AddLine(
            new CadPointD(100, 100),
            new CadPointD(200, 100));
        var viewport = CreateViewport();
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, viewport);
        tracker.Reset(document, calculator);
        var command = new DeleteEntitiesCommand([line.Id]);

        var deleteInvalidation = tracker.CreateInvalidation(
            document,
            command.Execute(document),
            calculator);
        var undoInvalidation = tracker.CreateInvalidation(
            document,
            command.Undo(document),
            calculator);

        Assert.False(deleteInvalidation.IsFull);
        Assert.Single(deleteInvalidation.DirtyScreenRects);
        Assert.False(undoInvalidation.IsFull);
        Assert.Single(undoInvalidation.DirtyScreenRects);
        Assert.Equal(
            deleteInvalidation.DirtyScreenRect,
            undoInvalidation.DirtyScreenRect);
    }

    [Fact]
    public void AppearanceChange_KeepsPreviousWideStrokeExtentDirty()
    {
        var document = CadDocument.Create("Dirty regions");
        var line = document.AddLine(
            new CadPointD(100, 100),
            new CadPointD(200, 100));
        var viewport = CreateViewport();
        var strokeWidth = 80.0;
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(
                CadColor.FromRgb(255, 255, 255),
                strokeWidth));
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, calculator);

        strokeWidth = 1.0;
        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(
                line.Id,
                CadEntityChangeKind.Appearance),
            calculator);

        Assert.False(invalidation.IsFull);
        Assert.True(invalidation.DirtyScreenRect.Height >= 80);
    }

    [Fact]
    public void MovingBlockReference_InvalidatesScaledDefinitionStrokeExtent()
    {
        var document = CadDocument.Create("Block dirty regions");
        var child = document.AddLine(
            new CadPointD(0, 0),
            new CadPointD(20, 0));
        var definitionId = document.CreateBlockDefinition(
            "Wide block",
            CadPointD.Origin);
        document.MoveEntityToBlock(child.Id, definitionId);
        var reference = document.AddBlockReference(
            definitionId,
            new CadPointD(400, 400),
            scaleX: 10,
            scaleY: 10);
        var viewport = CreateViewport();
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            entity => new CadTransientStyle(
                CadColor.FromRgb(255, 255, 255),
                entity.Id.Equals(child.Id) ? 40.0 : 1.0));
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, calculator);

        reference.SetPosition(new CadPointD(500, 400));
        document.RefreshBlockReferenceBounds();
        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(
                reference.Id,
                CadEntityChangeKind.Geometry),
            calculator);

        Assert.False(invalidation.IsFull);
        Assert.True(invalidation.DirtyScreenRect.Height >= 400);
    }

    [Fact]
    public void MovingRotatedImage_InvalidatesOldAndNewFrameBounds()
    {
        var document = CadDocument.Create("Image dirty regions");
        var image = document.AddImage(
            CadRectD.FromXYWH(100, 100, 80, 40),
            1,
            1,
            4,
            [0x20, 0x80, 0xE0, 0xFF],
            rotationRadians: Math.PI / 4);
        var viewport = CreateViewport();
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, viewport);
        tracker.Reset(document, calculator);

        image.SetBounds(CadRectD.FromXYWH(700, 700, 120, 60));
        image.SetRotation(Math.PI / 6);
        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(
                image.Id,
                CadEntityChangeKind.Geometry | CadEntityChangeKind.Rotation),
            calculator);

        Assert.False(invalidation.IsFull);
        Assert.Equal(2, invalidation.DirtyScreenRects.Count);
        Assert.Contains(
            invalidation.DirtyScreenRects,
            rect => rect.X < 250 && rect.Y > 800);
        Assert.Contains(
            invalidation.DirtyScreenRects,
            rect => rect.X > 600 && rect.Y < 400);
    }

    [Fact]
    public void MovingOleObject_InvalidatesOldAndNewBounds()
    {
        var document = CadDocument.Create("OLE dirty regions");
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(100, 100, 80, 40),
            [1, 2, 3, 4]);
        var viewport = CreateViewport();
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, viewport);
        tracker.Reset(document, calculator);

        ole.SetBounds(CadRectD.FromXYWH(700, 700, 120, 60));
        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(
                ole.Id,
                CadEntityChangeKind.Geometry),
            calculator);

        Assert.False(invalidation.IsFull);
        Assert.Equal(2, invalidation.DirtyScreenRects.Count);
        Assert.Contains(
            invalidation.DirtyScreenRects,
            rect => rect.X < 250 && rect.Y > 800);
        Assert.Contains(
            invalidation.DirtyScreenRects,
            rect => rect.X > 600 && rect.Y < 400);
    }

    [Fact]
    public void LayoutStructureChange_RequiresFullRender()
    {
        var document = CadDocument.Create("Dirty regions");
        var viewport = CreateViewport();
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, viewport);
        tracker.Reset(document, calculator);

        var invalidation = tracker.CreateInvalidation(
            document,
            new CadDocumentChangeSet([])
            {
                AffectsLayoutStructure = true
            },
            calculator);

        Assert.True(invalidation.IsFull);
    }

    private static CadViewport CreateViewport()
    {
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1.0, new CadPointD(0, 1000));
        return viewport;
    }

    private static CadRenderInvalidationCalculator CreateCalculator(
        CadDocument document,
        CadViewport viewport)
    {
        return new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(
                CadColor.FromRgb(255, 255, 255),
                1.0));
    }
}
