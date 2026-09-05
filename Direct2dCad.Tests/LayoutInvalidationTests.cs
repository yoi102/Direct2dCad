using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Rendering;

namespace Direct2dCad.Tests;

public sealed class LayoutInvalidationTests
{
    [Fact]
    public void SwitchingScreenAndPaperLineWeights_RecapturesSnapshotsBeforePartialUpdates()
    {
        var (document, viewport, layout) = CreateScene();
        document.AddLayoutViewport(layout.Id, CadRectD.FromXYWH(50, 50, 200, 200), CadPointD.Origin, 1);
        var line = document.AddLine(CadPointD.Origin, new(10, 0));
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, new(document, viewport, 1000, 1000, _ => CadTransientStyle.Construction));
        var change = CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Appearance);
        Assert.True(tracker.CreateInvalidation(document, change, Calculator(document, viewport, layout)).IsFull);
        Assert.False(tracker.CreateInvalidation(document, change, Calculator(document, viewport, layout)).IsFull);
    }

    [Theory]
    [InlineData(0.0, 0.25)]
    [InlineData(0.7, 1.0)]
    [InlineData(1.57, 3.0)]
    public void ModelEdit_InvalidatesBothLocationsInEveryVisibleViewport(double rotation, double scale)
    {
        var (document, viewport, layout) = CreateScene();
        var first = layout.GetViewport(document.AddLayoutViewport(layout.Id,
            CadRectD.FromXYWH(50, 50, 200, 200), CadPointD.Origin, scale, rotation));
        var second = layout.GetViewport(document.AddLayoutViewport(layout.Id,
            CadRectD.FromXYWH(650, 650, 200, 200), CadPointD.Origin, scale, -rotation));
        var line = document.AddLine(new(-10, -10), new(10, -10));
        var calculator = Calculator(document, viewport, layout);
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, calculator);

        line.SetGeometry(new(-10, 15), new(10, 15));
        var dirty = tracker.CreateInvalidation(document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry), calculator);

        Assert.False(dirty.IsFull);
        foreach (var view in new[] { first, second })
            foreach (var point in new CadPointD[] { new(-10, -10), new(10, -10), new(-10, 15), new(10, 15) })
                AssertCovered(dirty, CadLayoutViewportMapper.ModelToScreen(viewport, view, point));
        Assert.True(dirty.DirtyScreenRects.Sum(rect => (long)rect.Width * rect.Height) < 200_000);

        var delete = new DeleteEntitiesCommand([line.Id]);
        var deleted = tracker.CreateInvalidation(document, delete.Execute(document), calculator);
        var restored = tracker.CreateInvalidation(document, delete.Undo(document), calculator);
        Assert.False(deleted.IsFull);
        Assert.Equal(deleted.DirtyScreenRects, restored.DirtyScreenRects);
    }

    [Fact]
    public void HiddenOffscreenAndDefinitionEntities_DoNotDirtyTheLayout()
    {
        var (document, viewport, layout) = CreateScene();
        var view = layout.GetViewport(document.AddLayoutViewport(layout.Id,
            CadRectD.FromXYWH(100, 100, 200, 200), CadPointD.Origin, 1));
        var line = document.AddLine(CadPointD.Origin, new(10, 0));
        var calculator = Calculator(document, viewport, layout);
        Assert.True(calculator.TryCaptureEntitySnapshot(line.Id, out var snapshot));
        view.SetVisible(false);
        Assert.True(calculator.CreateEntitySnapshotInvalidation(snapshot).IsEmpty);
        view.SetVisible(true);
        line.SetGeometry(new(2000, 0), new(2100, 0));
        calculator.TryCaptureEntitySnapshot(line.Id, out snapshot);
        Assert.True(calculator.CreateEntitySnapshotInvalidation(snapshot).IsEmpty);
        document.MoveEntityToBlock(line.Id, document.CreateBlockDefinition("Unused", CadPointD.Origin));
        calculator.TryCaptureEntitySnapshot(line.Id, out snapshot);
        Assert.True(calculator.CreateEntitySnapshotInvalidation(snapshot).IsEmpty);
    }

    [Fact]
    public void PaperEntity_DirtiesPaperNotModelViewports()
    {
        var (document, viewport, layout) = CreateScene();
        document.AddLayoutViewport(layout.Id, CadRectD.FromXYWH(600, 600, 200, 200), CadPointD.Origin, 3);
        var line = document.AddLine(new(20, 30), new(40, 30));
        document.MoveEntityToBlock(line.Id, layout.PaperSpaceBlockId);
        var calculator = Calculator(document, viewport, layout);
        calculator.TryCaptureEntitySnapshot(line.Id, out var snapshot);
        var dirty = calculator.CreateEntitySnapshotInvalidation(snapshot);
        AssertCovered(dirty, viewport.WorldToScreen(line.Start));
        Assert.Single(dirty.DirtyScreenRects);
        Assert.True(dirty.DirtyScreenRect.X < 100);
    }

    [Fact]
    public void StrokeReductionAfterZoomIn_ClearsThePreviousWorldStroke()
    {
        var (document, viewport, layout) = CreateScene();
        var view = layout.GetViewport(document.AddLayoutViewport(layout.Id,
            CadRectD.FromXYWH(0, 0, 200, 200), CadPointD.Origin, 0.25));
        var line = document.AddLine(new(-10, 0), new(10, 0));
        var width = 40.0;
        CadRenderInvalidationCalculator Create() => new(document, viewport, 1000, 1000,
            _ => new CadTransientStyle(CadColor.Green, width, KeepStrokeWidthScreenConstant: false), layout.Id);
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, Create());
        viewport.SetView(3, new(0, 1000));
        width = 1;
        var dirty = tracker.CreateInvalidation(document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Appearance), Create());
        var center = CadLayoutViewportMapper.ModelToScreen(viewport, view, CadPointD.Origin);
        AssertCovered(dirty, new(center.X, center.Y - 59));
        AssertCovered(dirty, new(center.X, center.Y + 59));
    }

    private static void AssertCovered(CadRenderInvalidation dirty, CadPointD point) =>
        Assert.Contains(dirty.DirtyScreenRects, rect =>
            point.X >= rect.X && point.X <= rect.X + rect.Width &&
            point.Y >= rect.Y && point.Y <= rect.Y + rect.Height);

    private static CadRenderInvalidationCalculator Calculator(CadDocument document, CadViewport viewport, CadLayout layout) =>
        new(document, viewport, 1000, 1000, _ => new CadTransientStyle(CadColor.Green, 1), layout.Id);

    private static (CadDocument, CadViewport, CadLayout) CreateScene()
    {
        var document = CadDocument.Create("Layout dirty regions");
        var layout = document.GetLayout(LayoutId.Default);
        foreach (var view in layout.Viewports.ToArray())
            document.RemoveLayoutViewport(layout.Id, view.Id);
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1, new(0, 1000));
        return (document, viewport, layout);
    }
}
