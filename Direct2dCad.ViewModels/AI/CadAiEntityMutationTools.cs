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
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.AI;

internal sealed class CadAiEntityMutationTools(
    CadDocument document,
    Func<JsonElement, EntityId[]> resolveEntityIds,
    Action<ICadCommand> executeCommand,
    Action<IReadOnlyList<EntityId>> selectEntities)
{
    private const int MaximumEmbeddedDataBytes = 16 * 1024 * 1024;
    private static readonly string[] TextPropertyFields =
    [
        "text", "text_style", "shape_font", "inverted", "inverted_margin_factor"
    ];

    internal static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        Tool("get_entity_properties",
            "Read common and type-specific properties for one or more entities before changing them.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema()
            }, ["entity_ids"])),
        Tool("set_text_properties",
            "Set shared content and type-specific Text or ShapeText properties as one undoable batch. Omitted fields are preserved.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema(),
                ["text"] = new { type = "string" },
                ["text_style"] = StringSchema("CadText style name or ID; use none to clear"),
                ["shape_font"] = StringSchema("CadShapeText shape-font name or ID"),
                ["inverted"] = new { type = "boolean" },
                ["inverted_margin_factor"] = new { type = "number", minimum = 0.0 }
            }, ["entity_ids"])),
        Tool("set_block_reference_definition",
            "Change one or more BlockReference entities to the same existing user Block definition.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema(),
                ["block"] = StringSchema("Existing user Block definition name or ID")
            }, ["entity_ids", "block"])),
        Tool("set_block_definition_base_point",
            "Change an existing user Block definition base point with undo/redo.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["block"] = StringSchema("Existing user Block definition name or ID"),
                ["base_x"] = NumberSchema(),
                ["base_y"] = NumberSchema()
            }, ["block", "base_x", "base_y"])),
        Tool("replace_embedded_entity_data",
            "Replace one Image's raw BGRA32 pixels or one OLE object's persisted bytes. Use only when binary content was explicitly supplied; data is limited to 16 MiB.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema(),
                ["data_base64"] = StringSchema("Base64 raw BGRA32 pixels for Image, or persisted OLE bytes for OleObject"),
                ["pixel_width"] = new { type = "integer", minimum = 1 },
                ["pixel_height"] = new { type = "integer", minimum = 1 },
                ["stride"] = new { type = "integer", minimum = 4 },
                ["content_type"] = StringSchema("Optional replacement content type"),
                ["source_name"] = StringSchema("Optional replacement source name")
            }, ["entity_ids", "data_base64"]))
    ];

    internal object Execute(string toolName, JsonElement arguments) => toolName switch
    {
        "get_entity_properties" => GetEntityProperties(arguments),
        "set_text_properties" => SetTextProperties(arguments),
        "set_block_reference_definition" => SetBlockReferenceDefinition(arguments),
        "set_block_definition_base_point" => SetBlockDefinitionBasePoint(arguments),
        "replace_embedded_entity_data" => ReplaceEmbeddedEntityData(arguments),
        _ => throw new ArgumentException($"Unknown entity mutation tool: {toolName}")
    };

    private object GetEntityProperties(JsonElement arguments)
    {
        var ids = resolveEntityIds(arguments);
        return new
        {
            entities = ids.Select(id => EntityDto(document.GetEntity(id))).ToArray()
        };
    }

    private object SetTextProperties(JsonElement arguments)
    {
        var ids = resolveEntityIds(arguments);
        var entities = ids.Select(document.GetEntity).ToArray();
        if (entities.Any(entity => entity is not (CadText or CadShapeText)))
            throw new NotSupportedException("All entity_ids must refer to Text or ShapeText entities.");

        var changedFields = TextPropertyFields.Where(field => HasValue(arguments, field)).ToArray();
        if (changedFields.Length == 0)
            throw new ArgumentException("At least one text property must be supplied.");

        if (HasValue(arguments, "text_style") && entities.Any(entity => entity is not CadText))
            throw new NotSupportedException("text_style is supported only when every entity is CadText.");
        if (HasValue(arguments, "shape_font") && entities.Any(entity => entity is not CadShapeText))
            throw new NotSupportedException("shape_font is supported only when every entity is CadShapeText.");

        if (HasValue(arguments, "text"))
        {
            var value = RequiredStringValue(arguments, "text");
            foreach (var entity in entities)
            {
                executeCommand(entity switch
                {
                    CadText => new SetTextContentCommand(entity.Id, value),
                    CadShapeText => new SetShapeTextContentCommand(entity.Id, value),
                    _ => throw new UnreachableException()
                });
            }
        }

        if (HasValue(arguments, "text_style"))
        {
            var styleId = ResolveTextStyle(RequiredString(arguments, "text_style"));
            foreach (var id in ids)
                executeCommand(new SetTextStyleCommand(id, styleId));
        }

        if (HasValue(arguments, "shape_font"))
        {
            var shapeFontId = ResolveShapeFont(RequiredString(arguments, "shape_font"));
            executeCommand(new SetShapeTextFontCommand(ids, shapeFontId));
        }

        if (HasValue(arguments, "inverted"))
            executeCommand(new SetTextInvertedCommand(ids, RequiredBoolean(arguments, "inverted")));

        if (HasValue(arguments, "inverted_margin_factor"))
        {
            var margin = RequiredFinite(arguments, "inverted_margin_factor");
            if (margin < 0)
                throw new ArgumentOutOfRangeException("inverted_margin_factor");
            executeCommand(new SetTextInvertedMarginFactorCommand(ids, margin));
        }

        selectEntities(ids);
        return new
        {
            entity_ids = ids.Select(id => id.Value).ToArray(),
            changed_fields = changedFields
        };
    }

    private object SetBlockReferenceDefinition(JsonElement arguments)
    {
        var ids = resolveEntityIds(arguments);
        if (ids.Select(document.GetEntity).Any(entity => entity is not CadBlockReference))
            throw new NotSupportedException("All entity_ids must refer to BlockReference entities.");

        var definition = ResolveUserBlock(RequiredString(arguments, "block"));
        foreach (var id in ids)
            executeCommand(new SetBlockReferenceDefinitionCommand(id, definition.Id));

        selectEntities(ids);
        return new
        {
            entity_ids = ids.Select(id => id.Value).ToArray(),
            definition_block_id = definition.Id.Value,
            definition_name = definition.Name
        };
    }

    private object SetBlockDefinitionBasePoint(JsonElement arguments)
    {
        var definition = ResolveUserBlock(RequiredString(arguments, "block"));
        var basePoint = new CadPointD(
            RequiredFinite(arguments, "base_x"),
            RequiredFinite(arguments, "base_y"));
        executeCommand(new SetBlockDefinitionBasePointCommand(definition.Id, basePoint));
        return new
        {
            block_id = definition.Id.Value,
            block_name = definition.Name,
            base_point = new { x = basePoint.X, y = basePoint.Y }
        };
    }

    private object ReplaceEmbeddedEntityData(JsonElement arguments)
    {
        var ids = resolveEntityIds(arguments);
        if (ids.Length != 1)
            throw new ArgumentException("replace_embedded_entity_data requires exactly one entity_id.");

        var entity = document.GetEntity(ids[0]);
        if (entity is not (CadImage or CadOleObject))
            throw new NotSupportedException("The entity must be an Image or OleObject.");

        var data = DecodeBase64(RequiredString(arguments, "data_base64"));
        var contentType = OptionalString(arguments, "content_type");
        var sourceName = OptionalString(arguments, "source_name");

        switch (entity)
        {
            case CadImage image:
            {
                var pixelWidth = RequiredPositiveInteger(arguments, "pixel_width");
                var pixelHeight = RequiredPositiveInteger(arguments, "pixel_height");
                var minimumStride = checked(pixelWidth * 4);
                var stride = OptionalPositiveInteger(arguments, "stride", minimumStride);
                if (stride < minimumStride)
                    throw new ArgumentOutOfRangeException("stride", $"stride must be at least {minimumStride}.");
                var expectedLength = checked(stride * pixelHeight);
                if (data.Length != expectedLength)
                    throw new ArgumentException($"Image data must contain exactly stride * pixel_height bytes ({expectedLength}).");

                executeCommand(new SetImageDataCommand(
                    image.Id,
                    pixelWidth,
                    pixelHeight,
                    stride,
                    data,
                    contentType ?? image.ContentType,
                    sourceName ?? image.SourceName));
                break;
            }
            case CadOleObject oleObject:
                executeCommand(new SetOleObjectDataCommand(
                    oleObject.Id,
                    data,
                    contentType ?? oleObject.ContentType,
                    sourceName ?? oleObject.SourceName));
                break;
        }

        selectEntities(ids);
        return new
        {
            entity_id = ids[0].Value,
            type = EntityType(entity),
            byte_count = data.Length
        };
    }

    private object EntityDto(CadEntity entity) => new
    {
        id = entity.Id.Value,
        type = EntityType(entity),
        entity.Name,
        layer_id = entity.LayerId.Value,
        owner_block_id = entity.OwnerBlockId.Value,
        entity.IsLocked,
        entity.IsVisible,
        color_source = ProtocolEnum(entity.ColorSource),
        line_weight = entity.UseLayerLineWeight ? (object)"by_layer" : entity.LineWeight?.Value,
        entity.ZIndex,
        graphic_style_id = GraphicStyleId(entity)?.Value,
        fill_style_id = FillStyleId(entity)?.Value,
        stroke_style = new
        {
            start_cap = ProtocolEnum(entity.StrokeStyle.StartCap),
            end_cap = ProtocolEnum(entity.StrokeStyle.EndCap),
            dash_cap = ProtocolEnum(entity.StrokeStyle.DashCap),
            dash_style = ProtocolEnum(entity.StrokeStyle.DashStyle),
            line_join = ProtocolEnum(entity.StrokeStyle.LineJoin)
        },
        capabilities = CapabilityNames(entity),
        specific = SpecificProperties(entity)
    };

    private object? SpecificProperties(CadEntity entity) => entity switch
    {
        CadText text => new
        {
            text.Text,
            text_style_id = text.TextStyleId?.Value,
            text_style_name = StyleName(text.TextStyleId),
            text.IsInverted,
            inverted_margin_factor = text.InvertedMarginFactor
        },
        CadShapeText text => new
        {
            text.Text,
            shape_font_id = text.ShapeFontId.Value,
            shape_font_name = CadShapeFontRegistry.GetOrDefault(text.ShapeFontId).Name,
            text.IsInverted,
            inverted_margin_factor = text.InvertedMarginFactor
        },
        CadImage image => new
        {
            image.Opacity,
            image.PixelWidth,
            image.PixelHeight,
            image.Stride,
            image.ContentType,
            image.SourceName,
            rotation_degrees = image.RotationRadians * 180.0 / Math.PI
        },
        CadOleObject oleObject => new
        {
            oleObject.Opacity,
            byte_count = oleObject.OleMemory.Length,
            oleObject.ContentType,
            oleObject.SourceName
        },
        CadBlockReference reference => new
        {
            definition_block_id = reference.DefinitionBlockId.Value,
            definition_name = document.TryGetBlock(reference.DefinitionBlockId, out var definition)
                ? definition?.Name
                : null
        },
        _ => null
    };

    private StyleId? ResolveTextStyle(string value)
    {
        if (IsNone(value))
            return null;

        var style = document.Styles.Values.OfType<CadTextStyle>().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal));
        return style?.Id ?? throw new ArgumentException($"Text style not found: {value}");
    }

    private static CadShapeFontId ResolveShapeFont(string value)
    {
        var font = CadShapeFontRegistry.Defaults.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.Value, value, StringComparison.OrdinalIgnoreCase));
        return font?.Id ?? throw new ArgumentException($"Shape font not found: {value}");
    }

    private CadBlockDefinition ResolveUserBlock(string value)
    {
        var block = document.Blocks.Values.FirstOrDefault(candidate =>
            candidate.Kind == CadBlockKind.User &&
            (string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(candidate.Id.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal)));
        return block ?? throw new ArgumentException($"User Block definition not found: {value}");
    }

    private string? StyleName(StyleId? styleId) =>
        styleId is { } id && document.TryGetStyle(id, out var style) ? style?.Name : null;

    private static string[] CapabilityNames(CadEntity entity)
    {
        var capabilities = CadEntityCapabilities.GetCapabilities(entity);
        return Enum.GetValues<CadEntityCapability>()
            .Where(value => value != CadEntityCapability.None && capabilities.HasFlag(value))
            .Select(ProtocolEnum)
            .ToArray();
    }

    private static StyleId? GraphicStyleId(CadEntity entity) => entity switch
    {
        CadLine value => value.GraphicStyleId,
        CadCircle value => value.GraphicStyleId,
        CadEllipse value => value.GraphicStyleId,
        CadEllipseArc value => value.GraphicStyleId,
        CadRectangle value => value.GraphicStyleId,
        CadArc value => value.GraphicStyleId,
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

    private static string EntityType(CadEntity entity) => entity switch
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

    private static byte[] DecodeBase64(string value)
    {
        var maximumEncodedLength = ((MaximumEmbeddedDataBytes + 2) / 3) * 4;
        if (value.Length > maximumEncodedLength)
            throw new ArgumentException($"Embedded data exceeds the {MaximumEmbeddedDataBytes} byte limit.");

        try
        {
            var result = Convert.FromBase64String(value);
            return result.Length <= MaximumEmbeddedDataBytes
                ? result
                : throw new ArgumentException($"Embedded data exceeds the {MaximumEmbeddedDataBytes} byte limit.");
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("data_base64 is not valid Base64.", nameof(value), exception);
        }
    }

    private static bool RequiredBoolean(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException($"{name} must be a boolean.");
        }
        return value.GetBoolean();
    }

    private static double RequiredFinite(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            double.IsNaN(result) || double.IsInfinity(result))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }
        return result;
    }

    private static int RequiredPositiveInteger(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result <= 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return result;
    }

    private static int OptionalPositiveInteger(JsonElement arguments, string name, int fallback)
    {
        if (!HasValue(arguments, name))
            return fallback;
        return RequiredPositiveInteger(arguments, name);
    }

    private static string RequiredString(JsonElement arguments, string name)
    {
        var value = RequiredStringValue(arguments, name).Trim();
        return value.Length > 0 ? value : throw new ArgumentException($"{name} cannot be empty.");
    }

    private static string RequiredStringValue(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"{name} must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static string? OptionalString(JsonElement arguments, string name) =>
        HasValue(arguments, name) ? RequiredStringValue(arguments, name) : null;

    private static bool HasValue(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static bool IsNone(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static string ProtocolEnum<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    private static object ObjectSchema(
        IReadOnlyDictionary<string, object> properties,
        IReadOnlyList<string>? required = null) => new
    {
        type = "object",
        properties,
        required = required ?? [],
        additionalProperties = false
    };

    private static object DocumentIdSchema() =>
        StringSchema("Stable open-document ID from list_documents");

    private static object EntityIdsSchema() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "integer" }
    };

    private static object StringSchema(string description) => new { type = "string", description };
    private static object NumberSchema() => new { type = "number" };

    private static AiToolDefinition Tool(string name, string description, object parameters) =>
        new(name, description, JsonSerializer.SerializeToElement(parameters));
}
