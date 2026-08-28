using System.Windows.Media;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.wpf.Services.Printing.Vector;

internal static class CadVectorPrintStyleResolver
{
    public static bool TryResolve(
        CadDocument document,
        CadEntity entity,
        CadVectorPrintBlockStyle? blockStyle,
        out CadVectorPrintEntityStyle style)
    {
        var layer = ResolveEffectiveLayer(document, entity.LayerId, blockStyle);
        if (layer is null ||
            entity.IsErased ||
            !entity.IsVisible ||
            !layer.IsVisible ||
            layer.IsFrozen)
        {
            style = default;
            return false;
        }

        var graphic = ResolveGraphicStyle(document, entity, layer);
        var color = ResolveStrokeColor(document, entity, layer, graphic, blockStyle);
        var lineWeight = ResolveLineWeight(entity, layer, graphic);
        var lineType = graphic is not null &&
                       document.LineTypes.TryGetValue(graphic.LineTypeId, out var resolvedLineType)
            ? resolvedLineType
            : null;

        style = new CadVectorPrintEntityStyle(
            layer,
            color,
            lineWeight,
            entity.StrokeStyle,
            lineType,
            ResolveFillStyle(document, entity));
        return true;
    }

    public static bool TryResolveBlockStyle(
        CadDocument document,
        CadBlockReference reference,
        CadVectorPrintBlockStyle? parentStyle,
        out CadVectorPrintBlockStyle style)
    {
        var effectiveLayer = ResolveEffectiveLayer(document, reference.LayerId, parentStyle);
        if (effectiveLayer is null ||
            reference.IsErased ||
            !reference.IsVisible ||
            !effectiveLayer.IsVisible ||
            effectiveLayer.IsFrozen)
        {
            style = default;
            return false;
        }

        var graphic = ResolveGraphicStyle(document, reference, effectiveLayer);
        var layerColor = ResolveLayerColor(document, effectiveLayer);
        var referenceColor = reference.ColorSource switch
        {
            CadColorSource.Explicit => graphic?.StrokeColor ?? layerColor,
            CadColorSource.ByBlock when parentStyle is { } parent => parent.ReferenceColor,
            _ => layerColor
        };
        style = new CadVectorPrintBlockStyle(effectiveLayer, referenceColor);
        return true;
    }

    public static Pen CreatePen(
        CadVectorPrintEntityStyle style,
        double paperScale,
        CadMatrixD ownerToPaper)
    {
        var outputThickness = CadLineWeightDisplay.ToDips(
            Math.Max(style.LineWeight, 0.01));
        var thickness = outputThickness / Math.Max(paperScale, double.Epsilon);
        var pen = new Pen(CreateBrush(style.StrokeColor), thickness)
        {
            StartLineCap = ToLineCap(style.StrokeStyle.StartCap),
            EndLineCap = ToLineCap(style.StrokeStyle.EndCap),
            DashCap = ToLineCap(style.StrokeStyle.DashCap),
            LineJoin = ToLineJoin(style.StrokeStyle.LineJoin),
            MiterLimit = 10.0
        };

        if (style.LineType is { IsContinuous: false } lineType)
        {
            var ownerScale = ResolveMaximumScale(ownerToPaper);
            var dashes = lineType.DashPattern
                .Select(value => Math.Max(
                    Math.Abs(value) * ownerScale * paperScale / outputThickness,
                    0.01))
                .ToArray();
            if (dashes.Length > 0)
                pen.DashStyle = new DashStyle(dashes, 0);
        }
        else
        {
            pen.DashStyle = style.StrokeStyle.DashStyle switch
            {
                CadStrokeDashStyle.Dash => new DashStyle([4.0, 2.0], 0),
                CadStrokeDashStyle.Dot => new DashStyle([0.1, 2.0], 0),
                CadStrokeDashStyle.DashDot => new DashStyle([4.0, 2.0, 0.1, 2.0], 0),
                CadStrokeDashStyle.DashDotDot =>
                    new DashStyle([4.0, 2.0, 0.1, 2.0, 0.1, 2.0], 0),
                _ => DashStyles.Solid
            };
        }

        return pen;
    }

    public static Brush CreateBrush(CadColor color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public static Brush? CreateFillBrush(CadFillStyle? fillStyle)
    {
        if (fillStyle is not CadGradientFillStyle gradient || gradient.Stops.Count == 0)
            return null;

        if (gradient.IsSolid)
            return CreateBrush(gradient.Stops[0].Color);

        GradientBrush brush;
        if (gradient.GradientKind == CadGradientKind.Radial)
        {
            brush = new RadialGradientBrush
            {
                Center = new System.Windows.Point(0.5, 0.5),
                GradientOrigin = new System.Windows.Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
        }
        else
        {
            var radians = gradient.GradientAngle * Math.PI / 180.0;
            var dx = Math.Cos(radians) * 0.5;
            var dy = Math.Sin(radians) * 0.5;
            brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0.5 - dx, 0.5 - dy),
                EndPoint = new System.Windows.Point(0.5 + dx, 0.5 + dy)
            };
        }

        brush.MappingMode = BrushMappingMode.RelativeToBoundingBox;
        foreach (var stop in gradient.Stops)
        {
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(stop.Color.A, stop.Color.R, stop.Color.G, stop.Color.B),
                stop.Offset));
        }
        return brush;
    }

    public static double ResolveMaximumScale(CadMatrixD matrix)
    {
        var scaleX = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        var scaleY = Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
        return Math.Max(Math.Max(scaleX, scaleY), double.Epsilon);
    }

    private static CadLayer? ResolveEffectiveLayer(
        CadDocument document,
        LayerId layerId,
        CadVectorPrintBlockStyle? blockStyle)
    {
        if (layerId.Equals(LayerId.Default) && blockStyle is { } containingBlock)
            return containingBlock.EffectiveLayer;
        return document.TryGetLayer(layerId, out var layer) ? layer : null;
    }

    private static CadGraphicStyle? ResolveGraphicStyle(
        CadDocument document,
        CadEntity entity,
        CadLayer layer)
    {
        var styleId = GetGraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
        return styleId is { } id &&
               document.TryGetStyle(id, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic
            : null;
    }

    private static CadColor ResolveStrokeColor(
        CadDocument document,
        CadEntity entity,
        CadLayer layer,
        CadGraphicStyle? graphic,
        CadVectorPrintBlockStyle? blockStyle)
    {
        if (entity.ColorSource == CadColorSource.ByBlock && blockStyle is { } block)
            return block.ReferenceColor;
        if (entity.ColorSource == CadColorSource.ByLayer)
            return ResolveLayerColor(document, layer);
        return graphic?.StrokeColor ?? ResolveLayerColor(document, layer);
    }

    private static CadColor ResolveLayerColor(CadDocument document, CadLayer layer)
    {
        return layer.DefaultGraphicStyleId is { } styleId &&
               document.TryGetStyle(styleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : layer.Color;
    }

    private static double ResolveLineWeight(
        CadEntity entity,
        CadLayer layer,
        CadGraphicStyle? graphic)
    {
        var weight = entity.UseLayerLineWeight
            ? layer.LineWeight
            : entity.LineWeight is { IsByLayer: false } entityWeight
                ? entityWeight
                : graphic?.LineWeight is { IsByLayer: false } styleWeight
                    ? styleWeight
                    : layer.LineWeight;
        return weight.IsByLayer || weight.Value <= 0
            ? CadLineWeight.Default.Value
            : weight.Value;
    }

    private static CadFillStyle? ResolveFillStyle(CadDocument document, CadEntity entity)
    {
        var styleId = entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            CadSpline { Closed: true } spline => spline.FillStyleId,
            CadCompositePath { Closed: true } path => path.FillStyleId,
            _ => null
        };
        return styleId is { } id &&
               document.TryGetStyle(id, out var style) &&
               style is CadFillStyle fill
            ? fill
            : null;
    }

    private static StyleId? GetGraphicStyleId(CadEntity entity) => entity switch
    {
        CadLine line => line.GraphicStyleId,
        CadCircle circle => circle.GraphicStyleId,
        CadArc arc => arc.GraphicStyleId,
        CadEllipse ellipse => ellipse.GraphicStyleId,
        CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
        CadRectangle rectangle => rectangle.GraphicStyleId,
        CadPolyline polyline => polyline.GraphicStyleId,
        CadSpline spline => spline.GraphicStyleId,
        CadCompositePath path => path.GraphicStyleId,
        CadText text => text.GraphicStyleId,
        CadShapeText shapeText => shapeText.GraphicStyleId,
        CadBlockReference block => block.GraphicStyleId,
        _ => null
    };

    private static PenLineCap ToLineCap(CadStrokeCap cap) => cap switch
    {
        CadStrokeCap.Square => PenLineCap.Square,
        CadStrokeCap.Round => PenLineCap.Round,
        CadStrokeCap.Triangle => PenLineCap.Triangle,
        _ => PenLineCap.Flat
    };

    private static PenLineJoin ToLineJoin(CadStrokeLineJoin join) => join switch
    {
        CadStrokeLineJoin.Bevel => PenLineJoin.Bevel,
        CadStrokeLineJoin.Round => PenLineJoin.Round,
        _ => PenLineJoin.Miter
    };
}

internal readonly record struct CadVectorPrintBlockStyle(
    CadLayer EffectiveLayer,
    CadColor ReferenceColor);

internal readonly record struct CadVectorPrintEntityStyle(
    CadLayer EffectiveLayer,
    CadColor StrokeColor,
    double LineWeight,
    CadStrokeStyle StrokeStyle,
    CadLineTypeDefinition? LineType,
    CadFillStyle? FillStyle);
