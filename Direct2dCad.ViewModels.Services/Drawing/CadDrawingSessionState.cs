using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal sealed class CadDrawingSessionState
{
    public CadPointD? PendingWorldPoint { get; set; }

    public CadPointD? PendingArcStartPoint { get; set; }

    public CadPointD? PendingCircleSecondPoint { get; set; }

    public List<CadPointD> PendingPolylinePoints { get; } = [];

    public List<CadPointD> PendingPolygonPoints { get; } = [];

    public List<CadPointD> PendingSplinePoints { get; } = [];

    public List<CadPointD> PendingEllipsePoints { get; } = [];

    public bool HasPendingPoints => PendingWorldPoint is not null ||
        PendingArcStartPoint is not null || PendingCircleSecondPoint is not null ||
        PendingPolylinePoints.Count > 0 || PendingPolygonPoints.Count > 0 ||
        PendingSplinePoints.Count > 0 || PendingEllipsePoints.Count > 0;

    public int GetPendingPointCount(CadCanvasToolMode mode) => mode switch
    {
        CadCanvasToolMode.Polyline => PendingPolylinePoints.Count,
        CadCanvasToolMode.Polygon => PendingPolygonPoints.Count,
        CadCanvasToolMode.Spline => PendingSplinePoints.Count,
        CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseAxisEnd or CadCanvasToolMode.EllipseArc => PendingEllipsePoints.Count,
        CadCanvasToolMode.CircleThreePoint => (PendingWorldPoint is null ? 0 : 1) + (PendingCircleSecondPoint is null ? 0 : 1),
        >= CadCanvasToolMode.ArcThreePoint and <= CadCanvasToolMode.ArcCenterStartLength =>
            (PendingWorldPoint is null ? 0 : 1) + (PendingArcStartPoint is null ? 0 : 1),
        CadCanvasToolMode.Line or CadCanvasToolMode.Rectangle or CadCanvasToolMode.CircleCenterRadius or
            CadCanvasToolMode.CircleCenterDiameter or CadCanvasToolMode.CircleTwoPoint => PendingWorldPoint is null ? 0 : 1,
        _ => 0
    };

    public static int GetMinimumCompletionPointCount(CadCanvasToolMode mode) => mode switch
    {
        CadCanvasToolMode.Polyline or CadCanvasToolMode.Spline => 2,
        CadCanvasToolMode.Polygon => 3,
        _ => 0
    };

    public bool CanComplete(CadCanvasToolMode mode)
    {
        var minimum = GetMinimumCompletionPointCount(mode);
        return minimum > 0 && GetPendingPointCount(mode) >= minimum;
    }

    // Only uncommitted input belongs here. Document undo remains a separate operation.
    public bool UndoLastPoint(CadCanvasToolMode mode)
    {
        if (GetPendingPointCount(mode) == 0)
            return false;

        var points = mode switch
        {
            CadCanvasToolMode.Polyline => PendingPolylinePoints,
            CadCanvasToolMode.Polygon => PendingPolygonPoints,
            CadCanvasToolMode.Spline => PendingSplinePoints,
            CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseAxisEnd or CadCanvasToolMode.EllipseArc => PendingEllipsePoints,
            _ => null
        };
        if (points is not null)
            points.RemoveAt(points.Count - 1);
        else if (mode == CadCanvasToolMode.CircleThreePoint && PendingCircleSecondPoint is not null)
            PendingCircleSecondPoint = null;
        else if (mode is >= CadCanvasToolMode.ArcThreePoint and <= CadCanvasToolMode.ArcCenterStartLength && PendingArcStartPoint is not null)
            PendingArcStartPoint = null;
        else
            PendingWorldPoint = null;
        return true;
    }

    public CadDrawingSessionState Clone()
    {
        var copy = new CadDrawingSessionState();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(CadDrawingSessionState source)
    {
        PendingWorldPoint = source.PendingWorldPoint;
        PendingArcStartPoint = source.PendingArcStartPoint;
        PendingCircleSecondPoint = source.PendingCircleSecondPoint;
        PendingPolylinePoints.Clear();
        PendingPolylinePoints.AddRange(source.PendingPolylinePoints);
        PendingPolygonPoints.Clear();
        PendingPolygonPoints.AddRange(source.PendingPolygonPoints);
        PendingSplinePoints.Clear();
        PendingSplinePoints.AddRange(source.PendingSplinePoints);
        PendingEllipsePoints.Clear();
        PendingEllipsePoints.AddRange(source.PendingEllipsePoints);
    }

    public void Clear()
    {
        PendingWorldPoint = null;
        PendingArcStartPoint = null;
        PendingCircleSecondPoint = null;
        PendingPolylinePoints.Clear();
        PendingPolygonPoints.Clear();
        PendingSplinePoints.Clear();
        PendingEllipsePoints.Clear();
    }
}
