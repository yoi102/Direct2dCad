using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal readonly struct CadDrawingStyleResolver(
    CadDocument document,
    CadLayer layer,
    ICadDrawingDefaults defaults,
    CadPreviewStyleService previewStyleService)
{
    public CadTransientStyle CreateLineTransientStyle()
    {
        return CreatePreviewStyle(ResolveLineStrokeColor(), ResolveLineLineWeight(), strokeStyle: defaults.LineStrokeStyle);
    }

    public CadTransientStyle CreateLineGuideStyle()
    {
        return previewStyleService.CreateDrawingAuxiliaryStyle(ResolveLineStrokeColor());
    }

    public StyleId? ResolveLineGraphicStyleId()
    {
        return ResolveGraphicStyleId("Line", defaults.LineStrokeColor, defaults.LineUseLayerColor);
    }

    public CadLineWeight ResolveLineLineWeight()
    {
        return ResolveLineWeight(defaults.LineLineWeight, defaults.LineUseLayerLineWeight);
    }

    public CadTransientStyle CreatePolylineTransientStyle(bool includeFill = false)
    {
        return CreatePreviewStyle(
            ResolvePolylineStrokeColor(),
            ResolvePolylineLineWeight(),
            includeFill ? ResolvePolylineFillStyleId() : null,
            defaults.PolylineStrokeStyle);
    }

    public StyleId? ResolvePolylineGraphicStyleId()
    {
        return ResolveGraphicStyleId("Polyline", defaults.PolylineStrokeColor, defaults.PolylineUseLayerColor);
    }

    public StyleId? ResolvePolylineFillStyleId()
    {
        return ResolveFillStyleId(defaults.PolylineFillStyleId);
    }

    public CadLineWeight ResolvePolylineLineWeight()
    {
        return ResolveLineWeight(defaults.PolylineLineWeight, defaults.PolylineUseLayerLineWeight);
    }

    public bool ResolvePolylineClosed(int pointCount)
    {
        return defaults.PolylineClosed && pointCount >= 3;
    }

    public CadTransientStyle CreatePolygonTransientStyle(bool includeFill = true)
    {
        return CreatePreviewStyle(
            ResolvePolygonStrokeColor(),
            ResolvePolygonLineWeight(),
            includeFill ? ResolvePolygonFillStyleId() : null,
            defaults.PolygonStrokeStyle);
    }

    public CadTransientStyle CreatePolylineGuideStyle()
    {
        return previewStyleService.CreateDrawingAuxiliaryStyle(ResolvePolylineStrokeColor());
    }

    public CadTransientStyle CreatePolygonGuideStyle()
    {
        return previewStyleService.CreateDrawingAuxiliaryStyle(
            ResolvePolygonStrokeColor());
    }

    public StyleId? ResolvePolygonGraphicStyleId()
    {
        return ResolveGraphicStyleId("Polygon", defaults.PolygonStrokeColor, defaults.PolygonUseLayerColor);
    }

    public StyleId? ResolvePolygonFillStyleId()
    {
        return ResolveFillStyleId(defaults.PolygonFillStyleId);
    }

    public CadLineWeight ResolvePolygonLineWeight()
    {
        return ResolveLineWeight(defaults.PolygonLineWeight, defaults.PolygonUseLayerLineWeight);
    }

    public CadTransientStyle CreateSplineTransientStyle(bool includeFill = false)
    {
        return CreatePreviewStyle(
            ResolveSplineStrokeColor(),
            ResolveSplineLineWeight(),
            includeFill ? ResolveSplineFillStyleId() : null,
            defaults.SplineStrokeStyle);
    }

    public CadTransientStyle CreateSplineGuideStyle()
    {
        return previewStyleService.CreateDrawingAuxiliaryStyle(ResolveSplineStrokeColor());
    }

    public StyleId? ResolveSplineGraphicStyleId()
    {
        return ResolveGraphicStyleId("Spline", defaults.SplineStrokeColor, defaults.SplineUseLayerColor);
    }

    public StyleId? ResolveSplineFillStyleId()
    {
        return ResolveFillStyleId(defaults.SplineFillStyleId);
    }

    public CadLineWeight ResolveSplineLineWeight()
    {
        return ResolveLineWeight(defaults.SplineLineWeight, defaults.SplineUseLayerLineWeight);
    }

    public bool ResolveSplineClosed(int fitPointCount)
    {
        return defaults.SplineClosed && fitPointCount >= 3;
    }

    public CadTransientStyle CreateArcTransientStyle()
    {
        return CreatePreviewStyle(ResolveArcStrokeColor(), ResolveArcLineWeight(), strokeStyle: defaults.ArcStrokeStyle);
    }

    public StyleId? ResolveArcGraphicStyleId()
    {
        return ResolveGraphicStyleId("Arc", defaults.ArcStrokeColor, defaults.ArcUseLayerColor);
    }

    public CadLineWeight ResolveArcLineWeight()
    {
        return ResolveLineWeight(defaults.ArcLineWeight, defaults.ArcUseLayerLineWeight);
    }

    public CadTransientStyle CreateCircleTransientStyle()
    {
        return CreatePreviewStyle(
            ResolveCircleStrokeColor(),
            ResolveCircleLineWeight(),
            ResolveCircleFillStyleId(),
            defaults.CircleStrokeStyle);
    }

    public StyleId? ResolveCircleGraphicStyleId()
    {
        return ResolveGraphicStyleId("Circle", defaults.CircleStrokeColor, defaults.CircleUseLayerColor);
    }

    public StyleId? ResolveCircleFillStyleId()
    {
        return ResolveFillStyleId(defaults.CircleFillStyleId);
    }

    public CadLineWeight ResolveCircleLineWeight()
    {
        return ResolveLineWeight(defaults.CircleLineWeight, defaults.CircleUseLayerLineWeight);
    }

    public CadTransientStyle CreateEllipseTransientStyle()
    {
        return CreatePreviewStyle(
            ResolveEllipseStrokeColor(),
            ResolveEllipseLineWeight(),
            ResolveEllipseFillStyleId(),
            defaults.EllipseStrokeStyle);
    }

    public StyleId? ResolveEllipseGraphicStyleId()
    {
        return ResolveGraphicStyleId("Ellipse", defaults.EllipseStrokeColor, defaults.EllipseUseLayerColor);
    }

    public StyleId? ResolveEllipseFillStyleId()
    {
        return ResolveFillStyleId(defaults.EllipseFillStyleId);
    }

    public CadLineWeight ResolveEllipseLineWeight()
    {
        return ResolveLineWeight(defaults.EllipseLineWeight, defaults.EllipseUseLayerLineWeight);
    }

    public CadTransientStyle CreateRectangleTransientStyle()
    {
        return CreatePreviewStyle(
            ResolveRectangleStrokeColor(),
            ResolveRectangleLineWeight(),
            ResolveRectangleFillStyleId(),
            defaults.RectangleStrokeStyle);
    }

    public StyleId? ResolveRectangleGraphicStyleId()
    {
        return ResolveGraphicStyleId("Rectangle", defaults.RectangleStrokeColor, defaults.RectangleUseLayerColor);
    }

    public StyleId? ResolveRectangleFillStyleId()
    {
        return ResolveFillStyleId(defaults.RectangleFillStyleId);
    }

    public CadLineWeight ResolveRectangleLineWeight()
    {
        return ResolveLineWeight(defaults.RectangleLineWeight, defaults.RectangleUseLayerLineWeight);
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
        return CreatePreviewStyle(ResolveTextStrokeColor(), ResolveTextLineWeight());
    }

    public StyleId? ResolveTextGraphicStyleId()
    {
        return ResolveGraphicStyleId("Text", defaults.TextStrokeColor, defaults.TextUseLayerColor);
    }

    public CadLineWeight ResolveTextLineWeight()
    {
        return ResolveLineWeight(defaults.TextLineWeight, defaults.TextUseLayerLineWeight);
    }

    public CadColor ResolveLineStrokeColor() => ResolveStrokeColor(defaults.LineStrokeColor, defaults.LineUseLayerColor);
    public CadColor ResolvePolylineStrokeColor() => ResolveStrokeColor(defaults.PolylineStrokeColor, defaults.PolylineUseLayerColor);
    public CadColor ResolvePolygonStrokeColor() => ResolveStrokeColor(defaults.PolygonStrokeColor, defaults.PolygonUseLayerColor);
    public CadColor ResolveSplineStrokeColor() => ResolveStrokeColor(defaults.SplineStrokeColor, defaults.SplineUseLayerColor);
    public CadColor ResolveCircleStrokeColor() => ResolveStrokeColor(defaults.CircleStrokeColor, defaults.CircleUseLayerColor);
    public CadColor ResolveEllipseStrokeColor() => ResolveStrokeColor(defaults.EllipseStrokeColor, defaults.EllipseUseLayerColor);
    public CadColor ResolveRectangleStrokeColor() => ResolveStrokeColor(defaults.RectangleStrokeColor, defaults.RectangleUseLayerColor);
    public CadColor ResolveTextStrokeColor() => ResolveStrokeColor(defaults.TextStrokeColor, defaults.TextUseLayerColor);
    public CadColor ResolveArcStrokeColor() => ResolveStrokeColor(defaults.ArcStrokeColor, defaults.ArcUseLayerColor);

    public CadColor ResolveDefaultStrokeColor()
    {
        return previewStyleService.ResolveLayerStrokeColor(layer);
    }

    public CadLineWeight ResolveLineWeight(double value, bool useLayerLineWeight)
    {
        if (useLayerLineWeight)
            return CadLineWeight.ByLayer;

        return IsFinitePositive(value)
            ? new CadLineWeight(value)
            : CadLineWeight.Default;
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
        StyleId? fillStyleId = null,
        CadStrokeStyle? strokeStyle = null)
    {
        return previewStyleService.CreateEntityPreviewStyle(
            strokeColor,
            lineWeight,
            layer.LineWeight,
            fillStyleId) with { StrokeStyle = strokeStyle };
    }

    private CadColor ResolveStrokeColor(CadColor strokeColor, bool useLayerColor)
    {
        return useLayerColor ? ResolveDefaultStrokeColor() : strokeColor;
    }

    private StyleId? ResolveGraphicStyleId(string label, CadColor strokeColor, bool useLayerColor)
    {
        if (useLayerColor)
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

}
