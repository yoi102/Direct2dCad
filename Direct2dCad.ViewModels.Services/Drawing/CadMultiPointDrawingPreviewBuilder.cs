using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal readonly struct CadMultiPointDrawingPreviewBuilder(
    CadDocument document,
    CadViewport viewport,
    CadDrawingStyleResolver styleResolver,
    CadTransientMeasurementBuilder measurementBuilder)
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
        measurementBuilder.AddSegmentMeasurements(
            items,
            previewPoints[^2],
            previewPoints[^1],
            styleResolver.CreatePolylineGuideStyle());
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

        var closed = styleResolver.ResolveSplineClosed(previewPoints.Length);
        items.Add(new CadTransientSpline(
            previewPoints,
            closed,
            styleResolver.CreateSplineTransientStyle(closed)));
        measurementBuilder.AddSegmentMeasurements(
            items,
            previewPoints[^2],
            previewPoints[^1],
            styleResolver.CreateSplineGuideStyle(),
            includeAngle: false);
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

        // A polygon is always previewed as a closed contour. The two edges
        // incident to the cursor are provisional, so they remain dashed until
        // the cursor position is committed as the next vertex.
        var previewPoints = AppendPoint(pendingPoints, mouseWorld);
        if (previewPoints.Length >= 3)
        {
            items.Add(new CadTransientPolyline(
                previewPoints,
                Closed: true,
                styleResolver.CreatePolygonTransientStyle() with
                {
                    StrokeColor = CadColor.Transparent
                }));
        }

        if (pendingPoints.Count >= 2)
        {
            items.Add(new CadTransientPolyline(
                pendingPoints.ToArray(),
                Closed: false,
                styleResolver.CreatePolygonTransientStyle(includeFill: false)));
        }

        items.Add(new CadTransientLine(
            pendingPoints[^1],
            mouseWorld,
            styleResolver.CreatePolygonGuideStyle()));

        measurementBuilder.AddSegmentMeasurements(
            items,
            pendingPoints[^1],
            mouseWorld,
            styleResolver.CreatePolygonGuideStyle(),
            normalSign: 1);

        if (pendingPoints.Count >= 2)
        {
            items.Add(new CadTransientLine(
                mouseWorld,
                pendingPoints[0],
                styleResolver.CreatePolygonGuideStyle()));

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
