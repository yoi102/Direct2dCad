using Direct2dCad.Db.Geometry;

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
