using System.Text.Json;
using Direct2dCad.AI;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadBulkCreationTools
{
    private const int MaximumEntitiesPerCall = 200;

    private static readonly HashSet<string> SupportedTypes =
    [
        "line", "circle", "rectangle", "text", "polyline",
        "arc", "ellipse", "polygon", "spline", "composite_path"
    ];

    internal static AiToolDefinition ToolDefinition { get; } = new(
        "add_entities",
        "Create up to 200 styled entities in one document undo batch. Prefer this for complete drawings with many parts.",
        JsonSerializer.SerializeToElement(CreateSchema()));

    internal static IReadOnlyList<CadBulkCreationItem> Parse(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("entities must be an array.");

        var count = entities.GetArrayLength();
        if (count is < 1 or > MaximumEntitiesPerCall)
            throw new ArgumentOutOfRangeException("entities", $"entities must contain between 1 and {MaximumEntitiesPerCall} items.");

        var result = new List<CadBulkCreationItem>(count);
        foreach (var entity in entities.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Each entities item must be an object.");
            var type = RequiredString(entity, "type").ToLowerInvariant();
            if (!SupportedTypes.Contains(type))
                throw new ArgumentException($"Unsupported bulk entity type: {type}");
            result.Add(new CadBulkCreationItem($"add_{type}", entity));
        }

        return result;
    }

    private static object CreateSchema()
    {
        var itemProperties = GeometryProperties();
        itemProperties["type"] = new { type = "string", @enum = SupportedTypes.Order().ToArray() };
        itemProperties["layer"] = String("Existing layer name or ID; current drawing layer is used when omitted");
        itemProperties["name"] = String("Optional entity name");
        itemProperties["color_source"] = Enum("by_layer", "explicit", "by_block");
        itemProperties["color"] = String("#RRGGBB, #AARRGGBB, or a named protocol color");
        itemProperties["line_weight"] = new
        {
            oneOf = new object[]
            {
                new { type = "number", exclusiveMinimum = 0.0 },
                new { type = "string", @enum = new[] { "by_layer" } }
            }
        };
        itemProperties["graphic_style"] = String("Existing graphic style name or ID; use none to clear");
        itemProperties["z_index"] = new { type = "integer" };
        itemProperties["visible"] = new { type = "boolean" };
        itemProperties["stroke_style"] = new
        {
            type = "object",
            properties = new
            {
                start_cap = Enum("flat", "square", "round", "triangle"),
                end_cap = Enum("flat", "square", "round", "triangle"),
                dash_cap = Enum("flat", "square", "round", "triangle"),
                dash_style = Enum("solid", "dash", "dot", "dash_dot", "dash_dot_dot"),
                line_join = Enum("miter", "bevel", "round", "miter_or_bevel")
            },
            additionalProperties = false
        };
        itemProperties["fill"] = new
        {
            type = "object",
            properties = new
            {
                mode = Enum("none", "style", "solid", "hatch"),
                style = String("Existing fill style name or ID"),
                color = String("Solid or hatch foreground color"),
                pattern = String("Hatch pattern name or ID"),
                scale = new { type = "number", exclusiveMinimum = 0.0 },
                angle_degrees = new { type = "number" },
                origin_x = new { type = "number" },
                origin_y = new { type = "number" }
            },
            required = new[] { "mode" },
            additionalProperties = false
        };

        return new
        {
            type = "object",
            properties = new
            {
                document_id = String("Stable open-document ID"),
                entities = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = MaximumEntitiesPerCall,
                    items = new
                    {
                        type = "object",
                        properties = itemProperties,
                        required = new[] { "type" },
                        additionalProperties = false
                    }
                }
            },
            required = new[] { "entities" },
            additionalProperties = false
        };
    }

    private static Dictionary<string, object> GeometryProperties() => new()
    {
        ["x1"] = Number("Line start X"), ["y1"] = Number("Line start Y"),
        ["x2"] = Number("Line end X"), ["y2"] = Number("Line end Y"),
        ["center_x"] = Number("Center X"), ["center_y"] = Number("Center Y"),
        ["radius"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["radius_x"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["radius_y"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["start_angle_degrees"] = Number("Arc start angle"),
        ["sweep_angle_degrees"] = Number("Arc sweep angle"),
        ["min_x"] = Number("Bounds minimum X"), ["min_y"] = Number("Bounds minimum Y"),
        ["max_x"] = Number("Bounds maximum X"), ["max_y"] = Number("Bounds maximum Y"),
        ["corner_radius"] = new { type = "number", minimum = 0.0 },
        ["text"] = String("Text content"),
        ["x"] = Number("Text insertion X"), ["y"] = Number("Text insertion Y"),
        ["height"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["rotation_degrees"] = Number("Counter-clockwise text rotation"),
        ["points"] = PointArray(2),
        ["fit_points"] = PointArray(2),
        ["start"] = Point(),
        ["segments"] = new
        {
            type = "array",
            minItems = 1,
            items = new
            {
                type = "object",
                properties = new
                {
                    type = Enum("line", "arc", "spline"),
                    end = Point(),
                    center = Point(),
                    sweep_angle_degrees = Number("Arc sweep"),
                    fit_points = PointArray(1)
                },
                required = new[] { "type" },
                additionalProperties = false
            }
        },
        ["closed"] = new { type = "boolean" }
    };

    private static object Point() => new
    {
        type = "object",
        properties = new { x = Number("X"), y = Number("Y") },
        required = new[] { "x", "y" },
        additionalProperties = false
    };

    private static object PointArray(int minimum) => new
    {
        type = "array",
        minItems = minimum,
        items = Point()
    };

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static object Number(string description) => new { type = "number", description };
    private static object String(string description) => new { type = "string", description };
    private static object Enum(params string[] values) => new { type = "string", @enum = values };
}

internal readonly record struct CadBulkCreationItem(string ToolName, JsonElement Arguments);
