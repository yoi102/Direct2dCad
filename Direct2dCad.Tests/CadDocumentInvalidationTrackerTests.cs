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
