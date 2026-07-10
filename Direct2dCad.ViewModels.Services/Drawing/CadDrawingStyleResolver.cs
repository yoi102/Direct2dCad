using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal sealed class CadDrawingStyleResolver(
    CadDocument document,
    CadLayer layer,
    CadDrawingDefaults defaults,
    CadPreviewStyleService previewStyleService)
{
    public CadTransientStyle CreateLineTransientStyle()
    {
        return CreatePreviewStyle(defaults.LineStrokeColor, ResolveLineLineWeight());
    }

    public StyleId? ResolveLineGraphicStyleId()
    {
        return ResolveGraphicStyleId("Line", defaults.LineStrokeColor);
    }

    public CadLineWeight ResolveLineLineWeight()
    {
        return ResolveLineWeight(defaults.LineLineWeight);
    }

    public CadTransientStyle CreatePolylineTransientStyle(bool includeFill = false)
    {
        return CreatePreviewStyle(
            defaults.PolylineStrokeColor,
            ResolvePolylineLineWeight(),
            includeFill ? ResolvePolylineFillStyleId() : null);
    }

    public StyleId? ResolvePolylineGraphicStyleId()
    {
        return ResolveGraphicStyleId("Polyline", defaults.PolylineStrokeColor);
    }

    public StyleId? ResolvePolylineFillStyleId()
    {
        return ResolveFillStyleId(defaults.PolylineFillStyleId);
    }

    public CadLineWeight ResolvePolylineLineWeight()
    {
        return ResolveLineWeight(defaults.PolylineLineWeight);
    }

    public bool ResolvePolylineClosed(int pointCount)
    {
        return defaults.PolylineClosed && pointCount >= 3;
    }

    public CadTransientStyle CreatePolygonTransientStyle(bool includeFill = true)
    {
        return CreatePreviewStyle(
            defaults.PolygonStrokeColor,
            ResolvePolygonLineWeight(),
            includeFill ? ResolvePolygonFillStyleId() : null);
    }

    public StyleId? ResolvePolygonGraphicStyleId()
    {
        return ResolveGraphicStyleId("Polygon", defaults.PolygonStrokeColor);
    }

    public StyleId? ResolvePolygonFillStyleId()
    {
        return ResolveFillStyleId(defaults.PolygonFillStyleId);
    }

    public CadLineWeight ResolvePolygonLineWeight()
    {
        return ResolveLineWeight(defaults.PolygonLineWeight);
    }

    public CadTransientStyle CreateSplineTransientStyle(bool includeFill = false)
    {
        return CreatePreviewStyle(
            defaults.SplineStrokeColor,
            ResolveSplineLineWeight(),
            includeFill ? ResolveSplineFillStyleId() : null);
    }

    public StyleId? ResolveSplineGraphicStyleId()
    {
        return ResolveGraphicStyleId("Spline", defaults.SplineStrokeColor);
    }

    public StyleId? ResolveSplineFillStyleId()
    {
        return ResolveFillStyleId(defaults.SplineFillStyleId);
    }

    public CadLineWeight ResolveSplineLineWeight()
    {
        return ResolveLineWeight(defaults.SplineLineWeight);
    }

    public bool ResolveSplineClosed(int fitPointCount)
    {
        return defaults.SplineClosed && fitPointCount >= 3;
    }

    public CadTransientStyle CreateArcTransientStyle()
    {
        return CreatePreviewStyle(defaults.ArcStrokeColor, ResolveArcLineWeight());
    }

    public StyleId? ResolveArcGraphicStyleId()
    {
        return ResolveGraphicStyleId("Arc", defaults.ArcStrokeColor);
    }

    public CadLineWeight ResolveArcLineWeight()
    {
        return ResolveLineWeight(defaults.ArcLineWeight);
    }

    public CadTransientStyle CreateCircleTransientStyle()
    {
        return CreatePreviewStyle(
            defaults.CircleStrokeColor,
            ResolveCircleLineWeight(),
            ResolveCircleFillStyleId());
    }

    public StyleId? ResolveCircleGraphicStyleId()
    {
        return ResolveGraphicStyleId("Circle", defaults.CircleStrokeColor);
    }

    public StyleId? ResolveCircleFillStyleId()
    {
        return ResolveFillStyleId(defaults.CircleFillStyleId);
    }

    public CadLineWeight ResolveCircleLineWeight()
    {
        return ResolveLineWeight(defaults.CircleLineWeight);
    }

    public CadTransientStyle CreateEllipseTransientStyle()
    {
        return CreatePreviewStyle(
            defaults.EllipseStrokeColor,
            ResolveEllipseLineWeight(),
            ResolveEllipseFillStyleId());
    }

    public StyleId? ResolveEllipseGraphicStyleId()
    {
        return ResolveGraphicStyleId("Ellipse", defaults.EllipseStrokeColor);
    }

    public StyleId? ResolveEllipseFillStyleId()
    {
        return ResolveFillStyleId(defaults.EllipseFillStyleId);
    }

    public CadLineWeight ResolveEllipseLineWeight()
    {
        return ResolveLineWeight(defaults.EllipseLineWeight);
    }

    public CadTransientStyle CreateRectangleTransientStyle()
    {
        return CreatePreviewStyle(
            defaults.RectangleStrokeColor,
            ResolveRectangleLineWeight(),
            ResolveRectangleFillStyleId());
    }

    public StyleId? ResolveRectangleGraphicStyleId()
    {
        return ResolveGraphicStyleId("Rectangle", defaults.RectangleStrokeColor);
    }

    public StyleId? ResolveRectangleFillStyleId()
    {
        return ResolveFillStyleId(defaults.RectangleFillStyleId);
    }

    public CadLineWeight ResolveRectangleLineWeight()
    {
        return ResolveLineWeight(defaults.RectangleLineWeight);
    }

    public double ResolveRectangleCornerRadiusX(CadRectD bounds)
    {
        return ResolveRectangleCornerRadius(defaults.RectangleCornerRadiusX, bounds.Width);
    }

    public double ResolveRectangleCornerRadiusY(CadRectD bounds)
    {
        return ResolveRectangleCornerRadius(defaults.RectangleCornerRadiusY, bounds.Height);
    }

    public CadTransientStyle CreateTextTransientStyle()
    {
        return CreatePreviewStyle(defaults.TextStrokeColor, ResolveTextLineWeight());
    }

    public StyleId? ResolveTextGraphicStyleId()
    {
        return ResolveGraphicStyleId("Text", defaults.TextStrokeColor);
    }

    public CadLineWeight ResolveTextLineWeight()
    {
        return ResolveLineWeight(defaults.TextLineWeight);
    }

    public CadColor ResolveDefaultStrokeColor()
    {
        return previewStyleService.ResolveLayerStrokeColor(layer);
    }

    public CadLineWeight ResolveLineWeight(double value)
    {
        if (!IsFinitePositive(value))
            return CadLineWeight.ByLayer;

        return AreClose(value, ResolveLineWeightDisplayValue(layer.LineWeight))
            ? CadLineWeight.ByLayer
            : new CadLineWeight(value);
    }

    public static double ResolveLineWeightDisplayValue(CadLineWeight lineWeight)
    {
        return lineWeight.IsByLayer || lineWeight.Value <= 0
            ? CadLineWeight.Default.Value
            : lineWeight.Value;
    }

    private CadTransientStyle CreatePreviewStyle(
        CadColor strokeColor,
        CadLineWeight lineWeight,
        StyleId? fillStyleId = null)
    {
        return previewStyleService.CreateEntityPreviewStyle(
            strokeColor,
            lineWeight,
            layer.LineWeight,
            fillStyleId);
    }

    private StyleId? ResolveGraphicStyleId(string label, CadColor strokeColor)
    {
        if (strokeColor == ResolveDefaultStrokeColor())
            return null;

        return document.CreateGraphicStyle(
            $"{label} stroke {strokeColor.A:X2}{strokeColor.R:X2}{strokeColor.G:X2}{strokeColor.B:X2}",
            strokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private StyleId? ResolveFillStyleId(StyleId? fillStyleId)
    {
        return fillStyleId is { } styleId &&
               document.TryGetStyle(styleId, out var style) &&
               style is CadFillStyle
            ? styleId
            : null;
    }

    private static double ResolveRectangleCornerRadius(double radius, double size)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? 0
            : Math.Min(radius, size * 0.5);
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }
}
