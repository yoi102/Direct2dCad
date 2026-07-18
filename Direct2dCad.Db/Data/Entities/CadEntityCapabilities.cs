namespace Direct2dCad.Db.Data.Entities;

public static class CadEntityCapabilities
{
    public static bool SupportsGraphicStyle(CadEntity entity) => entity is
        CadLine or CadCircle or CadEllipse or CadEllipseArc or CadRectangle or CadArc or
        CadPolyline or CadSpline or CadText or CadShapeText or CadBlockReference;

    public static bool SupportsStrokeStyle(CadEntity entity) => entity is
        CadLine or CadCircle or CadEllipse or CadEllipseArc or CadRectangle or CadArc or
        CadPolyline or CadSpline;

    public static bool SupportsStartEndCaps(CadEntity entity) => entity switch
    {
        CadLine => true,
        CadArc arc => !arc.IsFullCircle,
        CadEllipseArc => true,
        CadPolyline polyline => !polyline.Closed,
        CadSpline spline => !spline.Closed,
        _ => false
    };

    public static bool SupportsLineJoin(CadEntity entity) =>
        entity is CadRectangle or CadPolyline or CadSpline;

    public static bool SupportsFill(CadEntity entity) => entity switch
    {
        CadCircle or CadEllipse or CadRectangle => true,
        CadPolyline polyline => polyline.Closed,
        CadSpline spline => spline.Closed,
        _ => false
    };

    public static bool SupportsOpacity(CadEntity entity) =>
        entity is CadImage or CadOleObject;
}
