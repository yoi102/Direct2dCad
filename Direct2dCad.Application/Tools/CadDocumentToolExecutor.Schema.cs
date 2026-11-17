using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadDocumentToolExecutor
{
    private static AiToolDefinition Tool(string name, string description, object schema) =>
        new(name, description, Direct2dCad.AI.Contracts.CadJson.SerializeToElement(schema));

    private static object CoordinateSchema(IReadOnlyList<string> required, Dictionary<string, object> properties)
    {
        properties["layer"] = String("Layer name; current drawing layer is used when omitted");
        properties["name"] = String("Optional entity name");
        return new { type = "object", properties, required, additionalProperties = false };
    }

    private static object PointCollectionSchema(string propertyName, int minimumPoints, bool includeClosed)
    {
        var properties = new Dictionary<string, object>
        {
            [propertyName] = new
            {
                type = "array",
                minItems = minimumPoints,
                items = new
                {
                    type = "object",
                    properties = new { x = Number("X"), y = Number("Y") },
                    required = new[] { "x", "y" },
                    additionalProperties = false
                }
            },
            ["layer"] = String("Layer name; current drawing layer is used when omitted"),
            ["name"] = String("Optional entity name")
        };
        if (propertyName == "fit_points")
        {
            foreach (var pair in CadSplineTools.Properties()) properties[pair.Key] = pair.Value;
            return new { type = "object", properties = new Dictionary<string, object>(properties) { ["closed"] = new { type = "boolean" } },
                oneOf = new[] { new { required = new[] { "fit_points" } }, new { required = new[] { "control_points" } } }, additionalProperties = false };
        }
        if (includeClosed)
            properties["closed"] = new { type = "boolean" };
        return new
        {
            type = "object",
            properties,
            required = new[] { propertyName },
            additionalProperties = false
        };
    }

    private static object EntityIdsSchema(bool required) => new
    {
        type = "object",
        properties = new { entity_ids = EntityIdArray() },
        required = required ? new[] { "entity_ids" } : Array.Empty<string>(),
        additionalProperties = false
    };

    private static object DeleteEntitiesSchema() => new
    {
        type = "object",
        properties = new
        {
            entity_ids = EntityIdArray(),
            confirm = new { type = "boolean", @const = true, description = "Required explicit confirmation for deletion." }
        },
        required = new[] { "confirm" },
        additionalProperties = false
    };

    private static object EntityIdArray() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "integer" }
    };

    private static object Number(string description) => new { type = "number", description };

    private static object String(string description) => new { type = "string", description };

    private static object ViewSettingsSchema()
    {
        return new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["unit"] = new { type = "string", @enum = Enum.GetNames<CadUnit>() },
                ["grid_snap_enabled"]=new {type="boolean"},["object_snap_enabled"]=new {type="boolean"},
                ["ortho_enabled"]=new {type="boolean"},["polar_enabled"]=new {type="boolean"},
                ["polar_increment_degrees"]=new {type="number",exclusiveMinimum=0,maximum=180},
                ["snap_screen_tolerance"]=new {type="number",minimum=2,maximum=30},
                ["object_snap_modes"]=new {type="array",items=new {type="string",@enum=Enum.GetNames<CadObjectSnapModes>()}},
                ["grid_type"] = new { type = "string", @enum = Enum.GetNames<CadGridType>() },
                ["major_grid_preset"] = new { type = "string", description = "Existing grid preset name or GUID." },
                ["minor_grid_preset"] = new { type = "string", description = "Existing grid preset name or GUID." },
                ["snap_marker_type"] = new { type = "string", @enum = Enum.GetNames<CadSnapMarkerType>() },
                ["origin_display_type"] = new { type = "string", @enum = Enum.GetNames<CadOriginDisplayType>() },
                ["origin_marker_type"] = new { type = "string", @enum = Enum.GetNames<CadOriginMarkerType>() },
                ["origin_line_pattern"] = new { type = "string", @enum = Enum.GetNames<CadOriginLinePattern>() },
                ["background_color"] = new { type = "string", description = "#RRGGBB, #AARRGGBB, or a supported named color." },
                ["length_precision"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["angle_precision"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["grid_spacing_x_millimeters"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_spacing_y_millimeters"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_minor_spacing_x_millimeters"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_minor_spacing_y_millimeters"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_minimum_screen_spacing"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_minimum_world_spacing_millimeters"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_minor_line_color"] = new { type = "string" },
                ["grid_major_line_color"] = new { type = "string" },
                ["grid_minor_line_width"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_major_line_width"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["snap_marker_color"] = new { type = "string" },
                ["snap_marker_length"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["snap_marker_stroke_width"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["grid_snap_spacing_x_millimeters"] = new { type = "number", minimum = 0.0 },
                ["grid_snap_spacing_y_millimeters"] = new { type = "number", minimum = 0.0 },
                ["origin_color"] = new { type = "string" },
                ["origin_position_x_millimeters"] = new { type = "number" },
                ["origin_position_y_millimeters"] = new { type = "number" },
                ["origin_size"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["origin_stroke_width"] = new { type = "number", exclusiveMinimum = 0.0 }
            },
            additionalProperties = false
        };
    }

    private static object ViewportSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["operation"] = new { type = "string", @enum = new[] { "fit", "fit_bounds", "zoom", "zoom_entity", "pan", "center" } },
            ["factor"] = new { type = "number", exclusiveMinimum = 0.0, description = "Zoom multiplier; greater than 1 zooms in." },
            ["anchor_x"] = Number("World-coordinate zoom anchor X"),
            ["anchor_y"] = Number("World-coordinate zoom anchor Y"),
            ["delta_x"] = Number("World-coordinate pan delta X"),
            ["delta_y"] = Number("World-coordinate pan delta Y"),
            ["x"] = Number("World-coordinate center X"),
            ["y"] = Number("World-coordinate center Y"),
            ["padding"] = new { type = "number", minimum = 0.0, description = "Screen padding for fit." },
            ["entity_id"] = new { type = "integer", description = "Active-space entity ID for zoom_entity or fit_bounds." },
            ["min_x"] = Number("Bounds minimum X for fit_bounds"),
            ["min_y"] = Number("Bounds minimum Y for fit_bounds"),
            ["max_x"] = Number("Bounds maximum X for fit_bounds"),
            ["max_y"] = Number("Bounds maximum Y for fit_bounds")
        },
        required = new[] { "operation" },
        additionalProperties = false
    };

    private static object GridPresetManagementSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["operation"] = new { type = "string", @enum = new[] { "list", "create", "rename", "update", "reorder", "delete" } },
            ["preset_id"] = new { type = "string", format = "uuid" },
            ["preset_ids"] = new { type = "array", minItems = 2, uniqueItems = true,
                items = new { type = "string", format = "uuid" }, description = "Complete ordered preset IDs for operation=reorder." },
            ["name"] = String("Preset name"),
            ["spacing_x_millimeters"] = new { type = "number", minimum = CadGridSettings.MinimumSpacingMillimeters, maximum = CadGridSettings.MaximumSpacingMillimeters },
            ["spacing_y_millimeters"] = new { type = "number", minimum = CadGridSettings.MinimumSpacingMillimeters, maximum = CadGridSettings.MaximumSpacingMillimeters },
            ["link_axes"] = new { type = "boolean" }
        },
        required = new[] { "operation" },
        allOf = new object[]
        {
            new { @if = new { properties = new { operation = new { @const = "create" } } },
                then = new { required = new[] { "name", "spacing_x_millimeters" } } },
            new { @if = new { properties = new { operation = new { @const = "rename" } } },
                then = new { required = new[] { "preset_id", "name" } } },
            new { @if = new { properties = new { operation = new { @const = "update" } } },
                then = new { required = new[] { "preset_id" } } },
            new { @if = new { properties = new { operation = new { @const = "delete" } } },
                then = new { required = new[] { "preset_id" } } },
            new { @if = new { properties = new { operation = new { @const = "reorder" } } },
                then = new { required = new[] { "preset_ids" } } }
        },
        additionalProperties = false
    };

    private static object SelectionBoundsSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["min_x"] = Number("Minimum X"), ["min_y"] = Number("Minimum Y"),
            ["max_x"] = Number("Maximum X"), ["max_y"] = Number("Maximum Y"),
            ["mode"] = new { type = "string", @enum = Enum.GetNames<CadSelectionMode>() },
            ["require_contained"] = new { type = "boolean", description = "Select only entities fully contained by the bounds." }
        },
        required = new[] { "min_x", "min_y", "max_x", "max_y" },
        additionalProperties = false
    };

    private static object SelectionPolygonSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["points"] = new
            {
                type = "array",
                minItems = 3,
                items = new { type = "object", properties = new { x = Number("X"), y = Number("Y") }, required = new[] { "x", "y" }, additionalProperties = false }
            },
            ["mode"] = new { type = "string", @enum = Enum.GetNames<CadSelectionMode>() },
            ["require_contained"] = new { type = "boolean" }
        },
        required = new[] { "points" },
        additionalProperties = false
    };

    private static object SelectionFilterSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["type"] = new { type = "string", description = "Entity type, such as Line, Circle, Polyline, Text, or BlockReference." },
            ["layer"] = String("Existing layer name or ID"),
            ["name_contains"] = String("Case-insensitive entity-name fragment"),
            ["visible"] = new { type = "boolean" },
            ["locked"] = new { type = "boolean" },
            ["mode"] = new { type = "string", @enum = Enum.GetNames<CadSelectionMode>() }
        },
        anyOf = new[]
        {
            new { required = new[] { "type" } },
            new { required = new[] { "layer" } },
            new { required = new[] { "name_contains" } },
            new { required = new[] { "visible" } },
            new { required = new[] { "locked" } }
        },
        additionalProperties = false
    };

    private static object MeasurementSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["operation"] = new { type = "string", @enum = new[] { "measure", "intersections", "nearest_point", "project_point", "chord" } },
            ["entity_ids"] = EntityIdArray(),
            ["point"] = new
            {
                type = "object",
                properties = new { x = Number("X"), y = Number("Y") },
                required = new[] { "x", "y" },
                additionalProperties = false
            },
            ["points"] = new
            {
                type = "array",
                minItems = 2,
                items = new { type = "object", properties = new { x = Number("X"), y = Number("Y") }, required = new[] { "x", "y" }, additionalProperties = false }
            },
            ["start_angle_degrees"] = Number("Circle chord start angle"),
            ["end_angle_degrees"] = Number("Circle chord end angle")
        },
        additionalProperties = false
    };
}
