using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.ViewModels.AI;

internal sealed class CadAiLayerTools(
    CadDocument document,
    Action<ICadCommand> executeCommand)
{
    internal static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        Tool("list_layers", "List layers in effective drawing order, highest priority first.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema()
            })),
        Tool("create_layer", "Create an undoable drawing layer. Layer names must be unique.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["name"] = StringSchema("Unique layer name"),
                ["color"] = ColorSchema(),
                ["line_weight"] = LineWeightSchema(),
                ["drawing_priority"] = new { type = "integer" }
            }, ["name"])),
        Tool("rename_layer", "Rename an existing layer with an undoable command.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["layer"] = LayerSchema(),
                ["new_name"] = StringSchema("New unique layer name")
            }, ["layer", "new_name"])),
        Tool("delete_layer", "Delete a layer and all entities on it with an undoable command. A document must retain at least one layer.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["layer"] = LayerSchema(),
                ["delete_entities"] = new
                {
                    type = "boolean",
                    @const = true,
                    description = "Must be true to explicitly confirm deletion of every entity on the layer"
                }
            }, ["layer", "delete_entities"])),
        Tool("set_layer_properties", "Set layer appearance, visibility, lock, frozen state, or drawing priority with undoable commands. Omitted properties are preserved.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["layer"] = LayerSchema(),
                ["color"] = ColorSchema(),
                ["line_weight"] = LineWeightSchema(),
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" },
                ["frozen"] = new { type = "boolean" },
                ["drawing_priority"] = new { type = "integer" }
            }, ["layer"])),
        Tool("reorder_layers", "Replace the complete layer drawing order. Supply every layer exactly once, highest priority first.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["layer_order"] = new
                {
                    type = "array",
                    minItems = 1,
                    uniqueItems = true,
                    items = LayerSchema()
                }
            }, ["layer_order"]))
    ];

    internal object Execute(string toolName, JsonElement arguments) => toolName switch
    {
        "list_layers" => ListLayers(),
        "create_layer" => CreateLayer(arguments),
        "rename_layer" => RenameLayer(arguments),
        "delete_layer" => DeleteLayer(arguments),
        "set_layer_properties" => SetLayerProperties(arguments),
        "reorder_layers" => ReorderLayers(arguments),
        _ => throw new ArgumentException($"Unknown layer tool: {toolName}")
    };

    private object ListLayers() => new
    {
        count = document.Layers.Count,
        layers = OrderedLayers().Select(LayerDto).ToArray()
    };

    private object CreateLayer(JsonElement arguments)
    {
        var name = RequiredString(arguments, "name");
        var color = HasValue(arguments, "color")
            ? CadAiWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "color"))
            : CadColor.Green;
        var lineWeight = arguments.TryGetProperty("line_weight", out var lineWeightElement)
            ? ParseLineWeight(lineWeightElement)
            : CadLineWeight.Default;
        var drawingPriority = OptionalInt(arguments, "drawing_priority");

        var command = new CreateLayerCommand(name, color, lineWeight, drawingPriority: drawingPriority);
        executeCommand(command);
        var layerId = command.LayerId ?? throw new InvalidOperationException("The layer was not created.");
        return LayerDto(document.GetLayer(layerId));
    }

    private object RenameLayer(JsonElement arguments)
    {
        var layer = ResolveLayer(RequiredString(arguments, "layer"));
        executeCommand(new RenameLayerCommand(layer.Id, RequiredString(arguments, "new_name")));
        return LayerDto(document.GetLayer(layer.Id));
    }

    private object DeleteLayer(JsonElement arguments)
    {
        var confirmation = arguments.TryGetProperty("delete_entities", out var value) &&
                           value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                           value.GetBoolean();
        if (!confirmation)
            throw new ArgumentException("delete_entities must be true to confirm deletion of entities on the layer.");

        var layer = ResolveLayer(RequiredString(arguments, "layer"));
        var entityCount = document.GetEntityCountOnLayer(layer.Id);
        executeCommand(new DeleteLayerCommand(layer.Id));
        return new
        {
            deleted_layer_id = layer.Id.Value,
            deleted_layer_name = layer.Name,
            deleted_entity_count = entityCount,
            remaining_layer_count = document.Layers.Count
        };
    }

    private object SetLayerProperties(JsonElement arguments)
    {
        var layer = ResolveLayer(RequiredString(arguments, "layer"));
        var hasColor = HasValue(arguments, "color");
        var hasLineWeight = HasValue(arguments, "line_weight");
        var hasVisible = HasValue(arguments, "visible");
        var hasLocked = HasValue(arguments, "locked");
        var hasFrozen = HasValue(arguments, "frozen");
        var hasPriority = HasValue(arguments, "drawing_priority");
        if (!hasColor && !hasLineWeight && !hasVisible && !hasLocked && !hasFrozen && !hasPriority)
            throw new ArgumentException("At least one layer property must be supplied.");

        if (hasColor || hasLineWeight)
        {
            var color = hasColor
                ? CadAiWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "color"))
                : layer.Color;
            var lineWeight = hasLineWeight
                ? ParseLineWeight(arguments.GetProperty("line_weight"))
                : layer.LineWeight;
            executeCommand(new SetLayerAppearanceCommand(layer.Id, color, lineWeight));
        }

        if (hasVisible || hasLocked || hasFrozen)
        {
            executeCommand(new SetLayerStateCommand(
                layer.Id,
                hasVisible ? RequiredBoolean(arguments, "visible") : layer.IsVisible,
                hasLocked ? RequiredBoolean(arguments, "locked") : layer.IsLocked,
                hasFrozen ? RequiredBoolean(arguments, "frozen") : layer.IsFrozen));
        }

        if (hasPriority)
        {
            var priorities = new Dictionary<LayerId, int>(
                document.DocumentSettings.LayerDrawingPriority.Priorities)
            {
                [layer.Id] = RequiredInt(arguments, "drawing_priority")
            };
            executeCommand(new SetLayerDrawingPrioritiesCommand(
                priorities,
                document.DocumentSettings.LayerDrawingPriority.DefaultPriority));
        }

        return LayerDto(document.GetLayer(layer.Id));
    }

    private object ReorderLayers(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("layer_order", out var order) || order.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("layer_order must be an array.");

        var layerIds = order.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
                ? ResolveLayer(item.GetString()!).Id
                : throw new ArgumentException("layer_order must contain layer names or IDs."))
            .ToArray();
        if (layerIds.Length != document.Layers.Count ||
            layerIds.Distinct().Count() != document.Layers.Count ||
            !layerIds.ToHashSet().SetEquals(document.Layers.Keys))
        {
            throw new ArgumentException("layer_order must contain every document layer exactly once.");
        }

        var priorities = layerIds
            .Select((layerId, index) => new { layerId, priority = layerIds.Length - index - 1 })
            .ToDictionary(item => item.layerId, item => item.priority);
        executeCommand(new SetLayerDrawingPrioritiesCommand(priorities));
        return new { layers = OrderedLayers().Select(LayerDto).ToArray() };
    }

    private IEnumerable<CadLayer> OrderedLayers() => document.Layers.Values
        .OrderByDescending(layer => document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id))
        .ThenByDescending(layer => layer.Id.Value);

    private object LayerDto(CadLayer layer) => new
    {
        id = layer.Id.Value,
        layer.Name,
        color = ColorText(layer.Color),
        line_weight = LineWeightValue(layer.LineWeight),
        visible = layer.IsVisible,
        locked = layer.IsLocked,
        frozen = layer.IsFrozen,
        drawing_priority = document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id),
        default_graphic_style_id = layer.DefaultGraphicStyleId?.Value,
        entity_count = document.GetEntityCountOnLayer(layer.Id)
    };

    private CadLayer ResolveLayer(string value)
    {
        CadLayer? layer = null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
            document.Layers.TryGetValue(new LayerId(numericId), out layer);
        layer ??= document.Layers.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase));
        return layer ?? throw new ArgumentException($"Layer not found: {value}");
    }

    private static CadLineWeight ParseLineWeight(JsonElement value)
    {
        if (!value.TryGetDouble(out var weight) || !double.IsFinite(weight) || weight <= 0)
            throw new ArgumentException("Layer line_weight must be a finite number greater than zero.");
        return new CadLineWeight(weight);
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

    private static int RequiredInt(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new ArgumentException($"{name} must be an integer.");

    private static int? OptionalInt(JsonElement arguments, string name) =>
        HasValue(arguments, name) ? RequiredInt(arguments, name) : null;

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

    private static bool HasValue(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static object ObjectSchema(
        IReadOnlyDictionary<string, object> properties,
        IReadOnlyList<string>? required = null) => new
    {
        type = "object",
        properties,
        required = required ?? [],
        additionalProperties = false
    };

    private static object DocumentIdSchema() => StringSchema("Stable open-document ID from list_documents");
    private static object LayerSchema() => StringSchema("Existing layer name or ID");
    private static object ColorSchema() => StringSchema("#RRGGBB, #AARRGGBB, or a supported named color");
    private static object LineWeightSchema() => new { type = "number", exclusiveMinimum = 0.0 };
    private static object StringSchema(string description) => new { type = "string", description };

    private static AiToolDefinition Tool(string name, string description, object parameters) =>
        new(name, description, JsonSerializer.SerializeToElement(parameters));

    private static object? LineWeightValue(CadLineWeight lineWeight) =>
        lineWeight.IsByLayer ? "by_layer" : lineWeight.Value;

    private static string ColorText(CadColor color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
