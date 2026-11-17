using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Application.Platform;
using Direct2dCad.IO;

using MessagePipe;


namespace Direct2dCad.Application.Tools;

/// <summary>
/// Routes one tool execution session across the open-document workspace. Each touched document
/// receives its own command batch so its undo history remains independent.
/// </summary>

public sealed partial class CadWorkspaceToolExecutor
{
    private void ValidateCreationProperties(
        string toolName,
        JsonElement arguments,
        CadDocumentToolExecutor executor)
    {
        ValidateCreationGeometryProperties(toolName, arguments);
        ValidateCreationAppearance(toolName, arguments, executor);
        CadEntitySpecificPropertyTools.ValidateCreationArguments(
            executor.Session.CadEditor.Document,
            toolName,
            arguments);
    }

    private static void ValidateCreationGeometryProperties(string toolName, JsonElement arguments)
    {
        var supported = toolName switch
        {
            "add_line" => new[] { "x1", "y1", "x2", "y2" },
            "add_circle" => new[] { "center_x", "center_y", "radius" },
            "add_rectangle" => new[] { "min_x", "min_y", "max_x", "max_y", "corner_radius", "corner_radius_x", "corner_radius_y" },
            "add_text" => new[] { "text", "x", "y", "height", "rotation_degrees" },
            "add_shape_text" => new[] { "text", "x", "y", "height", "rotation_degrees", "width_factor", "character_spacing_factor", "oblique_angle_degrees" },
            "add_polyline" => new[] { "points", "closed" },
            "add_arc" => new[] { "center_x", "center_y", "radius", "start_angle_degrees", "sweep_angle_degrees" },
            "add_ellipse" => new[] { "center_x", "center_y", "radius_x", "radius_y" },
            "add_ellipse_arc" => new[] { "center_x", "center_y", "radius_x", "radius_y", "start_angle_degrees", "sweep_angle_degrees" },
            "add_polygon" => new[] { "points" },
            "add_spline" => new[] { "fit_points", "control_points", "degree", "knots", "weights", "periodic", "closed" },
            "add_composite_path" => new[] { "start", "segments", "closed" },
            _ => []
        };

        if (supported.Length == 0)
            return;

        var unsupported = arguments.EnumerateObject()
            .Where(property => IsCreationGeometryField(property.Name) &&
                               !supported.Contains(property.Name, StringComparer.Ordinal))
            .Select(property => property.Name)
            .ToArray();
        if (unsupported.Length > 0)
        {
            throw new NotSupportedException(
                $"{toolName} does not support geometry field(s): {string.Join(", ", unsupported)}.");
        }
    }

    private static bool IsCreationGeometryField(string name) => name switch
    {
        "x1" or "y1" or "x2" or "y2" or
        "center_x" or "center_y" or "radius" or "radius_x" or "radius_y" or
        "start_angle_degrees" or "sweep_angle_degrees" or
        "min_x" or "min_y" or "max_x" or "max_y" or
        "corner_radius" or "corner_radius_x" or "corner_radius_y" or
        "text" or "x" or "y" or "height" or "rotation_degrees" or
        "width_factor" or "character_spacing_factor" or "oblique_angle_degrees" or
        "points" or "fit_points" or "control_points" or "degree" or "knots" or "weights" or "periodic" or "start" or "segments" or "closed" => true,
        _ => false
    };

    private void ValidateCreationAppearance(string toolName, JsonElement arguments, CadDocumentToolExecutor executor)
    {
        var document = executor.Session.CadEditor.Document;
        if (HasValue(arguments, "color") && HasValue(arguments, "graphic_style"))
            throw new ArgumentException("color and graphic_style cannot be set in the same call.");
        if (HasValue(arguments, "graphic_style"))
            _ = ResolveGraphicStyle(document, RequiredString(arguments, "graphic_style"));
        if (HasValue(arguments, "color"))
            _ = ParseColor(RequiredString(arguments, "color"));
        if (HasValue(arguments, "color_source"))
            _ = ParseEnum<CadColorSource>(RequiredString(arguments, "color_source"));
        if (arguments.TryGetProperty("line_weight", out var lineWeight) && lineWeight.ValueKind != JsonValueKind.Null)
            _ = ParseLineWeight(lineWeight);
        if (HasValue(arguments, "z_index") &&
            (!arguments.TryGetProperty("z_index", out var zIndex) || !zIndex.TryGetInt32(out _)))
        {
            throw new ArgumentException("z_index must be an integer.");
        }
        if (HasValue(arguments, "visible") &&
            arguments.GetProperty("visible").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException("visible must be a boolean.");
        }
        if (HasValue(arguments, "locked") &&
            arguments.GetProperty("locked").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException("locked must be a boolean.");
        }

        if (HasValue(arguments, "fill") &&
            (!arguments.TryGetProperty("fill", out var fill) || fill.ValueKind != JsonValueKind.Object))
        {
            throw new ArgumentException("fill must be an object.");
        }
        if (arguments.TryGetProperty("fill", out fill) && fill.ValueKind == JsonValueKind.Object)
        {
            if (toolName is not ("add_circle" or "add_ellipse" or "add_rectangle" or "add_polygon" or "add_polyline" or "add_spline" or "add_composite_path"))
                throw new NotSupportedException($"{toolName} does not support fill.");
            if (toolName is "add_polyline" or "add_spline" or "add_composite_path" &&
                (!arguments.TryGetProperty("closed", out var closed) || closed.ValueKind != JsonValueKind.True))
            {
                throw new NotSupportedException($"Only a closed {toolName[4..]} supports fill.");
            }
            ValidateFillArguments(document, fill);
        }

        if (HasValue(arguments, "stroke_style") &&
            (!arguments.TryGetProperty("stroke_style", out var stroke) || stroke.ValueKind != JsonValueKind.Object))
        {
            throw new ArgumentException("stroke_style must be an object.");
        }
        if (arguments.TryGetProperty("stroke_style", out stroke) && stroke.ValueKind == JsonValueKind.Object)
        {
            if (toolName is "add_text" or "add_shape_text")
                throw new NotSupportedException($"{toolName} does not support stroke_style.");
            foreach (var field in StrokeFields)
                if (HasValue(stroke, field))
                    ValidateStrokeEnum(field, RequiredString(stroke, field));

            // The creation schema shares one appearance object between entity types.
            // Unsupported fields are filtered after creation, when the concrete
            // entity and its open/closed state are available.
        }
    }

    private static void ValidateFillArguments(CadDocument document, JsonElement arguments)
    {
        var mode = RequiredString(arguments, "mode").Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (mode is "none" or "no_fill")
        {
            RejectFillProperties(arguments, mode,
                "style", "color", "pattern", "scale", "angle_degrees", "origin_x", "origin_y",
                "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees",
                "gradient_origin_x", "gradient_origin_y", "gradient_centered");
            return;
        }
        if (mode == "style")
        {
            RejectFillProperties(arguments, mode,
                "color", "pattern", "scale", "angle_degrees", "origin_x", "origin_y",
                "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees",
                "gradient_origin_x", "gradient_origin_y", "gradient_centered");
            _ = ResolveExistingFillStyle(document, RequiredString(arguments, "style"));
            return;
        }
        if (mode is not ("solid" or "hatch" or "gradient"))
            throw new ArgumentException($"Unsupported fill mode: {mode}");
        if (HasValue(arguments, "color"))
            _ = ParseColor(RequiredString(arguments, "color"));
        if (mode == "solid")
        {
            RejectFillProperties(arguments, mode,
                "style", "pattern", "scale", "angle_degrees", "origin_x", "origin_y",
                "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees",
                "gradient_origin_x", "gradient_origin_y", "gradient_centered");
        }
        else if (mode == "hatch")
        {
            RejectFillProperties(arguments, mode,
                "style", "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees",
                "gradient_origin_x", "gradient_origin_y", "gradient_centered");
        }
        else
        {
            RejectFillProperties(arguments, mode,
                "style", "color", "pattern", "scale", "angle_degrees", "origin_x", "origin_y");
        }
        _ = OptionalPositive(arguments, "scale", 1.0);
        _ = OptionalFinite(arguments, "angle_degrees", 0.0);
        _ = OptionalFinite(arguments, "origin_x", 0.0);
        _ = OptionalFinite(arguments, "origin_y", 0.0);

        if (mode != "hatch")
        {
            if (mode == "gradient")
            {
                _ = ParseGradientKind(arguments);
                _ = ParseGradientStops(arguments);
                _ = OptionalPositive(arguments, "gradient_scale", 1.0);
                _ = OptionalFinite(arguments, "gradient_angle_degrees", 0.0);
                _ = OptionalFinite(arguments, "gradient_origin_x", 0.0);
                _ = OptionalFinite(arguments, "gradient_origin_y", 0.0);
                if (HasValue(arguments, "gradient_centered") &&
                    arguments.GetProperty("gradient_centered").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ArgumentException("gradient_centered must be a boolean.");
                }
            }
            return;
        }
        var requestedPattern = OptionalString(arguments, "pattern") ?? "ANSI31";
        var exists = document.HatchPatterns.Values.Any(pattern =>
            string.Equals(pattern.Name, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pattern.Id.Value.ToString(CultureInfo.InvariantCulture), requestedPattern, StringComparison.Ordinal));
        var isDefault = FillStyleCatalog.BuildFillStyleOptions(document).Any(option =>
            option.Kind == FillStyleOptionKind.Hatch &&
            (string.Equals(option.StyleName, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option.Name, requestedPattern, StringComparison.OrdinalIgnoreCase)));
        if (!exists && !isDefault)
            throw new ArgumentException($"Hatch pattern not found: {requestedPattern}");
    }

    private static void RejectFillProperties(JsonElement arguments, string mode, params string[] names)
    {
        var supplied = names.Where(name => HasValue(arguments, name)).ToArray();
        if (supplied.Length > 0)
            throw new ArgumentException($"Fill mode '{mode}' does not accept: {string.Join(", ", supplied)}.");
    }

    private static void ValidateStrokeEnum(string field, string value)
    {
        if (field is "start_cap" or "end_cap" or "dash_cap")
            _ = ParseEnum<CadStrokeCap>(value);
        else if (field == "dash_style")
            _ = ParseEnum<CadStrokeDashStyle>(value);
        else if (field == "line_join")
            _ = ParseEnum<CadStrokeLineJoin>(value);
    }

    private static JsonObject FilterSupportedCreationStrokeStyle(
        CadEntity entity,
        JsonElement stroke)
    {
        var filtered = new JsonObject();
        foreach (var field in StrokeFields)
        {
            if (!HasValue(stroke, field) || !SupportsCreationStrokeField(entity, field))
                continue;

            filtered[field] = JsonNode.Parse(stroke.GetProperty(field).GetRawText());
        }

        return filtered;
    }

    private static bool SupportsCreationStrokeField(CadEntity entity, string field) =>
        field switch
        {
            "start_cap" or "end_cap" => CadEntityCapabilities.SupportsStartEndCaps(entity),
            "line_join" => CadEntityCapabilities.SupportsLineJoin(entity),
            "dash_cap" or "dash_style" => CadEntityCapabilities.SupportsStrokeStyle(entity),
            _ => false
        };

    private IReadOnlyList<string> ApplyCreationAppearance(
        CadDocumentToolExecutor executor,
        EntityId entityId,
        JsonElement arguments)
    {
        var changed = new List<string>();
        var common = new JsonObject
        {
            ["entity_ids"] = new JsonArray(JsonValue.Create(entityId.Value))
        };
        foreach (var field in CommonAppearanceFields)
        {
            if (arguments.TryGetProperty(field, out var value))
                common[field] = JsonNode.Parse(value.GetRawText());
        }

        if (common.Count > 1)
        {
            using var commonDocument = JsonDocument.Parse(common.ToJsonString());
            SetEntityCommonProperties(
                executor,
                commonDocument.RootElement);
            changed.AddRange(CommonAppearanceFields.Where(common.ContainsKey));
            if (common.ContainsKey("color") && !common.ContainsKey("color_source"))
                changed.Add("color_source");
        }

        if (arguments.TryGetProperty("fill", out var fill) && fill.ValueKind == JsonValueKind.Object)
        {
            using var fillDocument = AddEntityIds(fill, entityId);
            SetEntityFill(
                executor,
                fillDocument.RootElement);
            changed.Add("fill");
        }

        if (arguments.TryGetProperty("stroke_style", out var stroke) && stroke.ValueKind == JsonValueKind.Object)
        {
            var entity = executor.Session.CadEditor.Document.GetEntity(entityId);
            var supportedStroke = FilterSupportedCreationStrokeStyle(entity, stroke);
            if (supportedStroke.Count > 0)
            {
                using var supportedStrokeDocument = JsonDocument.Parse(supportedStroke.ToJsonString());
                using var strokeDocument = AddEntityIds(supportedStrokeDocument.RootElement, entityId);
                SetEntityStrokeStyle(executor, strokeDocument.RootElement);
                changed.Add("stroke_style");
            }
        }

        return changed;
    }

    private IReadOnlyList<string> ApplyCreationProperties(
        CadDocumentToolExecutor executor,
        EntityId entityId,
        string toolName,
        JsonElement arguments)
    {
        var changed = ApplyCreationAppearance(executor, entityId, arguments).ToList();
        var fields = toolName switch
        {
            "add_text" => new[] { "text_style", "font_family", "inverted", "inverted_margin_factor" },
            "add_shape_text" => new[] { "shape_font", "inverted", "inverted_margin_factor" },
            _ => []
        };
        var supplied = fields.Where(field => HasValue(arguments, field)).ToArray();
        if (supplied.Length == 0)
            return changed;

        var properties = new JsonObject
        {
            ["entity_ids"] = new JsonArray(JsonValue.Create(entityId.Value))
        };
        foreach (var field in supplied)
            properties[field] = JsonNode.Parse(arguments.GetProperty(field).GetRawText());

        using var document = JsonDocument.Parse(properties.ToJsonString());
        _ = CadEntitySpecificPropertyTools.Execute(executor, document.RootElement);
        changed.AddRange(supplied);
        return changed;
    }
}
