using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.AI;

internal static class CadAiCompositePathTools
{
    private const double RadiansPerDegree = Math.PI / 180.0;
    private const double DegreesPerRadian = 180.0 / Math.PI;

    internal static AiToolDefinition ToolDefinition { get; } = new(
        "add_composite_path",
        "Create one continuous path mixing line, circular arc, cubic Bezier, and interpolating spline segments. Prefer cubic_bezier segments for controlled organic outlines. A closed path supports one shared solid or hatch fill.",
        JsonSerializer.SerializeToElement(CreateSchema()));

    internal static CadAiCompositePathGeometry Parse(JsonElement arguments)
    {
        var start = RequiredPoint(arguments, "start");
        if (!arguments.TryGetProperty("segments", out var segmentArray) || segmentArray.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("segments must be an array.");
        if (segmentArray.GetArrayLength() == 0)
            throw new ArgumentException("segments must contain at least one segment.");

        var segments = new List<CadCompositePathSegment>(segmentArray.GetArrayLength());
        foreach (var element in segmentArray.EnumerateArray())
        {
            var type = RequiredString(element, "type").ToLowerInvariant();
            segments.Add(type switch
            {
                "line" => new CadCompositeLineSegment(RequiredPoint(element, "end")),
                "arc" => new CadCompositeArcSegment(
                    RequiredPoint(element, "center"),
                    RequiredSweepDegrees(element) * RadiansPerDegree),
                "spline" => new CadCompositeSplineSegment(
                    RequiredPoints(element, "fit_points", minimumCount: 1)),
                "cubic_bezier" => new CadCompositeBezierSegment(
                    RequiredPoint(element, "control1"),
                    RequiredPoint(element, "control2"),
                    RequiredPoint(element, "end")),
                _ => throw new ArgumentException($"Unsupported composite path segment type: {type}")
            });
        }

        var closed = arguments.TryGetProperty("closed", out var closedElement) &&
                     closedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? closedElement.GetBoolean()
            : throw new ArgumentException("closed is required and must be boolean.");
        return new CadAiCompositePathGeometry(start, segments, closed);
    }

    internal static object ToDto(CadCompositePath path)
    {
        var current = path.StartPoint;
        var segments = new List<object>(path.Segments.Count);
        foreach (var segment in path.Segments)
        {
            switch (segment)
            {
                case CadCompositeLineSegment line:
                    segments.Add(new { type = "line", end = PointDto(line.End) });
                    break;
                case CadCompositeArcSegment arc:
                    segments.Add(new
                    {
                        type = "arc",
                        center = PointDto(arc.Center),
                        sweep_angle_degrees = arc.SweepAngleRadians * DegreesPerRadian,
                        end = PointDto(CadCompositePath.GetEndPoint(current, arc))
                    });
                    break;
                case CadCompositeSplineSegment spline:
                    segments.Add(new
                    {
                        type = "spline",
                        fit_points = spline.FitPoints.Select(PointDto).ToArray()
                    });
                    break;
                case CadCompositeBezierSegment bezier:
                    segments.Add(new
                    {
                        type = "cubic_bezier",
                        control1 = PointDto(bezier.Control1),
                        control2 = PointDto(bezier.Control2),
                        end = PointDto(bezier.End)
                    });
                    break;
            }
            current = CadCompositePath.GetEndPoint(current, segment);
        }

        return new
        {
            start = PointDto(path.StartPoint),
            segments = segments.ToArray(),
            closed = path.Closed,
            end = PointDto(path.EndPoint)
        };
    }

    private static object CreateSchema()
    {
        var point = PointSchema();
        return new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["document_id"] = new { type = "string", description = "Stable open-document ID" },
                ["start"] = point,
                ["segments"] = new
                {
                    type = "array",
                    minItems = 1,
                    items = new
                    {
                        description = "line requires end; arc requires center and sweep_angle_degrees; cubic_bezier requires control1, control2, and end; spline requires fit_points.",
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["type"] = new { type = "string", @enum = new[] { "line", "arc", "cubic_bezier", "spline" } },
                            ["end"] = point,
                            ["center"] = point,
                            ["sweep_angle_degrees"] = new { type = "number", description = "Non-zero, at most 360 degrees" },
                            ["fit_points"] = PointArraySchema(1),
                            ["control1"] = new
                            {
                                type = "object",
                                description = "First cubic Bezier control handle, measured from the previous endpoint",
                                properties = new { x = new { type = "number" }, y = new { type = "number" } },
                                required = new[] { "x", "y" },
                                additionalProperties = false
                            },
                            ["control2"] = new
                            {
                                type = "object",
                                description = "Second cubic Bezier control handle, approaching end",
                                properties = new { x = new { type = "number" }, y = new { type = "number" } },
                                required = new[] { "x", "y" },
                                additionalProperties = false
                            }
                        },
                        required = new[] { "type" },
                        additionalProperties = false
                    }
                },
                ["closed"] = new { type = "boolean" },
                ["layer"] = new { type = "string", description = "Existing layer name or ID" },
                ["name"] = new { type = "string" },
                ["color_source"] = new { type = "string", @enum = new[] { "by_layer", "explicit", "by_block" } },
                ["color"] = new { type = "string" },
                ["line_weight"] = new
                {
                    oneOf = new object[]
                    {
                        new { type = "number", exclusiveMinimum = 0.0 },
                        new { type = "string", @enum = new[] { "by_layer" } }
                    }
                },
                ["graphic_style"] = new { type = "string" },
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["stroke_style"] = StrokeStyleSchema(),
                ["fill"] = FillSchema()
            },
            required = new[] { "start", "segments", "closed" },
            additionalProperties = false
        };
    }

    private static object PointSchema() => new
    {
        type = "object",
        properties = new { x = new { type = "number" }, y = new { type = "number" } },
        required = new[] { "x", "y" },
        additionalProperties = false
    };

    private static object PointArraySchema(int minimum) => new
    {
        type = "array",
        minItems = minimum,
        items = PointSchema()
    };

    private static object StrokeStyleSchema() => new
    {
        type = "object",
        properties = new
        {
            start_cap = EnumSchema("flat", "square", "round", "triangle"),
            end_cap = EnumSchema("flat", "square", "round", "triangle"),
            dash_cap = EnumSchema("flat", "square", "round", "triangle"),
            dash_style = EnumSchema("solid", "dash", "dot", "dash_dot", "dash_dot_dot"),
            line_join = EnumSchema("miter", "bevel", "round", "miter_or_bevel")
        },
        additionalProperties = false
    };

    private static object FillSchema() => new
    {
        type = "object",
        properties = new
        {
            mode = EnumSchema("none", "style", "solid", "hatch"),
            style = new { type = "string", description = "Existing fill style name or ID" },
            color = new { type = "string", description = "Solid or hatch foreground color" },
            pattern = new { type = "string", description = "Hatch pattern name or ID" },
            scale = new { type = "number", exclusiveMinimum = 0.0 },
            angle_degrees = new { type = "number" },
            origin_x = new { type = "number" },
            origin_y = new { type = "number" }
        },
        required = new[] { "mode" },
        additionalProperties = false
    };

    private static object EnumSchema(params string[] values) => new { type = "string", @enum = values };

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static CadPointD RequiredPoint(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} is required and must be a point.");
        return new CadPointD(RequiredFinite(point, "x"), RequiredFinite(point, "y"));
    }

    private static CadPointD[] RequiredPoints(JsonElement element, string name, int minimumCount)
    {
        if (!element.TryGetProperty(name, out var points) || points.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array.");
        var result = points.EnumerateArray().Select(point => new CadPointD(
            RequiredFinite(point, "x"),
            RequiredFinite(point, "y"))).ToArray();
        if (result.Length < minimumCount)
            throw new ArgumentException($"{name} must contain at least {minimumCount} point(s).");
        return result;
    }

    private static double RequiredFinite(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }
        return result;
    }

    private static double RequiredSweepDegrees(JsonElement element)
    {
        var sweep = RequiredFinite(element, "sweep_angle_degrees");
        if (Math.Abs(sweep) <= 1e-9 || Math.Abs(sweep) > 360.0 + 1e-9)
            throw new ArgumentOutOfRangeException("sweep_angle_degrees", "Sweep must be non-zero and no greater than 360 degrees.");
        return sweep;
    }

    private static object PointDto(CadPointD point) => new { x = point.X, y = point.Y };
}

internal sealed record CadAiCompositePathGeometry(
    CadPointD StartPoint,
    IReadOnlyList<CadCompositePathSegment> Segments,
    bool Closed);
