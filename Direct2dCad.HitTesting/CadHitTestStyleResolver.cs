using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.HitTesting;

internal static class CadHitTestStyleResolver
{
    public static double ResolveStrokeHitPadding(
        CadDocument document,
        CadEntity entity,
        CadHitTestOptions options)
    {
        if (!HasStrokeEdge(entity))
            return 0.0;

        return ResolveWorldStrokeWidth(document, entity, options) * 0.5;
    }

    public static CadRectD InflateByStroke(
        CadDocument document,
        CadEntity entity,
        CadHitTestOptions options)
    {
        var padding = ResolveStrokeHitPadding(document, entity, options);
        return padding <= 0 ? entity.Bounds : entity.Bounds.Inflate(padding);
    }

    private static bool HasStrokeEdge(CadEntity entity)
    {
        return entity is CadLine or
            CadCircle or
            CadEllipse or
            CadRectangle or
            CadArc or
            CadPolyline or
            CadSpline or
            CadShapeText;
    }

    private static double ResolveWorldStrokeWidth(
        CadDocument document,
        CadEntity entity,
        CadHitTestOptions options)
    {
        var layer = document.GetLayer(entity.LayerId);
        var graphic = ResolveGraphicStyle(document, entity, layer);
        var modelStrokeWidth = ResolveModelStrokeWidth(
            entity.LineWeight,
            entity.UseLayerLineWeight,
            graphic?.LineWeight,
            layer.LineWeight);

        return options.ResolveWorldStrokeWidth(modelStrokeWidth);
    }

    private static CadGraphicStyle? ResolveGraphicStyle(
        CadDocument document,
        CadEntity entity,
        CadLayer layer)
    {
        var styleId = ResolveGraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic
            : null;
    }

    private static StyleId? ResolveGraphicStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static double ResolveModelStrokeWidth(
        CadLineWeight? entityWeight,
        bool useLayerLineWeight,
        CadLineWeight? styleWeight,
        CadLineWeight layerWeight)
    {
        var weight = useLayerLineWeight
            ? layerWeight
            : entityWeight switch
            {
                { IsByLayer: false } explicitWeight => explicitWeight,
                { IsByLayer: true } => layerWeight,
                _ => styleWeight is { IsByLayer: false }
                    ? styleWeight.Value
                    : layerWeight
            };

        if (weight.IsByLayer || weight.Value <= 0)
            weight = CadLineWeight.Default;

        return Math.Max(weight.Value, 0.01);
    }
}
