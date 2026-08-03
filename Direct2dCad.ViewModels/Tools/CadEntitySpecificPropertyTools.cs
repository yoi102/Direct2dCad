using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Text;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadEntitySpecificPropertyTools
{
    private const double Epsilon = 1e-9;
    private static readonly string[] SpecificPropertyNames =
    [
        "text_style", "font_family", "shape_font", "inverted", "inverted_margin_factor"
    ];

    internal static AiToolDefinition ToolDefinition { get; } = new(
        "set_entity_specific_properties",
        "Set undoable type-specific properties. Supports text content/font/inversion, shape font, image or OLE opacity, and BlockReference definition. Omitted properties are preserved.",
        JsonSerializer.SerializeToElement(CreateSchema()));

    internal static object Execute(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var document = executor.DocumentViewModel.CadEditor.Document;
        var entities = ids.Select(document.GetEntity).ToArray();
        foreach (var entity in entities)
            CadEntityAccessPolicy.EnsureEditable(document, entity);

        var hasText = HasValue(arguments, "text");
        var hasTextStyle = HasValue(arguments, "text_style");
        var hasFontFamily = HasValue(arguments, "font_family");
        var hasShapeFont = HasValue(arguments, "shape_font");
        var hasInverted = HasValue(arguments, "inverted");
        var hasMargin = HasValue(arguments, "inverted_margin_factor");
        var hasOpacity = HasValue(arguments, "opacity");
        var hasBlockDefinition = HasValue(arguments, "block_definition");

        if (!hasText && !hasTextStyle && !hasFontFamily && !hasShapeFont &&
            !hasInverted && !hasMargin && !hasOpacity && !hasBlockDefinition)
        {
            throw new ArgumentException("At least one type-specific property must be supplied.");
        }
        if (hasTextStyle && hasFontFamily)
            throw new ArgumentException("text_style and font_family cannot be set in the same call.");

        ValidateTypes(
            entities,
            hasText,
            hasTextStyle || hasFontFamily,
            hasShapeFont,
            hasInverted || hasMargin,
            hasOpacity,
            hasBlockDefinition);

        var changed = new List<string>();
        if (hasText)
        {
            var value = RequiredString(arguments, "text", allowEmpty: true);
            foreach (var entity in entities)
            {
                executor.ExecuteCommand(entity switch
                {
                    CadText => new SetTextContentCommand(entity.Id, value),
                    CadShapeText => new SetShapeTextContentCommand(entity.Id, value),
                    _ => throw new UnreachableException()
                });
            }
            changed.Add("text");
        }

        if (hasTextStyle || hasFontFamily)
        {
            var styleId = hasTextStyle
                ? ResolveTextStyle(document, RequiredString(arguments, "text_style"))
                : ResolveOrCreateFontStyle(executor, document, RequiredString(arguments, "font_family"));
            foreach (var entity in entities)
                executor.ExecuteCommand(new SetTextStyleCommand(entity.Id, styleId));
            changed.Add(hasTextStyle ? "text_style" : "font_family");
        }

        if (hasShapeFont)
        {
            var shapeFont = ResolveShapeFont(RequiredString(arguments, "shape_font"));
            executor.ExecuteCommand(new SetShapeTextFontCommand(ids, shapeFont.Id));
            changed.Add("shape_font");
        }

        if (hasInverted)
        {
            executor.ExecuteCommand(new SetTextInvertedCommand(ids, RequiredBool(arguments, "inverted")));
            changed.Add("inverted");
        }

        if (hasMargin)
        {
            executor.ExecuteCommand(new SetTextInvertedMarginFactorCommand(
                ids,
                RequiredNonNegative(arguments, "inverted_margin_factor")));
            changed.Add("inverted_margin_factor");
        }

        if (hasOpacity)
        {
            executor.ExecuteCommand(new SetEntityOpacityCommand(ids, RequiredUnitInterval(arguments, "opacity")));
            changed.Add("opacity");
        }

        if (hasBlockDefinition)
        {
            var block = ResolveUserBlock(document, RequiredString(arguments, "block_definition"));
            foreach (var entity in entities)
                executor.ExecuteCommand(new SetBlockReferenceDefinitionCommand(entity.Id, block.Id));
            changed.Add("block_definition");
        }

        executor.DocumentViewModel.SelectEntities(ids);
        return new
        {
            entity_ids = ids.Select(id => id.Value).ToArray(),
            changed_fields = changed
        };
    }

    internal static void ValidateCreationArguments(
        CadDocument document,
        string toolName,
        JsonElement arguments)
    {
        var allowed = toolName switch
        {
            "add_text" => new HashSet<string>(
                ["text_style", "font_family", "inverted", "inverted_margin_factor"],
                StringComparer.Ordinal),
            "add_shape_text" => new HashSet<string>(
                ["shape_font", "inverted", "inverted_margin_factor"],
                StringComparer.Ordinal),
            _ => []
        };

        foreach (var property in SpecificPropertyNames)
        {
            if (HasValue(arguments, property) && !allowed.Contains(property))
                throw new NotSupportedException($"{toolName} does not support {property}.");
        }

        if (HasValue(arguments, "text_style") && HasValue(arguments, "font_family"))
            throw new ArgumentException("text_style and font_family cannot be set in the same call.");
        if (HasValue(arguments, "text_style"))
            _ = ResolveTextStyle(document, RequiredString(arguments, "text_style"));
        if (HasValue(arguments, "font_family"))
            _ = RequiredString(arguments, "font_family");
        if (HasValue(arguments, "shape_font"))
            _ = ResolveShapeFont(RequiredString(arguments, "shape_font"));
        if (HasValue(arguments, "inverted"))
            _ = RequiredBool(arguments, "inverted");
        if (HasValue(arguments, "inverted_margin_factor"))
            _ = RequiredNonNegative(arguments, "inverted_margin_factor");
    }

    private static void ValidateTypes(
        IReadOnlyList<CadEntity> entities,
        bool text,
        bool textStyle,
        bool shapeFont,
        bool inverted,
        bool opacity,
        bool blockDefinition)
    {
        if (text && entities.Any(entity => entity is not (CadText or CadShapeText)))
            throw new NotSupportedException("text is only supported by Text and ShapeText entities.");
        if (textStyle && entities.Any(entity => entity is not CadText))
            throw new NotSupportedException("text_style and font_family are only supported by Text entities.");
        if (shapeFont && entities.Any(entity => entity is not CadShapeText))
            throw new NotSupportedException("shape_font is only supported by ShapeText entities.");
        if (inverted && entities.Any(entity => entity is not (CadText or CadShapeText)))
            throw new NotSupportedException("inverted properties are only supported by text entities.");
        if (opacity && entities.Any(entity => entity is not (CadImage or CadOleObject)))
            throw new NotSupportedException("opacity is only supported by Image and OleObject entities.");
        if (blockDefinition && entities.Any(entity => entity is not CadBlockReference))
            throw new NotSupportedException("block_definition is only supported by BlockReference entities.");
    }

    private static StyleId? ResolveTextStyle(CadDocument document, string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var style = document.Styles.Values.OfType<CadTextStyle>().FirstOrDefault(candidate =>
            candidate.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            candidate.Id.Value.ToString(CultureInfo.InvariantCulture).Equals(value, StringComparison.Ordinal));
        return style?.Id ?? throw new ArgumentException($"Text style not found: {value}");
    }

    private static StyleId? ResolveOrCreateFontStyle(
        CadDocumentToolExecutor executor,
        CadDocument document,
        string fontFamily)
    {
        if (fontFamily.Equals("Meiryo", StringComparison.OrdinalIgnoreCase))
            return null;

        var existing = document.Styles.Values.OfType<CadTextStyle>().FirstOrDefault(style =>
            style.FontFamily.Equals(fontFamily, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(style.TextHeight - 1.0) <= Epsilon &&
            Math.Abs(style.WidthFactor - 1.0) <= Epsilon &&
            Math.Abs(style.ObliqueAngle) <= Epsilon &&
            !style.IsBold &&
            !style.IsItalic);
        if (existing is not null)
            return existing.Id;

        var baseName = $"Font - {fontFamily}";
        var names = document.Styles.Values.Select(style => style.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        for (var suffix = 2; names.Contains(name); suffix++)
            name = $"{baseName} ({suffix})";
        var command = new CreateTextStyleCommand(name, fontFamily, textHeight: 1.0);
        executor.ExecuteCommand(command);
        return command.CreatedStyleId
            ?? throw new InvalidOperationException("The text style was not created.");
    }

    private static CadShapeFont ResolveShapeFont(string value) =>
        CadShapeFontRegistry.Defaults.FirstOrDefault(font =>
            font.Id.Value.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            font.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) ??
        throw new ArgumentException($"Shape font not found: {value}");

    private static CadBlockDefinition ResolveUserBlock(CadDocument document, string value)
    {
        CadBlockDefinition? block = null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            document.Blocks.TryGetValue(new BlockId(id), out block);
        block ??= document.Blocks.Values.FirstOrDefault(candidate =>
            candidate.Kind == CadBlockKind.User &&
            candidate.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
        return block is { Kind: CadBlockKind.User }
            ? block
            : throw new ArgumentException($"User block not found: {value}");
    }

    private static object CreateSchema() => new
    {
        type = "object",
        properties = new
        {
            document_id = new { type = "string", description = "Stable open-document ID" },
            entity_ids = new
            {
                type = "array",
                minItems = 1,
                uniqueItems = true,
                items = new { type = "integer" }
            },
            text = new { type = "string", description = "Text or ShapeText content; empty is allowed" },
            text_style = new { type = "string", description = "Existing Text style name or ID; none selects the default" },
            font_family = new { type = "string", minLength = 1, description = "Text font family; creates or reuses a matching Text style" },
            shape_font = new { type = "string", description = "Shape font ID or name: unicode, simplex, monoline, or box-fallback" },
            inverted = new { type = "boolean" },
            inverted_margin_factor = new { type = "number", minimum = 0.0 },
            opacity = new { type = "number", minimum = 0.0, maximum = 1.0 },
            block_definition = new { type = "string", description = "Existing user Block name or ID" }
        },
        required = new[] { "entity_ids" },
        anyOf = new[]
        {
            new { required = new[] { "text" } },
            new { required = new[] { "text_style" } },
            new { required = new[] { "font_family" } },
            new { required = new[] { "shape_font" } },
            new { required = new[] { "inverted" } },
            new { required = new[] { "inverted_margin_factor" } },
            new { required = new[] { "opacity" } },
            new { required = new[] { "block_definition" } }
        },
        not = new
        {
            required = new[] { "text_style", "font_family" }
        },
        additionalProperties = false
    };

    private static bool HasValue(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string RequiredString(JsonElement arguments, string name, bool allowEmpty = false)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"{name} must be a string.");
        var text = value.GetString() ?? string.Empty;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{name} cannot be empty.");
        return allowEmpty ? text : text.Trim();
    }

    private static bool RequiredBool(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException($"{name} must be a boolean.");
        }
        return value.GetBoolean();
    }

    private static double RequiredNonNegative(JsonElement arguments, string name)
    {
        var value = RequiredFinite(arguments, name);
        return value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
    }

    private static double RequiredUnitInterval(JsonElement arguments, string name)
    {
        var value = RequiredFinite(arguments, name);
        return value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(name);
    }

    private static double RequiredFinite(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }
        return result;
    }
}
