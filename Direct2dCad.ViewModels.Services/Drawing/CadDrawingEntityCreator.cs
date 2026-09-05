using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Geometry;
using Direct2dCad.ViewModels.Services.Text;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal sealed class CadDrawingEntityCreator(
    CadEditor editor,
    LayerId layerId,
    ICadDrawingDefaults defaults,
    CadDrawingStyleResolver styleResolver,
    CadTextMeasurementService textMeasurementService,
    Action entityCreated)
{
    public void AddLine(CadPointD start, CadPointD end)
    {
        editor.AddLine(
            start,
            end,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveLineGraphicStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveLineLineWeight(),
            zIndex: defaults.LineZIndex,
            isVisible: defaults.LineIsVisible,
            strokeStyle: defaults.LineStrokeStyle);
        entityCreated();
    }

    public void AddRectangleIfValid(CadRectD bounds)
    {
        if (!IsValidRectangleBounds(bounds))
            return;

        editor.AddRectangle(
            bounds,
            layerId: layerId,
            cornerRadiusX: styleResolver.ResolveRectangleCornerRadiusX(bounds),
            cornerRadiusY: styleResolver.ResolveRectangleCornerRadiusY(bounds),
            graphicStyleId: styleResolver.ResolveRectangleGraphicStyleId(),
            fillStyleId: styleResolver.ResolveRectangleFillStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveRectangleLineWeight(),
            zIndex: defaults.RectangleZIndex,
            isVisible: defaults.RectangleIsVisible,
            strokeStyle: defaults.RectangleStrokeStyle);
        entityCreated();
    }

    public void AddCircleIfValid(CadPointD center, double radius)
    {
        if (!IsValidCircleGeometry(radius))
            return;

        editor.AddCircle(
            center,
            radius,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveCircleGraphicStyleId(),
            fillStyleId: styleResolver.ResolveCircleFillStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveCircleLineWeight(),
            zIndex: defaults.CircleZIndex,
            isVisible: defaults.CircleIsVisible,
            strokeStyle: defaults.CircleStrokeStyle);
        entityCreated();
    }

    public void AddArcIfValid(ArcDrawingGeometry geometry)
    {
        if (!IsValidArcGeometry(geometry.Radius, geometry.SweepAngleRadians))
            return;

        editor.AddArc(
            geometry.Center,
            geometry.Radius,
            geometry.StartAngleRadians,
            geometry.SweepAngleRadians,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveArcGraphicStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveArcLineWeight(),
            zIndex: defaults.ArcZIndex,
            isVisible: defaults.ArcIsVisible,
            strokeStyle: defaults.ArcStrokeStyle);
        entityCreated();
    }

    public void AddEllipseIfValid(CadPointD center, double radiusX, double radiusY)
    {
        if (!IsValidEllipseGeometry(radiusX, radiusY))
            return;

        editor.AddEllipse(
            center,
            radiusX,
            radiusY,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveEllipseGraphicStyleId(),
            fillStyleId: styleResolver.ResolveEllipseFillStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveEllipseLineWeight(),
            zIndex: defaults.EllipseZIndex,
            isVisible: defaults.EllipseIsVisible,
            strokeStyle: defaults.EllipseStrokeStyle);
        entityCreated();
    }

    public void AddEllipseArcIfValid(EllipseArcDrawingGeometry geometry)
    {
        if (!IsValidEllipseGeometry(geometry.RadiusX, geometry.RadiusY) ||
            !IsValidArcGeometry(1.0, geometry.SweepAngleRadians))
        {
            return;
        }

        editor.AddEllipseArc(
            geometry.Center,
            geometry.RadiusX,
            geometry.RadiusY,
            geometry.StartAngleRadians,
            geometry.SweepAngleRadians,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveEllipseGraphicStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveEllipseLineWeight(),
            zIndex: defaults.EllipseZIndex,
            isVisible: defaults.EllipseIsVisible,
            strokeStyle: defaults.EllipseStrokeStyle);
        entityCreated();
    }

    public void AddPolyline(IReadOnlyList<CadPointD> points)
    {
        if (points.Count < 2)
            return;

        var closed = styleResolver.ResolvePolylineClosed(points.Count);
        editor.AddPolyline(
            points,
            closed,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolvePolylineGraphicStyleId(),
            fillStyleId: closed ? styleResolver.ResolvePolylineFillStyleId() : null,
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolvePolylineLineWeight(),
            zIndex: defaults.PolylineZIndex,
            isVisible: defaults.PolylineIsVisible,
            strokeStyle: defaults.PolylineStrokeStyle);
        entityCreated();
    }

    public void AddSpline(IReadOnlyList<CadPointD> fitPoints)
    {
        if (fitPoints.Count < 2)
            return;

        var closed = styleResolver.ResolveSplineClosed(fitPoints.Count);
        editor.AddSpline(
            fitPoints,
            closed,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveSplineGraphicStyleId(),
            fillStyleId: closed ? styleResolver.ResolveSplineFillStyleId() : null,
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolveSplineLineWeight(),
            zIndex: defaults.SplineZIndex,
            isVisible: defaults.SplineIsVisible,
            strokeStyle: defaults.SplineStrokeStyle);
        entityCreated();
    }

    public void AddPolygon(IReadOnlyList<CadPointD> points)
    {
        if (points.Count < 3)
            return;

        editor.AddPolygon(
            points,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolvePolygonGraphicStyleId(),
            fillStyleId: styleResolver.ResolvePolygonFillStyleId(),
            name: defaults.EntityName,
            lineWeight: styleResolver.ResolvePolygonLineWeight(),
            zIndex: defaults.PolygonZIndex,
            isVisible: defaults.PolygonIsVisible,
            strokeStyle: defaults.PolygonStrokeStyle);
        entityCreated();
    }

    public void AddText(
        CadPointD position,
        string text,
        StyleId? textStyleId,
        double invertedMarginFactor,
        double rotationRadians)
    {
        editor.AddText(
            text,
            position,
            textMeasurementService.ResolveTextBoxHeight(text, textStyleId),
            rotationRadians,
            layerId: layerId,
            graphicStyleId: styleResolver.ResolveTextGraphicStyleId(),
            textStyleId: textStyleId,
            name: defaults.EntityName,
            isInverted: defaults.TextInverted,
            invertedMarginFactor: invertedMarginFactor,
            lineWeight: styleResolver.ResolveTextLineWeight(),
            zIndex: defaults.TextZIndex,
            isVisible: defaults.TextIsVisible);
        entityCreated();
    }

    public void SetOriginPosition(CadPointD position)
    {
        editor.SetOriginPosition(position);
    }

    public static bool IsValidRectangleBounds(CadRectD bounds)
    {
        return !bounds.IsEmpty &&
               bounds.Width > 0 &&
               bounds.Height > 0 &&
               !double.IsNaN(bounds.Width) &&
               !double.IsNaN(bounds.Height) &&
               !double.IsInfinity(bounds.Width) &&
               !double.IsInfinity(bounds.Height);
    }
}
