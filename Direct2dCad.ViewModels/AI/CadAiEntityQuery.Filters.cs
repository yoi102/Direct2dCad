using System.Globalization;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.AI;

internal static partial class CadAiEntityQuery
{
    private static void ValidateOptions(CadAiEntityQueryOptions options)
    {
        if (options.Scope is not (CurrentSpaceScope or DocumentScope))
            throw new ArgumentException("scope must be current_space or document.");
        ValidateEnum(options.FillKind, "fill_kind", "none", "solid", "hatch", "gradient");
        ValidateEnum(options.ColorSource, "color_source", "by_layer", "explicit", "by_block");
        ValidateEnum(options.LineWeightSource, "line_weight_source", "by_layer", "explicit");
        ValidateEnum(options.DashStyle, "dash_style", "solid", "dash", "dot", "dash_dot", "dash_dot_dot");
        ValidateEnum(options.SpatialRelation, "spatial_relation", "intersects", "contained", "contains", "center_in");
        ValidateEnum(options.SortBy, "sort_by", "id", "name", "type", "layer", "z_index", "length", "width", "height", "bounds_area");
        if (options.SpatialRelation is not null && options.Bounds is null)
            throw new ArgumentException("bounds is required when spatial_relation is supplied.");
        ValidateRange(options.MinZIndex, options.MaxZIndex, "z_index");
        ValidateNonNegativeRange(options.MinLength, options.MaxLength, "length");
        ValidateNonNegativeRange(options.MinWidth, options.MaxWidth, "width");
        ValidateNonNegativeRange(options.MinHeight, options.MaxHeight, "height");
        ValidateNonNegativeRange(options.MinRadius, options.MaxRadius, "radius");
        ValidateNonNegativeRange(options.MinPointCount, options.MaxPointCount, "point_count");
        ValidateRange(options.MinOpacity, options.MaxOpacity, "opacity");
        if (options.MinOpacity is < 0 or > 1 || options.MaxOpacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException("opacity", "Opacity must be between zero and one.");
        if (options.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(options.Offset));
        if (options.Limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.Limit));
    }

    private static void ValidateEnum(string? value, string name, params string[] supported)
    {
        if (value is not null && !supported.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{name} must be one of: {string.Join(", ", supported)}.");
    }

    private static void ValidateNonNegativeRange(double? minimum, double? maximum, string name)
    {
        if (minimum is < 0 || maximum is < 0)
            throw new ArgumentOutOfRangeException(name, $"{name} values must not be negative.");
        ValidateRange(minimum, maximum, name);
    }

    private static void ValidateNonNegativeRange(int? minimum, int? maximum, string name)
    {
        if (minimum is < 0 || maximum is < 0)
            throw new ArgumentOutOfRangeException(name, $"{name} values must not be negative.");
        ValidateRange(minimum, maximum, name);
    }

    private static void ValidateRange<T>(T? minimum, T? maximum, string name)
        where T : struct, IComparable<T>
    {
        if (minimum is { } min && maximum is { } max && min.CompareTo(max) > 0)
            throw new ArgumentException($"min_{name} must not exceed max_{name}.");
    }

    private static IEnumerable<CadEntity> ApplyFilters(
        CadDocument document,
        IEnumerable<CadEntity> entities,
        IReadOnlySet<EntityId> selectedEntityIds,
        CadAiEntityQueryOptions options)
    {
        if (options.SelectedOnly)
            entities = entities.Where(entity => selectedEntityIds.Contains(entity.Id));

        var requestedIds = options.EntityIds?.ToHashSet();
        if (requestedIds is { Count: > 0 })
            entities = entities.Where(entity => requestedIds.Contains(entity.Id.Value));

        var requestedTypes = CombineFilters(options.Type, options.Types);
        if (requestedTypes.Count > 0)
            entities = entities.Where(entity => requestedTypes.Any(type => TypeMatches(entity, type)));

        var requestedLayers = CombineFilters(options.Layer, options.Layers);
        if (requestedLayers.Count > 0)
            entities = entities.Where(entity => requestedLayers.Any(layer => LayerMatches(document, entity, layer)));

        if (!string.IsNullOrWhiteSpace(options.Owner))
            entities = entities.Where(entity => OwnerMatches(document, entity, options.Owner));
        if (!string.IsNullOrWhiteSpace(options.Name))
            entities = entities.Where(entity => string.Equals(entity.Name, options.Name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.NameContains))
            entities = entities.Where(entity => entity.Name.Contains(options.NameContains, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.TextContains))
            entities = entities.Where(entity => EntityText(entity)?.Contains(options.TextContains, StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrWhiteSpace(options.SourceNameContains))
            entities = entities.Where(entity => SourceName(entity)?.Contains(options.SourceNameContains, StringComparison.OrdinalIgnoreCase) == true);
        if (options.IsVisible is { } isVisible)
            entities = entities.Where(entity => entity.IsVisible == isVisible);
        if (options.IsLocked is { } isLocked)
            entities = entities.Where(entity => entity.IsLocked == isLocked);
        if (options.IsClosed is { } isClosed)
            entities = entities.Where(entity => entity is Curve curve && curve.IsClosed == isClosed);
        if (options.HasFill is { } hasFill)
            entities = entities.Where(entity => SupportsFill(entity) && (FillStyleId(entity) is not null) == hasFill);
        if (!string.IsNullOrWhiteSpace(options.FillKind))
            entities = entities.Where(entity => string.Equals(
                ResolveFillKind(document, entity),
                options.FillKind,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.ColorSource))
            entities = entities.Where(entity => string.Equals(
                ProtocolEnum(entity.ColorSource),
                options.ColorSource,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.LineWeightSource))
            entities = entities.Where(entity => string.Equals(
                entity.UseLayerLineWeight ? "by_layer" : "explicit",
                options.LineWeightSource,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.GraphicStyle))
            entities = entities.Where(entity => StyleMatches(document, GraphicStyleId(entity), options.GraphicStyle));
        if (!string.IsNullOrWhiteSpace(options.FillStyle))
            entities = entities.Where(entity => StyleMatches(document, FillStyleId(entity), options.FillStyle));
        if (!string.IsNullOrWhiteSpace(options.DashStyle))
            entities = entities.Where(entity => string.Equals(
                ProtocolEnum(entity.StrokeStyle.DashStyle),
                options.DashStyle,
                StringComparison.OrdinalIgnoreCase));

        entities = ApplyComparableRange(entities, entity => entity.ZIndex, options.MinZIndex, options.MaxZIndex);
        entities = ApplyNullableRange(entities, CurveLength, options.MinLength, options.MaxLength);
        entities = ApplyRange(entities, entity => entity.Bounds.Width, options.MinWidth, options.MaxWidth);
        entities = ApplyRange(entities, entity => entity.Bounds.Height, options.MinHeight, options.MaxHeight);
        entities = ApplyNullableRange(entities, Radius, options.MinRadius, options.MaxRadius);
        entities = ApplyNullableComparableRange(entities, PointCount, options.MinPointCount, options.MaxPointCount);
        entities = ApplyNullableRange(entities, Opacity, options.MinOpacity, options.MaxOpacity);

        if (options.Bounds is { } bounds)
        {
            var relation = options.SpatialRelation ?? "intersects";
            entities = entities.Where(entity => SpatialMatches(entity.Bounds, bounds, relation));
        }

        return entities;
    }

    private static IEnumerable<CadEntity> OrderEntities(
        CadDocument document,
        IEnumerable<CadEntity> entities,
        CadAiEntityQueryOptions options)
    {
        Func<CadEntity, object?> keySelector = options.SortBy switch
        {
            "id" => entity => entity.Id.Value,
            "name" => entity => entity.Name,
            "type" => EntityType,
            "layer" => entity => LayerName(document, entity.LayerId),
            "z_index" => entity => entity.ZIndex,
            "length" => entity => CurveLength(entity),
            "width" => entity => entity.Bounds.Width,
            "height" => entity => entity.Bounds.Height,
            "bounds_area" => entity => entity.Bounds.Width * entity.Bounds.Height,
            _ => throw new ArgumentException($"Unsupported entity sort field: {options.SortBy}")
        };

        return options.SortDescending
            ? entities.OrderByDescending(keySelector, CadQueryValueComparer.Instance).ThenBy(entity => entity.Id.Value)
            : entities.OrderBy(keySelector, CadQueryValueComparer.Instance).ThenBy(entity => entity.Id.Value);
    }

    private static bool TypeMatches(CadEntity entity, string requested) =>
        string.Equals(EntityType(entity), requested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entity.GetType().Name, requested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entity.GetType().Name, $"Cad{requested}", StringComparison.OrdinalIgnoreCase);

    private static bool LayerMatches(CadDocument document, CadEntity entity, string requested) =>
        string.Equals(LayerName(document, entity.LayerId), requested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entity.LayerId.Value.ToString(CultureInfo.InvariantCulture), requested, StringComparison.Ordinal);

    private static bool OwnerMatches(CadDocument document, CadEntity entity, string requested) =>
        string.Equals(OwnerName(document, entity.OwnerBlockId), requested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entity.OwnerBlockId.Value.ToString(CultureInfo.InvariantCulture), requested, StringComparison.Ordinal);

    private static IReadOnlyList<string> CombineFilters(string? single, IReadOnlyList<string>? multiple)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(single))
            result.Add(single);
        if (multiple is not null)
            result.AddRange(multiple.Where(value => !string.IsNullOrWhiteSpace(value)));
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? EntityText(CadEntity entity) => entity switch
    {
        CadText text => text.Text,
        CadShapeText text => text.Text,
        _ => null
    };

    private static string? SourceName(CadEntity entity) => entity switch
    {
        CadImage image => image.SourceName,
        CadOleObject ole => ole.SourceName,
        _ => null
    };

    private static double? CurveLength(CadEntity entity) => entity is Curve curve ? curve.Length : null;

    private static double? Radius(CadEntity entity) => entity switch
    {
        CadCircle circle => circle.Radius,
        CadArc arc => arc.Radius,
        _ => null
    };

    private static int? PointCount(CadEntity entity) => entity switch
    {
        CadPolyline polyline => polyline.Points.Count,
        CadSpline spline => spline.FitPoints.Count,
        CadCompositePath path => path.Segments.Count,
        _ => null
    };

    private static double? Opacity(CadEntity entity) => entity switch
    {
        CadImage image => image.Opacity,
        CadOleObject ole => ole.Opacity,
        _ => null
    };

    private static IEnumerable<CadEntity> ApplyRange(
        IEnumerable<CadEntity> entities,
        Func<CadEntity, double> selector,
        double? minimum,
        double? maximum)
    {
        if (minimum is { } min)
            entities = entities.Where(entity => selector(entity) >= min);
        if (maximum is { } max)
            entities = entities.Where(entity => selector(entity) <= max);
        return entities;
    }

    private static IEnumerable<CadEntity> ApplyNullableRange(
        IEnumerable<CadEntity> entities,
        Func<CadEntity, double?> selector,
        double? minimum,
        double? maximum)
    {
        if (minimum is null && maximum is null)
            return entities;
        return entities.Where(entity =>
        {
            var value = selector(entity);
            return value is not null &&
                   (minimum is null || value.Value >= minimum.Value) &&
                   (maximum is null || value.Value <= maximum.Value);
        });
    }

    private static IEnumerable<CadEntity> ApplyComparableRange<T>(
        IEnumerable<CadEntity> entities,
        Func<CadEntity, T> selector,
        T? minimum,
        T? maximum)
        where T : struct, IComparable<T>
    {
        if (minimum is { } min)
            entities = entities.Where(entity => selector(entity).CompareTo(min) >= 0);
        if (maximum is { } max)
            entities = entities.Where(entity => selector(entity).CompareTo(max) <= 0);
        return entities;
    }

    private static IEnumerable<CadEntity> ApplyNullableComparableRange<T>(
        IEnumerable<CadEntity> entities,
        Func<CadEntity, T?> selector,
        T? minimum,
        T? maximum)
        where T : struct, IComparable<T>
    {
        if (minimum is null && maximum is null)
            return entities;
        return entities.Where(entity =>
        {
            var value = selector(entity);
            return value is not null &&
                   (minimum is null || value.Value.CompareTo(minimum.Value) >= 0) &&
                   (maximum is null || value.Value.CompareTo(maximum.Value) <= 0);
        });
    }

    private static bool SpatialMatches(CadRectD entityBounds, CadRectD queryBounds, string relation) => relation switch
    {
        "intersects" => entityBounds.Intersects(queryBounds),
        "contained" => queryBounds.Contains(entityBounds),
        "contains" => entityBounds.Contains(queryBounds),
        "center_in" => queryBounds.Contains(entityBounds.Center),
        _ => throw new ArgumentException($"Unsupported spatial relation: {relation}")
    };

    private sealed class CadQueryValueComparer : IComparer<object?>
    {
        internal static CadQueryValueComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return 1;
            if (y is null)
                return -1;
            if (x is string leftString && y is string rightString)
                return StringComparer.OrdinalIgnoreCase.Compare(leftString, rightString);
            if (x is IComparable comparable)
                return comparable.CompareTo(y);
            return StringComparer.OrdinalIgnoreCase.Compare(x.ToString(), y.ToString());
        }
    }
}
