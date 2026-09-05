using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadDocumentInvalidationTrackerTests
{
    [Fact]
    public void LayerMetadataNeedsNoSceneRedrawButLayerOrderingDoes()
    {
        var document = CadDocument.Create("Layer invalidation");
        document.AddCircle(new(20, 20), 5);
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);
        var metadata = CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.LayerMetadata);
        var order = CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.LayerOrder);
        Assert.True(tracker.CreateInvalidation(document, metadata, calculator).IsEmpty);
        Assert.True(tracker.CreateInvalidation(document, order, calculator).IsFull);
    }

    [Fact]
    public void ScreenConstantStrokeAfterLargeZoom_DoesNotInflateToTheWholeViewport()
    {
        var document = CadDocument.Create("Screen stroke");
        var line = document.AddLine(CadPointD.Origin, new(0.1, 0));
        var viewport = CreateViewport();
        viewport.SetView(0.001, new(500, 500));
        var width = 80.0;
        var calculator = new CadRenderInvalidationCalculator(document, viewport, 1000, 1000,
            _ => new CadTransientStyle(CadColor.Green, width));
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, calculator);
        viewport.SetView(100, new(500, 500));
        width = 1;
        var dirty = tracker.CreateInvalidation(document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Appearance), calculator);
        Assert.InRange(dirty.DirtyScreenRect.Height, 80, 120);
        Assert.False(dirty.IsFull);
    }

    [Fact]
    public void FullRenderStillUpdatesSnapshotsForTheFollowingMoveAndDelete()
    {
        var document = CadDocument.Create("Full render snapshots");
        var line = document.AddLine(new CadPointD(20, 20), new CadPointD(40, 20));
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);
        line.SetGeometry(new CadPointD(400, 400), new CadPointD(420, 400));
        Assert.True(tracker.CreateInvalidation(document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry).WithViewSettingsChanged(),
            calculator).IsFull);
        line.SetGeometry(new CadPointD(800, 800), new CadPointD(820, 800));
        var move = tracker.CreateInvalidation(document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry), calculator);
        Assert.False(move.IsFull);
        Assert.Equal(2, move.DirtyScreenRects.Count);
        Assert.Contains(move.DirtyScreenRects, rect => rect.X > 350 && rect.X < 450);
        Assert.DoesNotContain(move.DirtyScreenRects, rect => rect.X < 100);
        var delete = new DeleteEntitiesCommand([line.Id]);
        Assert.True(tracker.CreateInvalidation(document,
            delete.Execute(document).WithViewSettingsChanged(), calculator).IsFull);
        Assert.False(tracker.CreateInvalidation(document, delete.Undo(document), calculator).IsFull);
    }

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
    public void MovingBlockReference_InvalidatesCompositePathStrokeExtent()
    {
        var document = CadDocument.Create("Composite path block dirty regions");
        var child = document.AddCompositePath(
            CadPointD.Origin,
            [new CadCompositeLineSegment(new CadPointD(20, 0))]);
        var definitionId = document.CreateBlockDefinition(
            "Wide composite path block",
            CadPointD.Origin);
        document.MoveEntityToBlock(child.Id, definitionId);
        var reference = document.AddBlockReference(
            definitionId,
            new CadPointD(300, 400),
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

        reference.SetPosition(new CadPointD(600, 400));
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

    [Fact]
    public void EmptyChangeSetOnSameDocument_ReturnsEmptyInvalidation()
    {
        var document = CadDocument.Create("Dirty regions");
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);

        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.Empty,
            calculator);

        Assert.True(invalidation.IsEmpty);
    }

    [Fact]
    public void MetadataOnlyChange_DoesNotInvalidatePixels()
    {
        var document = CadDocument.Create("Dirty regions");
        var line = document.AddLine(new CadPointD(20, 20), new CadPointD(60, 20));
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);

        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Metadata),
            calculator);

        Assert.True(invalidation.IsEmpty);
    }

    [Fact]
    public void UntrackedMutationWithoutCreatedFlag_RequiresFullRender()
    {
        var document = CadDocument.Create("Dirty regions");
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);
        var line = document.AddLine(new CadPointD(20, 20), new CadPointD(60, 20));

        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry),
            calculator);

        Assert.True(invalidation.IsFull);
    }

    [Fact]
    public void AlreadyHiddenEntityChange_DoesNotCreateDirtyPixels()
    {
        var document = CadDocument.Create("Dirty regions");
        var line = document.AddLine(new CadPointD(20, 20), new CadPointD(60, 20));
        line.SetVisible(false);
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);

        line.SetGeometry(new CadPointD(700, 700), new CadPointD(740, 700));
        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry),
            calculator);

        Assert.True(invalidation.IsEmpty);
    }

    [Fact]
    public void ViewSettingsChange_RequiresFullRender()
    {
        var document = CadDocument.Create("Dirty regions");
        var tracker = new CadDocumentInvalidationTracker();
        var calculator = CreateCalculator(document, CreateViewport());
        tracker.Reset(document, calculator);

        var invalidation = tracker.CreateInvalidation(
            document,
            CadDocumentChangeSet.Empty.WithViewSettingsChanged(),
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
