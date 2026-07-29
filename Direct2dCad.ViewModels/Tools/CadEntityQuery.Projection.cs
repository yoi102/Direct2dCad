using System.Globalization;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tools;

internal static partial class CadEntityQuery
{
    private static object FilterDto(CadEntityQueryOptions options) => new
    {
        type = options.Type,
        types = options.Types,
        layer = options.Layer,
        layers = options.Layers,
        entity_ids = options.EntityIds,
        owner = options.Owner,
        name = options.Name,
        name_contains = options.NameContains,
        text_contains = options.TextContains,
        source_name_contains = options.SourceNameContains,
        selected_only = options.SelectedOnly,
        visible = options.IsVisible,
        locked = options.IsLocked,
        closed = options.IsClosed,
        has_fill = options.HasFill,
        fill_kind = options.FillKind,
        color_source = options.ColorSource,
        line_weight_source = options.LineWeightSource,
        graphic_style = options.GraphicStyle,
        fill_style = options.FillStyle,
        dash_style = options.DashStyle,
        min_z_index = options.MinZIndex,
        max_z_index = options.MaxZIndex,
        min_length = options.MinLength,
        max_length = options.MaxLength,
        min_width = options.MinWidth,
        max_width = options.MaxWidth,
        min_height = options.MinHeight,
        max_height = options.MaxHeight,
        min_radius = options.MinRadius,
        max_radius = options.MaxRadius,
        min_point_count = options.MinPointCount,
        max_point_count = options.MaxPointCount,
        min_opacity = options.MinOpacity,
        max_opacity = options.MaxOpacity,
        bounds = options.Bounds is { } bounds ? RectDto(bounds) : null,
        spatial_relation = options.SpatialRelation
    };

    private static object EntityDto(CadDocument document, CadEntity entity) => new
    {
        id = entity.Id.Value,
        type = EntityType(entity),
        entity.Name,
        layer = LayerName(document, entity.LayerId),
        owner_block_id = entity.OwnerBlockId.Value,
        owner = OwnerName(document, entity.OwnerBlockId),
        bounds = RectDto(entity.Bounds),
        entity.IsVisible,
        entity.IsLocked,
        color_source = ProtocolEnum(entity.ColorSource),
        line_weight = entity.UseLayerLineWeight ? (object)"by_layer" : entity.LineWeight?.Value,
        entity.ZIndex,
        graphic_style_id = GraphicStyleId(entity)?.Value,
        graphic_style = StyleName(document, GraphicStyleId(entity)),
        fill_style_id = FillStyleId(entity)?.Value,
        fill_style = StyleName(document, FillStyleId(entity)),
        fill_kind = ResolveFillKind(document, entity),
        stroke_style = new
        {
            start_cap = ProtocolEnum(entity.StrokeStyle.StartCap),
            end_cap = ProtocolEnum(entity.StrokeStyle.EndCap),
            dash_cap = ProtocolEnum(entity.StrokeStyle.DashCap),
            dash_style = ProtocolEnum(entity.StrokeStyle.DashStyle),
            line_join = ProtocolEnum(entity.StrokeStyle.LineJoin)
        },
        characteristics = CharacteristicDto(document, entity)
    };

    private static object CharacteristicDto(CadDocument document, CadEntity entity)
    {
        var result = new Dictionary<string, object?>
        {
            ["bounds_width"] = entity.Bounds.Width,
            ["bounds_height"] = entity.Bounds.Height,
            ["bounds_area"] = entity.Bounds.Width * entity.Bounds.Height
        };

        if (entity is Curve curve)
        {
            result["length"] = curve.Length;
            result["closed"] = curve.IsClosed;
            result["orientation"] = ProtocolEnum(curve.Orientation);
        }

        switch (entity)
        {
            case CadLine line:
                result["start"] = PointDto(line.Start);
                result["end"] = PointDto(line.End);
                break;
            case CadCircle circle:
                result["center"] = PointDto(circle.Center);
                result["radius"] = circle.Radius;
                break;
            case CadArc arc:
                result["center"] = PointDto(arc.Center);
                result["radius"] = arc.Radius;
                result["start_angle_degrees"] = arc.StartAngleDegrees;
                result["sweep_angle_degrees"] = arc.SweepAngleDegrees;
                break;
            case CadEllipse ellipse:
                result["center"] = PointDto(ellipse.Center);
                result["radius_x"] = ellipse.RadiusX;
                result["radius_y"] = ellipse.RadiusY;
                break;
            case CadEllipseArc ellipseArc:
                result["center"] = PointDto(ellipseArc.Center);
                result["radius_x"] = ellipseArc.RadiusX;
                result["radius_y"] = ellipseArc.RadiusY;
                result["start_angle_degrees"] = ellipseArc.StartAngleDegrees;
                result["sweep_angle_degrees"] = ellipseArc.SweepAngleDegrees;
                break;
            case CadRectangle rectangle:
                result["corner_radius_x"] = rectangle.CornerRadiusX;
                result["corner_radius_y"] = rectangle.CornerRadiusY;
                break;
            case CadPolyline polyline:
                result["point_count"] = polyline.Points.Count;
                break;
            case CadSpline spline:
                result["point_count"] = spline.FitPoints.Count;
                break;
            case CadCompositePath path:
                result["point_count"] = path.Segments.Count;
                result["segment_count"] = path.Segments.Count;
                break;
            case CadText text:
                result["text"] = text.Text;
                result["height"] = text.Height;
                result["rotation_degrees"] = CadArc.RadiansToDegrees(text.RotationRadians);
                result["inverted"] = text.IsInverted;
                break;
            case CadShapeText text:
                result["text"] = text.Text;
                result["height"] = text.Height;
                result["rotation_degrees"] = CadArc.RadiansToDegrees(text.RotationRadians);
                result["inverted"] = text.IsInverted;
                break;
            case CadImage image:
                result["pixel_width"] = image.PixelWidth;
                result["pixel_height"] = image.PixelHeight;
                result["opacity"] = image.Opacity;
                result["rotation_degrees"] = CadArc.RadiansToDegrees(image.RotationRadians);
                result["source_name"] = image.SourceName;
                result["content_type"] = image.ContentType;
                break;
            case CadOleObject ole:
                result["opacity"] = ole.Opacity;
                result["source_name"] = ole.SourceName;
                result["content_type"] = ole.ContentType;
                break;
            case CadBlockReference block:
                result["definition_block_id"] = block.DefinitionBlockId.Value;
                result["definition_name"] = OwnerName(document, block.DefinitionBlockId);
                result["position"] = PointDto(block.Position);
                result["rotation_degrees"] = CadArc.RadiansToDegrees(block.RotationRadians);
                result["scale_x"] = block.ScaleX;
                result["scale_y"] = block.ScaleY;
                break;
        }

        return result;
    }

    internal static string EntityType(CadEntity entity) => entity switch
    {
        CadLine => "Line",
        CadCircle => "Circle",
        CadArc => "Arc",
        CadEllipse => "Ellipse",
        CadEllipseArc => "EllipseArc",
        CadRectangle => "Rectangle",
        CadPolyline => "Polyline",
        CadSpline => "Spline",
        CadCompositePath => "CompositePath",
        CadText => "Text",
        CadShapeText => "ShapeText",
        CadImage => "Image",
        CadOleObject => "OleObject",
        CadBlockReference => "BlockReference",
        _ => entity.GetType().Name
    };

    private static StyleId? GraphicStyleId(CadEntity entity) => entity switch
    {
        CadLine value => value.GraphicStyleId,
        CadCircle value => value.GraphicStyleId,
        CadArc value => value.GraphicStyleId,
        CadEllipse value => value.GraphicStyleId,
        CadEllipseArc value => value.GraphicStyleId,
        CadRectangle value => value.GraphicStyleId,
        CadPolyline value => value.GraphicStyleId,
        CadSpline value => value.GraphicStyleId,
        CadCompositePath value => value.GraphicStyleId,
        CadText value => value.GraphicStyleId,
        CadShapeText value => value.GraphicStyleId,
        CadBlockReference value => value.GraphicStyleId,
        _ => null
    };

    private static StyleId? FillStyleId(CadEntity entity) => entity switch
    {
        CadCircle value => value.FillStyleId,
        CadEllipse value => value.FillStyleId,
        CadRectangle value => value.FillStyleId,
        CadPolyline value => value.FillStyleId,
        CadSpline value => value.FillStyleId,
        CadCompositePath value => value.FillStyleId,
        _ => null
    };

    private static bool SupportsFill(CadEntity entity) => entity is
        CadCircle or CadEllipse or CadRectangle or CadPolyline or CadSpline or CadCompositePath;

    private static string ResolveFillKind(CadDocument document, CadEntity entity)
    {
        var styleId = FillStyleId(entity);
        if (styleId is null)
            return "none";
        if (!document.TryGetStyle(styleId.Value, out var style) || style is null)
            return "unknown";
        return style switch
        {
            CadHatchFillStyle => "hatch",
            CadGradientFillStyle { IsSolid: true } => "solid",
            CadGradientFillStyle => "gradient",
            _ => ProtocolEnum(style.Kind)
        };
    }

    private static bool StyleMatches(CadDocument document, StyleId? styleId, string requested)
    {
        if (styleId is null)
            return string.Equals(requested, "none", StringComparison.OrdinalIgnoreCase);
        if (styleId.Value.Value.ToString(CultureInfo.InvariantCulture).Equals(requested, StringComparison.Ordinal))
            return true;
        return document.TryGetStyle(styleId.Value, out var style) &&
               style is not null &&
               style.Name.Equals(requested, StringComparison.OrdinalIgnoreCase);
    }

    private static string? StyleName(CadDocument document, StyleId? styleId) =>
        styleId is { } value &&
        document.TryGetStyle(value, out var style) &&
        style is not null
            ? style.Name
            : null;

    private static string LayerName(CadDocument document, LayerId layerId) =>
        document.TryGetLayer(layerId, out var layer) && layer is not null
            ? layer.Name
            : layerId.Value.ToString(CultureInfo.InvariantCulture);

    private static string OwnerName(CadDocument document, BlockId blockId) =>
        document.TryGetBlock(blockId, out var block) && block is not null
            ? block.Name
            : blockId.Value.ToString(CultureInfo.InvariantCulture);

    private static object PointDto(CadPointD point) => new { x = point.X, y = point.Y };

    private static object RectDto(CadRectD rect) => rect.IsEmpty
        ? new { empty = true }
        : (object)new { min_x = rect.MinX, min_y = rect.MinY, max_x = rect.MaxX, max_y = rect.MaxY };

    private static string ProtocolEnum<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
}
