using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Drawing;

internal sealed class CadMultiPointDrawingPreviewBuilder(
    CadDocument document,
    CadViewport viewport,
    CadDrawingStyleResolver styleResolver)
{
    public void AddPolylinePreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingPoints,
        CadPointD mouseWorld)
    {
        if (pendingPoints.Count == 0)
            return;

        var previewPoints = AppendPoint(pendingPoints, mouseWorld);
        if (previewPoints.Length < 2)
            return;

        var closed = styleResolver.ResolvePolylineClosed(previewPoints.Length);
        items.Add(new CadTransientPolyline(previewPoints, closed, styleResolver.CreatePolylineTransientStyle(closed)));
    }

    public void AddSplinePreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingPoints,
        CadPointD mouseWorld)
    {
        if (pendingPoints.Count == 0)
            return;

        var previewPoints = AppendPoint(pendingPoints, mouseWorld);
        if (previewPoints.Length < 2)
            return;

        items.Add(new CadTransientSpline(
            previewPoints,
            styleResolver.ResolveSplineClosed(previewPoints.Length),
            styleResolver.CreateSplineTransientStyle()));
    }

    public void AddPolygonPreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingPoints,
        CadPointD mouseWorld)
    {
        if (pendingPoints.Count == 0)
            return;

        if (ShouldClosePolygon(pendingPoints, mouseWorld))
        {
            items.Add(new CadTransientPolyline(
                pendingPoints.ToArray(),
                Closed: true,
                styleResolver.CreatePolygonTransientStyle()));
            return;
        }

        var previewPoints = AppendPoint(pendingPoints, mouseWorld);
        if (previewPoints.Length >= 3)
        {
            items.Add(new CadTransientPolyline(
                previewPoints,
                Closed: true,
                styleResolver.CreatePolygonTransientStyle()));
        }
        else if (previewPoints.Length >= 2)
        {
            items.Add(new CadTransientPolyline(
                previewPoints,
                Closed: false,
                styleResolver.CreatePolygonTransientStyle(includeFill: false)));
        }
    }

    public bool ShouldCompletePolyline(IReadOnlyList<CadPointD> pendingPoints, CadPointD world)
    {
        return pendingPoints.Count >= 2 &&
               pendingPoints[^1].DistanceTo(world) <= ResolveFinishTolerance();
    }

    public bool ShouldCompleteSpline(IReadOnlyList<CadPointD> pendingPoints, CadPointD world)
    {
        return pendingPoints.Count >= 2 &&
               pendingPoints[^1].DistanceTo(world) <= ResolveFinishTolerance();
    }

    public bool ShouldClosePolygon(IReadOnlyList<CadPointD> pendingPoints, CadPointD world)
    {
        return pendingPoints.Count >= 3 &&
               pendingPoints[0].DistanceTo(world) <= ResolveFinishTolerance();
    }

    private double ResolveFinishTolerance()
    {
        var screenTolerance = 8.0 / Math.Max(viewport.Zoom, double.Epsilon);
        var grid = document.ViewSettings.Grid;
        var snapSpacing = Math.Min(grid.GetSnapSpacingX(), grid.GetSnapSpacingY());

        return IsFinitePositive(snapSpacing)
            ? Math.Max(1e-9, Math.Min(screenTolerance, snapSpacing * 0.49))
            : screenTolerance;
    }

    private static CadPointD[] AppendPoint(IReadOnlyList<CadPointD> points, CadPointD point)
    {
        var previewPoints = new CadPointD[points.Count + 1];
        for (var i = 0; i < points.Count; i++)
            previewPoints[i] = points[i];

        previewPoints[^1] = point;
        return previewPoints;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
