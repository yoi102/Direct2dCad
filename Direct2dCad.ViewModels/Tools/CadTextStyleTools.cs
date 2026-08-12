using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadTextStyleTools
{
    internal static AiToolDefinition ToolDefinition { get; } = new(
        "set_text_style_properties",
        "Change an existing shared Text style as one undoable operation. All CadText entities using the style are remeasured.",
        JsonSerializer.SerializeToElement(CreateSchema()));

    internal static object Execute(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var style = ResolveStyle(document, RequiredString(arguments, "text_style"));
        var changed = new List<string>();

        var fontFamily = OptionalString(arguments, "font_family");
        var textHeight = OptionalPositive(arguments, "text_height");
        var widthFactor = OptionalPositive(arguments, "width_factor");
        var hasObliqueAngle = HasValue(arguments, "oblique_angle_degrees");
        var obliqueAngle = hasObliqueAngle
            ? OptionalFinite(arguments, "oblique_angle_degrees") * Math.PI / 180.0
            : 0.0;
        var isBold = OptionalBool(arguments, "bold");
        var isItalic = OptionalBool(arguments, "italic");

        if (fontFamily is not null) changed.Add("font_family");
        if (textHeight is not null) changed.Add("text_height");
        if (widthFactor is not null) changed.Add("width_factor");
        if (hasObliqueAngle) changed.Add("oblique_angle_degrees");
        if (isBold is not null) changed.Add("bold");
        if (isItalic is not null) changed.Add("italic");
        if (changed.Count == 0)
            throw new ArgumentException("At least one text style property must be supplied.");

        var command = new SetTextStylePropertiesCommand(
            style.Id,
            fontFamily,
            textHeight,
            widthFactor,
            hasObliqueAngle ? obliqueAngle : null,
            isBold,
            isItalic);
        executor.ExecuteCommand(command);

        var affectedEntityIds = document.Entities.Values
            .Where(entity => !entity.IsErased &&
                             entity is CadText text &&
                             text.TextStyleId == style.Id)
            .Select(entity => entity.Id.Value)
            .ToArray();

        executor.DocumentViewModel.SelectEntities(affectedEntityIds.Select(id => new EntityId(id)));
        return new
        {
            text_style_id = style.Id.Value,
            text_style = style.Name,
            changed_fields = changed,
            affected_entity_ids = affectedEntityIds
        };
    }

    private static CadTextStyle ResolveStyle(CadDocument document, string value)
    {
        var style = document.Styles.Values
            .OfType<CadTextStyle>()
            .FirstOrDefault(candidate =>
                candidate.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                candidate.Id.Value.ToString(CultureInfo.InvariantCulture).Equals(value, StringComparison.Ordinal));
        return style ?? throw new ArgumentException($"Text style not found: {value}");
    }

    private static object CreateSchema() => new
    {
        type = "object",
        properties = new
        {
            document_id = new { type = "string", description = "Stable open-document ID" },
            text_style = new { type = "string", description = "Existing Text style name or ID" },
            font_family = new { type = "string", minLength = 1 },
            text_height = new { type = "number", exclusiveMinimum = 0.0 },
            width_factor = new { type = "number", exclusiveMinimum = 0.0 },
            oblique_angle_degrees = new { type = "number", description = "Text style oblique angle in degrees" },
            bold = new { type = "boolean" },
            italic = new { type = "boolean" }
        },
        required = new[] { "text_style" },
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

    private static string? OptionalString(JsonElement arguments, string name) =>
        HasValue(arguments, name) && arguments.GetProperty(name).ValueKind == JsonValueKind.String
            ? string.IsNullOrWhiteSpace(arguments.GetProperty(name).GetString())
                ? throw new ArgumentException($"{name} cannot be empty.")
                : arguments.GetProperty(name).GetString()!.Trim()
            : null;

    private static double? OptionalPositive(JsonElement arguments, string name)
    {
        if (!HasValue(arguments, name))
            return null;
        var value = arguments.GetProperty(name);
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result) || result <= 0)
            throw new ArgumentException($"{name} must be a finite number greater than zero.");
        return result;
    }

    private static double OptionalFinite(JsonElement arguments, string name)
    {
        var value = arguments.GetProperty(name);
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static bool? OptionalBool(JsonElement arguments, string name)
    {
        if (!HasValue(arguments, name))
            return null;
        var value = arguments.GetProperty(name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException($"{name} must be a boolean.");
        return value.GetBoolean();
    }
}
