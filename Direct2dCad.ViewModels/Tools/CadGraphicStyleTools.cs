using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadGraphicStyleTools
{
    internal static AiToolDefinition ToolDefinition { get; } = new(
        "set_graphic_style_properties",
        "Change an existing shared Graphic style as one undoable operation. All entities and layers using it are reported as affected.",
        JsonSerializer.SerializeToElement(CreateSchema()));

    internal static object Execute(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var style = ResolveStyle(document, RequiredString(arguments, "graphic_style"));
        var color = HasValue(arguments, "color")
            ? CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "color"))
            : (CadColor?)null;
        var lineWeight = HasValue(arguments, "line_weight")
            ? ParseLineWeight(arguments.GetProperty("line_weight"))
            : (CadLineWeight?)null;
        var lineTypeId = HasValue(arguments, "line_type_id")
            ? (LineTypeId?)ParseLineTypeId(document, arguments)
            : (LineTypeId?)null;

        var changed = new List<string>();
        if (color is not null) changed.Add("color");
        if (lineWeight is not null) changed.Add("line_weight");
        if (lineTypeId is not null) changed.Add("line_type_id");
        if (changed.Count == 0)
            throw new ArgumentException("At least one graphic style property must be supplied.");

        executor.ExecuteCommand(new SetGraphicStylePropertiesCommand(style.Id, color, lineWeight, lineTypeId));
        return new
        {
            graphic_style_id = style.Id.Value,
            graphic_style = style.Name,
            changed_fields = changed,
            color = color is { } parsedColor ? ColorText(parsedColor) : null,
            line_weight = (object?) (lineWeight is { } parsedWeight
                ? parsedWeight.IsByLayer ? "by_layer" : parsedWeight.Value
                : null),
            line_type_id = lineTypeId?.Value
        };
    }

    private static CadGraphicStyle ResolveStyle(CadDocument document, string value)
    {
        var style = document.Styles.Values.OfType<CadGraphicStyle>().FirstOrDefault(candidate =>
            candidate.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            candidate.Id.Value.ToString(CultureInfo.InvariantCulture).Equals(value, StringComparison.Ordinal));
        return style ?? throw new ArgumentException($"Graphic style not found: {value}");
    }

    private static CadLineWeight ParseLineWeight(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), "by_layer", StringComparison.OrdinalIgnoreCase))
        {
            return CadLineWeight.ByLayer;
        }

        if (!value.TryGetDouble(out var weight) || !double.IsFinite(weight) || weight <= 0)
            throw new ArgumentException("line_weight must be 'by_layer' or a finite number greater than zero.");
        return new CadLineWeight(weight);
    }

    private static long RequiredLong(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result) || result <= 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return result;
    }

    private static LineTypeId ParseLineTypeId(CadDocument document, JsonElement arguments)
    {
        var value = RequiredLong(arguments, "line_type_id");
        var lineTypeId = new LineTypeId(value);
        if (!document.LineTypes.ContainsKey(lineTypeId))
            throw new ArgumentException($"Line type not found: {value}.");
        return lineTypeId;
    }

    private static object CreateSchema() => new
    {
        type = "object",
        properties = new
        {
            document_id = new { type = "string", description = "Stable open-document ID" },
            graphic_style = new { type = "string", description = "Existing Graphic style name or ID" },
            color = new { type = "string", description = "Stroke color" },
            line_weight = new
            {
                oneOf = new object[]
                {
                    new { type = "number", exclusiveMinimum = 0.0 },
                    new { type = "string", @enum = new[] { "by_layer" } }
                }
            },
            line_type_id = new
            {
                type = "integer",
                minimum = 1,
                description = "Existing document line type ID. Use list_styles before assigning a custom line type."
            }
        },
        required = new[] { "graphic_style" },
        additionalProperties = false
    };

    private static bool HasValue(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string RequiredString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"{name} is required.");
        }
        return value.GetString()!.Trim();
    }

    private static string ColorText(CadColor color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
