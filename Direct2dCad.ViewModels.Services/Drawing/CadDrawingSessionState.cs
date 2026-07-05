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
