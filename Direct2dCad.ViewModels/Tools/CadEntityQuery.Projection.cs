using System.Globalization;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
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
        capability = options.Capability,
        capabilities = options.Capabilities,
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
        layer_id = entity.LayerId.Value,
        layer = LayerName(document, entity.LayerId),
        layer_details = LayerDto(document, entity.LayerId),
        owner_block_id = entity.OwnerBlockId.Value,
        owner = OwnerName(document, entity.OwnerBlockId),
        capabilities = Capabilities(entity),
        capability_details = CapabilityDetails(entity),
        bounds = RectDto(entity.Bounds),
        entity.IsVisible,
        entity.IsLocked,
        color_source = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? ProtocolEnum(entity.ColorSource)
            : null,
        line_weight = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? entity.UseLayerLineWeight ? (object)"by_layer" : entity.LineWeight?.Value
            : null,
        line_weight_source = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? entity.UseLayerLineWeight ? "by_layer" : "explicit"
            : null,
        resolved_appearance = ResolvedAppearanceDto(document, entity),
        line_type_id = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? ResolveLineTypeId(document, entity)?.Value
            : null,
        entity.ZIndex,
        graphic_style_id = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? GraphicStyleId(entity)?.Value
            : null,
        graphic_style = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? StyleName(document, GraphicStyleId(entity))
            : null,
        graphic_style_details = CadEntityCapabilities.SupportsGraphicStyle(entity)
            ? GraphicStyleDto(document, entity)
            : null,
        fill_style_id = CadEntityCapabilities.SupportsFill(entity)
            ? FillStyleId(entity)?.Value
            : null,
        fill_style = CadEntityCapabilities.SupportsFill(entity)
            ? StyleName(document, FillStyleId(entity))
            : null,
        fill_kind = CadEntityCapabilities.SupportsFill(entity)
            ? ResolveFillKind(document, entity)
            : null,
        fill_details = CadEntityCapabilities.SupportsFill(entity)
            ? FillDto(document, entity)
            : null,
        stroke_style = CadEntityCapabilities.SupportsStrokeStyle(entity)
            ? new
            {
                start_cap = ProtocolEnum(entity.StrokeStyle.StartCap),
                end_cap = ProtocolEnum(entity.StrokeStyle.EndCap),
                dash_cap = ProtocolEnum(entity.StrokeStyle.DashCap),
                dash_style = ProtocolEnum(entity.StrokeStyle.DashStyle),
                line_join = ProtocolEnum(entity.StrokeStyle.LineJoin)
            }
            : null,
        characteristics = CharacteristicDto(document, entity)
    };

    private static object? ResolvedAppearanceDto(CadDocument document, CadEntity entity)
    {
        if (!CadEntityCapabilities.SupportsGraphicStyle(entity))
            return null;

        var dependsOnBlockContext = IsByBlockContextDependent(document, entity);
        return new
        {
            color = dependsOnBlockContext ? null : ColorText(ResolveStrokeColor(document, entity)),
            line_weight = ResolveLineWeight(document, entity)?.Value,
            line_type_id = ResolveLineTypeId(document, entity)?.Value,
            graphic_style = GraphicStyleDto(document, ResolveEffectiveGraphicStyleId(document, entity)),
            depends_on_block_context = dependsOnBlockContext
        };
    }

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
                result["text_style_id"] = text.TextStyleId?.Value;
                result["text_style"] = StyleName(document, text.TextStyleId);
                if (text.TextStyleId is { } textStyleId &&
                    document.TryGetStyle(textStyleId, out var textStyle) &&
                    textStyle is CadTextStyle textStyleData)
                {
                    result["text_style_properties"] = new
                    {
                        textStyleData.FontFamily,
                        textStyleData.TextHeight,
                        textStyleData.WidthFactor,
                        oblique_angle_degrees = textStyleData.ObliqueAngle * 180.0 / Math.PI,
                        textStyleData.IsBold,
                        textStyleData.IsItalic
                    };
                }
                result["inverted"] = text.IsInverted;
                result["inverted_margin_factor"] = text.InvertedMarginFactor;
                break;
            case CadShapeText text:
                result["text"] = text.Text;
                result["height"] = text.Height;
                result["rotation_degrees"] = CadArc.RadiansToDegrees(text.RotationRadians);
                result["width_factor"] = text.WidthFactor;
                result["character_spacing_factor"] = text.CharacterSpacingFactor;
                result["oblique_angle_degrees"] = CadArc.RadiansToDegrees(text.ObliqueAngleRadians);
                result["shape_font"] = text.ShapeFontId.Value;
                var shapeFont = CadShapeFontRegistry.Defaults.FirstOrDefault(font => font.Id == text.ShapeFontId);
                result["shape_font_name"] = shapeFont?.Name;
                result["shape_font_supports_unicode"] = shapeFont?.SupportsUnicode;
                result["inverted"] = text.IsInverted;
                result["inverted_margin_factor"] = text.InvertedMarginFactor;
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

    private static CadColor ResolveStrokeColor(CadDocument document, CadEntity entity)
    {
        var layer = document.GetLayer(entity.LayerId);
        var styleId = GraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
        return entity.ColorSource == CadColorSource.Explicit
            ? ResolveGraphicStyle(document, styleId)?.StrokeColor ?? ResolveLayerStrokeColor(document, layer)
            : ResolveLayerStrokeColor(document, layer);
    }

    private static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer) =>
        ResolveGraphicStyle(document, layer.DefaultGraphicStyleId)?.StrokeColor ?? layer.Color;

    private static CadLineWeight? ResolveLineWeight(CadDocument document, CadEntity entity)
    {
        if (!entity.UseLayerLineWeight &&
            entity.LineWeight is { IsByLayer: false } explicitWeight &&
            explicitWeight.Value > 0)
            return explicitWeight;

        var layer = document.GetLayer(entity.LayerId);
        var style = ResolveGraphicStyle(document, GraphicStyleId(entity) ?? layer.DefaultGraphicStyleId);
        return style?.LineWeight is { IsByLayer: false } styleWeight && styleWeight.Value > 0
            ? styleWeight
            : layer.LineWeight is { IsByLayer: false } layerWeight && layerWeight.Value > 0
                ? layerWeight
                : CadLineWeight.Default;
    }

    private static LineTypeId? ResolveLineTypeId(CadDocument document, CadEntity entity)
    {
        var layer = document.GetLayer(entity.LayerId);
        var style = ResolveGraphicStyle(document, GraphicStyleId(entity) ?? layer.DefaultGraphicStyleId);
        return IsByBlockContextDependent(document, entity)
            ? null
            : style?.LineTypeId ?? LineTypeId.Continuous;
    }

    private static bool IsByBlockContextDependent(CadDocument document, CadEntity entity) =>
        entity.ColorSource == CadColorSource.ByBlock &&
        document.TryGetBlock(entity.OwnerBlockId, out var block) &&
        block is { Kind: CadBlockKind.User };

    private static string[] Capabilities(CadEntity entity)
    {
        var capabilities = CadEntityCapabilities.GetCapabilities(entity);
        return Enum.GetValues<CadEntityCapability>()
            .Where(capability => capability != CadEntityCapability.None &&
                                (capabilities & capability) == capability)
            .Select(ProtocolEnum)
            .ToArray();
    }

    private static object[] CapabilityDetails(CadEntity entity)
    {
        var capabilities = CadEntityCapabilities.GetCapabilities(entity);
        return Enum.GetValues<CadEntityCapability>()
            .Where(capability => capability != CadEntityCapability.None &&
                                 (capabilities & capability) == capability)
            .Select(capability => new
            {
                capability = ProtocolEnum(capability),
                condition = CapabilityCondition(entity, capability)
            })
            .ToArray();
    }

    private static string CapabilityCondition(CadEntity entity, CadEntityCapability capability) =>
        capability switch
        {
            CadEntityCapability.StartEndCaps => "Supported for an open curve; full circles and closed curves do not support start/end caps.",
            CadEntityCapability.Fill => "Supported only for a closed curve.",
            CadEntityCapability.LineJoin => "Supported for polygonal or composite closed/open paths where the entity type exposes joins.",
            CadEntityCapability.Opacity => "Supported only for Image and OleObject.",
            CadEntityCapability.EmbeddedContent => "The persisted embedded payload is changed through the corresponding import/data tool.",
            CadEntityCapability.GraphicStyle => "Applies to the entity reference; a ByBlock result depends on its insertion context.",
            CadEntityCapability.StrokeStyle => "Applies to curve stroke caps, dash style, and joins; text and embedded entities do not expose it.",
            _ => "Supported for this entity type."
        };

    private static CadGraphicStyle? ResolveGraphicStyle(CadDocument document, StyleId? styleId) =>
        styleId is { } id && document.TryGetStyle(id, out var style) && style is CadGraphicStyle graphic
            ? graphic
            : null;

    private static StyleId? ResolveEffectiveGraphicStyleId(CadDocument document, CadEntity entity)
    {
        if (IsByBlockContextDependent(document, entity))
            return null;

        var layer = document.GetLayer(entity.LayerId);
        return GraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
    }

    private static object? LayerDto(CadDocument document, LayerId layerId)
    {
        if (!document.TryGetLayer(layerId, out var layer) || layer is null)
            return null;

        return new
        {
            id = layer.Id.Value,
            layer.Name,
            layer.IsVisible,
            layer.IsLocked,
            layer.IsFrozen,
            color = ColorText(layer.Color),
            line_weight = layer.LineWeight.IsByLayer ? (object)"by_layer" : layer.LineWeight.Value,
            default_graphic_style_id = layer.DefaultGraphicStyleId?.Value,
            default_graphic_style = GraphicStyleDto(document, layer.DefaultGraphicStyleId)
        };
    }

    private static string ColorText(CadColor color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

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

    private static object? GraphicStyleDto(CadDocument document, CadEntity entity)
    {
        return GraphicStyleDto(document, GraphicStyleId(entity));
    }

    private static object? GraphicStyleDto(CadDocument document, StyleId? styleId)
    {
        if (styleId is null || !document.TryGetStyle(styleId.Value, out var style) || style is not CadGraphicStyle graphic)
            return null;

        return new
        {
            id = graphic.Id.Value,
            graphic.Name,
            color = ColorText(graphic.StrokeColor),
            line_weight = graphic.LineWeight.IsByLayer ? (object)"by_layer" : graphic.LineWeight.Value,
            line_type_id = graphic.LineTypeId.Value
        };
    }

    private static object? FillDto(CadDocument document, CadEntity entity)
    {
        var styleId = FillStyleId(entity);
        if (styleId is null || !document.TryGetStyle(styleId.Value, out var style) || style is not CadFillStyle fill)
            return null;

        return fill switch
        {
            CadHatchFillStyle hatch => new
            {
                id = fill.Id.Value,
                fill.Name,
                kind = "hatch",
                pattern_id = hatch.PatternId.Value,
                pattern = document.TryGetHatchPattern(hatch.PatternId, out var pattern) ? pattern?.Name : null,
                color = ColorText(hatch.ForegroundColor),
                scale = hatch.HatchScale,
                angle_degrees = hatch.HatchAngle * 180.0 / Math.PI,
                origin = PointDto(hatch.HatchOrigin),
                annotative = hatch.IsAnnotative,
                gradient_kind = (string?)null,
                centered = (bool?)null,
                stops = (object?)null
            },
            CadGradientFillStyle gradient => new
            {
                id = fill.Id.Value,
                fill.Name,
                kind = gradient.IsSolid ? "solid" : "gradient",
                pattern_id = (long?)null,
                pattern = (string?)null,
                color = gradient.IsSolid ? ColorText(gradient.Stops[0].Color) : null,
                scale = gradient.GradientScale,
                angle_degrees = gradient.GradientAngle * 180.0 / Math.PI,
                origin = PointDto(gradient.GradientOrigin),
                annotative = (bool?)null,
                gradient_kind = gradient.GradientKind.ToString().ToLowerInvariant(),
                centered = (bool?)gradient.IsCentered,
                stops = gradient.Stops.Select(stop => new
                {
                    offset = stop.Offset,
                    color = ColorText(stop.Color)
                }).ToArray()
            },
            _ => new
            {
                id = fill.Id.Value,
                fill.Name,
                kind = ProtocolEnum(fill.FillKind),
                pattern_id = (long?)null,
                pattern = (string?)null,
                color = (string?)null,
                scale = (double?)null,
                angle_degrees = (double?)null,
                origin = (object?)null,
                annotative = (bool?)null,
                gradient_kind = (string?)null,
                centered = (bool?)null,
                stops = (object?)null
            }
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
