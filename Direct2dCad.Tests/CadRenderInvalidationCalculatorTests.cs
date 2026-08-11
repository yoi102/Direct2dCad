using Direct2dCad.Db.Cad;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadRenderInvalidationCalculatorTests
{
    [Fact]
    public void GripAtFractionalScreenPoint_CoversRoundedRightAndBottomEdges()
    {
        var document = CadDocument.Create("Fractional grip dirty region");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1.0, new CadPointD(100.25, 200.75));
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(CadColor.FromRgb(255, 255, 255)));
        var scene = new CadHandleScene();
        scene.Replace(
        [
            new CadGripHandle(
                line.Id,
                CadPointD.Origin,
                CadHandleType.Vertex,
                CadHandleStyle.Grip)
        ]);
        var center = viewport.WorldToScreen(CadPointD.Origin);
        var radius = Math.Max(
            CadHandleStyle.Grip.Size,
            CadHandleStyle.Grip.StrokeWidth) + 4.0;
        var expectedLeft = (int)Math.Floor(center.X - radius);
        var expectedTop = (int)Math.Floor(center.Y - radius);
        var expectedRight = (int)Math.Ceiling(center.X + radius);
        var expectedBottom = (int)Math.Ceiling(center.Y + radius);

        var invalidation = calculator.CreateHandleSceneInvalidation(scene);

        var dirty = Assert.Single(invalidation.DirtyScreenRects);
        Assert.Equal(new CadScreenRect(
            expectedLeft,
            expectedTop,
            expectedRight - expectedLeft,
            expectedBottom - expectedTop), dirty);
    }

    [Fact]
    public void InfiniteCross_InvalidatesTheCurrentViewport()
    {
        var document = CadDocument.Create("Infinite snap marker dirty region");
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(4.0, new CadPointD(160, 120));
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            320,
            240,
            _ => new CadTransientStyle(CadColor.FromRgb(255, 255, 255)));
        var scene = new CadTransientScene();
        scene.Replace(
        [
            new CadTransientInfiniteCross(
                CadPointD.Origin,
                new CadTransientStyle(CadColor.FromRgb(255, 214, 92)))
        ]);

        var invalidation = calculator.CreateTransientSceneInvalidation(scene);

        Assert.Equal(new CadScreenRect(0, 0, 320, 240), Assert.Single(invalidation.DirtyScreenRects));
    }

    [Fact]
    public void HandleScene_CanExcludeGripHandlesFromInvalidation()
    {
        var document = CadDocument.Create("Handle dirty region filtering");
        var line = document.AddLine(new CadPointD(20, 20), new CadPointD(60, 20));
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1.0, new CadPointD(0, 1000));
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(CadColor.FromRgb(255, 255, 255)));
        var scene = new CadHandleScene();
        scene.Replace(
        [
            new CadSelectionEntityReference(
                line.Id,
                line.Bounds,
                CadVectorD.Zero,
                CadHandleStyle.SelectionOutline),
            new CadGripHandle(
                line.Id,
                new CadPointD(700, 700),
                CadHandleType.Center,
                CadHandleStyle.Grip)
        ]);

        var withoutGrips = calculator.CreateHandleSceneInvalidation(
            scene,
            includeGripHandles: false);
        var withGrips = calculator.CreateHandleSceneInvalidation(
            scene,
            includeGripHandles: true);

        Assert.Single(withoutGrips.DirtyScreenRects);
        Assert.Equal(2, withGrips.DirtyScreenRects.Count);
    }

    [Fact]
    public void TryCaptureEntitySnapshot_ReportsMissingAndHiddenEntitiesCorrectly()
    {
        var document = CadDocument.Create("Entity snapshot states");
        var line = document.AddLine(new CadPointD(20, 20), new CadPointD(60, 20));
        line.SetVisible(false);
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1.0, new CadPointD(0, 1000));
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(CadColor.FromRgb(255, 255, 255)));

        Assert.True(calculator.TryCaptureEntitySnapshot(line.Id, out var hidden));
        Assert.False(hidden.IsRenderable);
        Assert.False(calculator.TryCaptureEntitySnapshot(new EntityId(99999), out _));
    }

    [Fact]
    public void CurrentPolylineWithOverflowingProjection_RequiresFullInvalidation()
    {
        var document = CadDocument.Create("Overflowing polyline dirty region");
        var polyline = document.AddPolyline(
        [
            CadPointD.Origin,
            new CadPointD(double.MaxValue, 0)
        ]);
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1_000_000, new CadPointD(0, 1000));
        var calculator = new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(CadColor.FromRgb(255, 255, 255)));
        Assert.True(calculator.TryCaptureEntitySnapshot(polyline.Id, out var snapshot));

        var invalidation = calculator.CreateCurrentEntityInvalidation(polyline.Id, snapshot);

        Assert.True(invalidation.IsFull);
    }
}
