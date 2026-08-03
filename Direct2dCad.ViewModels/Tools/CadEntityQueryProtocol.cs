using System.Text.Json;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadEntityQueryProtocol
{
    internal static object CreateSchema(bool paged, int maximumListedEntities)
    {
        var properties = new Dictionary<string, object>
        {
            ["scope"] = new
            {
                type = "string",
                @enum = new[] { CadEntityQuery.CurrentSpaceScope, CadEntityQuery.DocumentScope },
                description = "Current editing space or every entity owned by any model, paper, or block space."
            },
            ["type"] = new { type = "string", @enum = CadEntityQuery.EntityTypeNames, description = "One entity type. Omit it when asking which types exist." },
            ["types"] = new { type = "array", minItems = 1, uniqueItems = true, items = new { type = "string", @enum = CadEntityQuery.EntityTypeNames }, description = "Match any of these entity types." },
            ["layer"] = String("One layer name or ID."),
            ["layers"] = StringArray("Match any of these layer names or IDs."),
            ["capability"] = String("One entity capability, such as fill, stroke_style, or rotation."),
            ["capabilities"] = StringArray("Match entities supporting any of these capabilities."),
            ["owner"] = String("Owner model, paper, or block-space name or ID."),
            ["selected_only"] = new { type = "boolean" }
        };

        if (paged)
            AddDetailQueryProperties(properties, maximumListedEntities);

        return new
        {
            type = "object",
            properties,
            additionalProperties = false
        };
    }

    internal static CadEntityQueryOptions Parse(
        JsonElement arguments,
        bool paged,
        int maximumListedEntities)
    {
        var scope = (OptionalString(arguments, "scope") ?? CadEntityQuery.CurrentSpaceScope)
            .ToLowerInvariant();
        if (scope is not (CadEntityQuery.CurrentSpaceScope or CadEntityQuery.DocumentScope))
            throw new ArgumentException("scope must be current_space or document.");

        return new CadEntityQueryOptions(
            scope,
            OptionalString(arguments, "type"),
            OptionalString(arguments, "layer"),
            OptionalBool(arguments, "selected_only"),
            OptionalString(arguments, "capability")?.ToLowerInvariant(),
            OptionalStringArray(arguments, "capabilities")?.Select(value => value.ToLowerInvariant()).ToArray(),
            Types: OptionalStringArray(arguments, "types"),
            Layers: OptionalStringArray(arguments, "layers"),
            EntityIds: OptionalInt64Array(arguments, "entity_ids"),
            Owner: OptionalString(arguments, "owner"),
            Name: OptionalString(arguments, "name"),
            NameContains: OptionalString(arguments, "name_contains"),
            TextContains: OptionalString(arguments, "text_contains"),
            SourceNameContains: OptionalString(arguments, "source_name_contains"),
            IsVisible: OptionalNullableBool(arguments, "visible"),
            IsLocked: OptionalNullableBool(arguments, "locked"),
            IsClosed: OptionalNullableBool(arguments, "closed"),
            HasFill: OptionalNullableBool(arguments, "has_fill"),
            FillKind: OptionalString(arguments, "fill_kind")?.ToLowerInvariant(),
            ColorSource: OptionalString(arguments, "color_source")?.ToLowerInvariant(),
            LineWeightSource: OptionalString(arguments, "line_weight_source")?.ToLowerInvariant(),
            GraphicStyle: OptionalString(arguments, "graphic_style"),
            FillStyle: OptionalString(arguments, "fill_style"),
            DashStyle: OptionalString(arguments, "dash_style")?.ToLowerInvariant(),
            MinZIndex: OptionalNullableInt(arguments, "min_z_index"),
            MaxZIndex: OptionalNullableInt(arguments, "max_z_index"),
            MinLength: OptionalNullableDouble(arguments, "min_length"),
            MaxLength: OptionalNullableDouble(arguments, "max_length"),
            MinWidth: OptionalNullableDouble(arguments, "min_width"),
            MaxWidth: OptionalNullableDouble(arguments, "max_width"),
            MinHeight: OptionalNullableDouble(arguments, "min_height"),
            MaxHeight: OptionalNullableDouble(arguments, "max_height"),
            MinRadius: OptionalNullableDouble(arguments, "min_radius"),
            MaxRadius: OptionalNullableDouble(arguments, "max_radius"),
            MinPointCount: OptionalNullableInt(arguments, "min_point_count"),
            MaxPointCount: OptionalNullableInt(arguments, "max_point_count"),
            MinOpacity: OptionalNullableDouble(arguments, "min_opacity"),
            MaxOpacity: OptionalNullableDouble(arguments, "max_opacity"),
            Bounds: OptionalBounds(arguments, "bounds"),
            SpatialRelation: OptionalString(arguments, "spatial_relation")?.ToLowerInvariant(),
            SortBy: paged ? (OptionalString(arguments, "sort_by") ?? "id").ToLowerInvariant() : "id",
            SortDescending: paged && string.Equals(
                OptionalString(arguments, "sort_direction"),
                "descending",
                StringComparison.OrdinalIgnoreCase),
            Offset: paged ? Math.Max(0, OptionalInt(arguments, "offset", 0)) : 0,
            Limit: paged ? Math.Clamp(OptionalInt(arguments, "limit", 50), 1, maximumListedEntities) : maximumListedEntities);
    }

    private static void AddDetailQueryProperties(
        IDictionary<string, object> properties,
        int maximumListedEntities)
    {
        properties["entity_ids"] = EntityIdArray();
        properties["name"] = String("Exact entity name, case-insensitive.");
        properties["name_contains"] = String("Case-insensitive entity-name fragment.");
        properties["text_contains"] = String("Case-insensitive CadText or CadShapeText content fragment.");
        properties["source_name_contains"] = String("Case-insensitive image or OLE source-name fragment.");
        properties["capability"] = new
        {
            type = "string",
            @enum = CadEntityQuery.CapabilityNames,
            description = "Require one capability supported by the concrete entity type."
        };
        properties["capabilities"] = new
        {
            type = "array",
            minItems = 1,
            uniqueItems = true,
            items = new { type = "string", @enum = CadEntityQuery.CapabilityNames }
        };
        properties["visible"] = new { type = "boolean" };
        properties["locked"] = new { type = "boolean" };
        properties["closed"] = new { type = "boolean", description = "Match closed or open curve entities." };
        properties["has_fill"] = new { type = "boolean", description = "Match fill-capable entities with or without a fill style." };
        properties["fill_kind"] = new
        {
            type = "string",
            @enum = new[] { "none", "solid", "hatch", "gradient" },
            description = "Match fill-capable entities by their effective fill kind; non-fill entities are excluded."
        };
        properties["color_source"] = new { type = "string", @enum = new[] { "by_layer", "explicit", "by_block" } };
        properties["line_weight_source"] = new { type = "string", @enum = new[] { "by_layer", "explicit" } };
        properties["graphic_style"] = String("Assigned graphic-style name or ID on graphic-style-capable entities; use none for no assigned style.");
        properties["fill_style"] = String("Assigned fill-style name or ID on fill-capable entities; use none for no assigned style.");
        properties["dash_style"] = new { type = "string", @enum = new[] { "solid", "dash", "dot", "dash_dot", "dash_dot_dot" } };
        properties["min_z_index"] = new { type = "integer" };
        properties["max_z_index"] = new { type = "integer" };
        properties["min_length"] = NonNegativeNumber("Minimum curve length.");
        properties["max_length"] = NonNegativeNumber("Maximum curve length.");
        properties["min_width"] = NonNegativeNumber("Minimum world-coordinate bounds width.");
        properties["max_width"] = NonNegativeNumber("Maximum world-coordinate bounds width.");
        properties["min_height"] = NonNegativeNumber("Minimum world-coordinate bounds height.");
        properties["max_height"] = NonNegativeNumber("Maximum world-coordinate bounds height.");
        properties["min_radius"] = NonNegativeNumber("Minimum radius for Circle and Arc entities.");
        properties["max_radius"] = NonNegativeNumber("Maximum radius for Circle and Arc entities.");
        properties["min_point_count"] = new { type = "integer", minimum = 0, description = "Minimum Polyline points, Spline fit points, or CompositePath segments." };
        properties["max_point_count"] = new { type = "integer", minimum = 0, description = "Maximum Polyline points, Spline fit points, or CompositePath segments." };
        properties["min_opacity"] = UnitNumber("Minimum Image or OleObject opacity.");
        properties["max_opacity"] = UnitNumber("Maximum Image or OleObject opacity.");
        properties["bounds"] = BoundsSchema();
        properties["spatial_relation"] = new
        {
            type = "string",
            @enum = new[] { "intersects", "contained", "contains", "center_in" },
            description = "How entity bounds relate to bounds; defaults to intersects."
        };
        properties["sort_by"] = new
        {
            type = "string",
            @enum = new[] { "id", "name", "type", "layer", "z_index", "length", "width", "height", "bounds_area" }
        };
        properties["sort_direction"] = new { type = "string", @enum = new[] { "ascending", "descending" } };
        properties["offset"] = new { type = "integer", minimum = 0 };
        properties["limit"] = new { type = "integer", minimum = 1, maximum = maximumListedEntities };
    }

    private static object BoundsSchema() => new
    {
        type = "object",
        description = "CAD world-coordinate spatial query rectangle.",
        properties = new
        {
            min_x = Number("Minimum X"),
            min_y = Number("Minimum Y"),
            max_x = Number("Maximum X"),
            max_y = Number("Maximum Y")
        },
        required = new[] { "min_x", "min_y", "max_x", "max_y" },
        additionalProperties = false
    };

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static int OptionalInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static int? OptionalNullableInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (!value.TryGetInt32(out var result))
            throw new ArgumentException($"{name} must be an integer.");
        return result;
    }

    private static double? OptionalNullableDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static bool OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static bool? OptionalNullableBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException($"{name} must be a boolean.");
        return value.GetBoolean();
    }

    private static string[]? OptionalStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array.");
        var result = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
            .ToArray();
        if (result.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"{name} must contain non-empty strings.");
        return result.Select(item => item!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static long[]? OptionalInt64Array(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array.");
        var result = new List<long>();
        foreach (var item in value.EnumerateArray())
        {
            if (!item.TryGetInt64(out var id))
                throw new ArgumentException($"{name} must contain integer IDs.");
            result.Add(id);
        }
        return result.Distinct().ToArray();
    }

    private static CadRectD? OptionalBounds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} must be an object.");
        var minX = RequiredDouble(value, "min_x");
        var minY = RequiredDouble(value, "min_y");
        var maxX = RequiredDouble(value, "max_x");
        var maxY = RequiredDouble(value, "max_y");
        if (maxX < minX || maxY < minY)
            throw new ArgumentException($"{name} max values must be greater than or equal to min values.");
        return CadRectD.FromLTRB(minX, minY, maxX, maxY);
    }

    private static double RequiredDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }
        return result;
    }

    private static object Number(string description) => new { type = "number", description };
    private static object NonNegativeNumber(string description) => new { type = "number", minimum = 0.0, description };
    private static object UnitNumber(string description) => new { type = "number", minimum = 0.0, maximum = 1.0, description };
    private static object String(string description) => new { type = "string", description };
    private static object StringArray(string description) => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "string" },
        description
    };
    private static object EntityIdArray() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "integer" }
    };
}
