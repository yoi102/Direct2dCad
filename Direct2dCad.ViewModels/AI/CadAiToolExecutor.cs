using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.AI;

internal sealed class CadAiToolExecutor(CadDocumentViewModel documentViewModel, Guid batchId)
{
    private const int MaximumListedEntities = 200;

    public static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        Tool("get_document_summary", "Get the active CAD document, layers, selection, and bounds summary.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("list_entities", "List entities in the current editing space. Use this before editing existing entities.",
            new
            {
                type = "object",
                properties = new
                {
                    type = new { type = "string", description = "Optional entity type such as Line, Circle, Rectangle, Text, or Polyline." },
                    layer = new { type = "string", description = "Optional layer name." },
                    selected_only = new { type = "boolean", description = "When true, return only currently selected entities." },
                    offset = new { type = "integer", minimum = 0 },
                    limit = new { type = "integer", minimum = 1, maximum = MaximumListedEntities }
                },
                additionalProperties = false
            }),
        Tool("add_line", "Add a line in CAD world coordinates.", CoordinateSchema(
            new[] { "x1", "y1", "x2", "y2" },
            new Dictionary<string, object>
            {
                ["x1"] = Number("Start X"), ["y1"] = Number("Start Y"),
                ["x2"] = Number("End X"), ["y2"] = Number("End Y")
            })),
        Tool("add_circle", "Add a circle in CAD world coordinates.", CoordinateSchema(
            new[] { "center_x", "center_y", "radius" },
            new Dictionary<string, object>
            {
                ["center_x"] = Number("Center X"), ["center_y"] = Number("Center Y"),
                ["radius"] = new { type = "number", exclusiveMinimum = 0.0 }
            })),
        Tool("add_rectangle", "Add an axis-aligned rectangle in CAD world coordinates.", CoordinateSchema(
            new[] { "min_x", "min_y", "max_x", "max_y" },
            new Dictionary<string, object>
            {
                ["min_x"] = Number("Minimum X"), ["min_y"] = Number("Minimum Y"),
                ["max_x"] = Number("Maximum X"), ["max_y"] = Number("Maximum Y"),
                ["corner_radius"] = new { type = "number", minimum = 0.0 }
            })),
        Tool("add_text", "Add TrueType text in CAD world coordinates. Rotation is in degrees.", CoordinateSchema(
            new[] { "text", "x", "y", "height" },
            new Dictionary<string, object>
            {
                ["text"] = new { type = "string", minLength = 1 },
                ["x"] = Number("Insertion X"), ["y"] = Number("Insertion Y"),
                ["height"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["rotation_degrees"] = Number("Counter-clockwise rotation in degrees")
            })),
        Tool("add_polyline", "Add a polyline from two or more CAD world-coordinate points.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["points"] = new
                    {
                        type = "array",
                        minItems = 2,
                        items = new
                        {
                            type = "object",
                            properties = new { x = Number("X"), y = Number("Y") },
                            required = new[] { "x", "y" },
                            additionalProperties = false
                        }
                    },
                    ["closed"] = new { type = "boolean" },
                    ["layer"] = String("Layer name; current drawing layer is used when omitted"),
                    ["name"] = String("Optional entity name")
                },
                required = new[] { "points" },
                additionalProperties = false
            }),
        Tool("add_arc", "Add a circular arc. Angles are counter-clockwise degrees; sweep may be negative.", CoordinateSchema(
            new[] { "center_x", "center_y", "radius", "start_angle_degrees", "sweep_angle_degrees" },
            new Dictionary<string, object>
            {
                ["center_x"] = Number("Center X"), ["center_y"] = Number("Center Y"),
                ["radius"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["start_angle_degrees"] = Number("Start angle in degrees"),
                ["sweep_angle_degrees"] = Number("Non-zero sweep in degrees, between -360 and 360")
            })),
        Tool("add_ellipse", "Add an axis-aligned ellipse in CAD world coordinates.", CoordinateSchema(
            new[] { "center_x", "center_y", "radius_x", "radius_y" },
            new Dictionary<string, object>
            {
                ["center_x"] = Number("Center X"), ["center_y"] = Number("Center Y"),
                ["radius_x"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["radius_y"] = new { type = "number", exclusiveMinimum = 0.0 }
            })),
        Tool("add_polygon", "Add a closed polygon from three or more CAD world-coordinate vertices.", PointCollectionSchema(
            "points", minimumPoints: 3, includeClosed: false)),
        Tool("add_spline", "Add one smooth interpolating spline that passes through every ordered fit point. Fit points are not Bezier control handles. Use several short splines for multi-part organic artwork.", PointCollectionSchema(
            "fit_points", minimumPoints: 2, includeClosed: true)),
        Tool("select_entities", "Replace the current selection with the supplied entity IDs.", EntityIdsSchema(required: true)),
        Tool("move_entities", "Move supplied entity IDs, or the current selection when IDs are omitted.",
            new
            {
                type = "object",
                properties = new
                {
                    entity_ids = EntityIdArray(),
                    delta_x = Number("World-coordinate X movement"),
                    delta_y = Number("World-coordinate Y movement")
                },
                required = new[] { "delta_x", "delta_y" },
                additionalProperties = false
            }),
        Tool("delete_entities", "Delete supplied entity IDs, or the current selection when IDs are omitted.", EntityIdsSchema(required: false)),
        Tool("change_entity_layer", "Move supplied entities to an existing target layer.",
            new
            {
                type = "object",
                properties = new { entity_ids = EntityIdArray(), layer = String("Existing target layer name") },
                required = new[] { "entity_ids", "layer" },
                additionalProperties = false
            }),
        Tool("undo", "Undo the latest CAD document command or command batch.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("redo", "Redo the latest CAD document command or command batch.",
            new { type = "object", properties = new { }, additionalProperties = false })
    ];

    internal CadDocumentViewModel DocumentViewModel => documentViewModel;

    internal void ExecuteCommand(ICadCommand command) =>
        documentViewModel.CadEditor.ExecuteInBatch(command, batchId);

    internal EntityId[] ResolveEntityIdsForTool(JsonElement arguments, bool allowSelectionFallback) =>
        ResolveEntityIds(arguments, allowSelectionFallback);

    internal LayerId ResolveLayerForTool(string name) => ResolveLayer(name);

    internal void ValidateCreationTool(string toolName, JsonElement arguments)
    {
        var layerId = ResolveLayer(arguments);
        CadEntityAccessPolicy.EnsureCanAddToLayer(documentViewModel.CadEditor.Document, layerId);
        _ = (ICadCommand)(toolName switch
        {
            "add_line" => CreateLineCommand(arguments),
            "add_circle" => CreateCircleCommand(arguments),
            "add_rectangle" => CreateRectangleCommand(arguments),
            "add_text" => CreateTextCommand(arguments),
            "add_polyline" => CreatePolylineCommand(arguments),
            "add_arc" => CreateArcCommand(arguments),
            "add_ellipse" => CreateEllipseCommand(arguments),
            "add_polygon" => CreatePolygonCommand(arguments),
            "add_spline" => CreateSplineCommand(arguments),
            _ => throw new ArgumentException($"Unsupported creation tool: {toolName}")
        });
    }

    public string Execute(AiToolCall toolCall)
    {
        try
        {
            using var arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(toolCall.ArgumentsJson)
                ? "{}"
                : toolCall.ArgumentsJson);
            return toolCall.Name switch
            {
                "get_document_summary" => GetDocumentSummary(),
                "list_entities" => ListEntities(arguments.RootElement),
                "add_line" => AddLine(arguments.RootElement),
                "add_circle" => AddCircle(arguments.RootElement),
                "add_rectangle" => AddRectangle(arguments.RootElement),
                "add_text" => AddText(arguments.RootElement),
                "add_polyline" => AddPolyline(arguments.RootElement),
                "add_arc" => AddArc(arguments.RootElement),
                "add_ellipse" => AddEllipse(arguments.RootElement),
                "add_polygon" => AddPolygon(arguments.RootElement),
                "add_spline" => AddSpline(arguments.RootElement),
                "select_entities" => SelectEntities(arguments.RootElement),
                "move_entities" => MoveEntities(arguments.RootElement),
                "delete_entities" => DeleteEntities(arguments.RootElement),
                "change_entity_layer" => ChangeEntityLayer(arguments.RootElement),
                "undo" => Undo(),
                "redo" => Redo(),
                _ => Error($"Unknown CAD tool: {toolCall.Name}")
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Error(exception.Message);
        }
    }

    public string CreateSystemPrompt()
    {
        var editor = documentViewModel.CadEditor;
        var viewport = editor.Viewport.VisibleWorldBounds;
        var entityCount = editor.Document.GetEntitiesInBlock(editor.ActiveOwnerBlockId)
            .Count(entity => !entity.IsErased);
        var selectedIds = editor.Selection.EntityIds.Take(20).Select(id => id.Value);
        var selection = string.Join(",", selectedIds);
        var spaceKind = documentViewModel.IsEditingBlock
            ? "block_definition"
            : documentViewModel.IsLayoutViewportActive
                ? "layout_model_viewport"
                : documentViewModel.IsPaperSpaceActive
                    ? "paper_space"
                    : "model_space";
        return $$"""
            You are the CAD editing assistant inside Direct2dCad. Use the supplied tools whenever the user asks to inspect or modify the drawing. Never claim an edit succeeded until its tool result confirms it. CAD coordinates use +X to the right and +Y upward. Angles exposed by tools are counter-clockwise degrees. Prefer inspecting entities before changing existing ones. Keep replies concise and summarize created or changed entity IDs. Current editor snapshot: space_kind={{spaceKind}}, owner_block_id={{editor.ActiveOwnerBlockId.Value}}, tool_mode={{documentViewModel.CadCanvasToolMode}}, entity_count={{entityCount}}, selected_entity_ids=[{{selection}}], can_undo={{editor.DocumentCommands.CanUndo}}, can_redo={{editor.DocumentCommands.CanRedo}}, drawing_layer='{{ResolveLayerName(documentViewModel.DrawingLayerId)}}', visible_world_bounds={{FormatRect(viewport)}}. Use get_editor_state for the full current snapshot. All mutating tool calls from this user request share one undo batch.
            """;
    }

    private string GetDocumentSummary()
    {
        var editor = documentViewModel.CadEditor;
        var document = editor.Document;
        var entities = GetCurrentSpaceEntities().ToArray();
        var bounds = entities.Aggregate(CadRectD.Empty, (current, entity) => current.Union(entity.Bounds));
        return Success(new
        {
            document = document.Name,
            active_space = editor.ActiveOwnerBlockId.ToString(),
            entity_count = entities.Length,
            selected_entity_ids = editor.Selection.EntityIds.Select(id => id.Value).ToArray(),
            drawing_layer = ResolveLayerName(documentViewModel.DrawingLayerId),
            layers = document.Layers.Values.Select(layer => new
            {
                id = layer.Id.Value,
                layer.Name,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen
            }).ToArray(),
            content_bounds = RectDto(bounds),
            visible_bounds = RectDto(editor.Viewport.VisibleWorldBounds)
        });
    }

    internal object GetEditorState() => CadAiEditorStateBuilder.Build(documentViewModel);

    private string ListEntities(JsonElement arguments)
    {
        var requestedType = OptionalString(arguments, "type");
        var requestedLayer = OptionalString(arguments, "layer");
        var selectedOnly = OptionalBool(arguments, "selected_only");
        var offset = Math.Max(0, OptionalInt(arguments, "offset", 0));
        var limit = Math.Clamp(OptionalInt(arguments, "limit", 50), 1, MaximumListedEntities);
        var selectedIds = selectedOnly
            ? documentViewModel.CadEditor.Selection.EntityIds.ToHashSet()
            : null;
        var query = GetCurrentSpaceEntities()
            .Where(entity => selectedIds is null || selectedIds.Contains(entity.Id))
            .Where(entity => requestedType is null || string.Equals(EntityType(entity), requestedType, StringComparison.OrdinalIgnoreCase))
            .Where(entity => requestedLayer is null || string.Equals(ResolveLayerName(entity.LayerId), requestedLayer, StringComparison.OrdinalIgnoreCase));
        var totalMatches = query.Count();
        var entities = query
            .Skip(offset)
            .Take(limit)
            .Select(entity => new
            {
                id = entity.Id.Value,
                type = EntityType(entity),
                entity.Name,
                layer = ResolveLayerName(entity.LayerId),
                bounds = RectDto(entity.Bounds),
                entity.IsVisible,
                entity.IsLocked,
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
                }
            })
            .ToArray();
        return Success(new
        {
            count = entities.Length,
            total_matches = totalMatches,
            offset,
            has_more = offset + entities.Length < totalMatches,
            entities
        });
    }

    private string AddLine(JsonElement arguments)
    {
        var command = CreateLineCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddLineCommand CreateLineCommand(JsonElement arguments) => new(
            new CadPointD(RequiredDouble(arguments, "x1"), RequiredDouble(arguments, "y1")),
            new CadPointD(RequiredDouble(arguments, "x2"), RequiredDouble(arguments, "y2")),
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Line"));

    private string AddCircle(JsonElement arguments)
    {
        var command = CreateCircleCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddCircleCommand CreateCircleCommand(JsonElement arguments) => new(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius"),
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Circle"));

    private string AddRectangle(JsonElement arguments)
    {
        var command = CreateRectangleCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddRectangleCommand CreateRectangleCommand(JsonElement arguments)
    {
        var minX = RequiredDouble(arguments, "min_x");
        var minY = RequiredDouble(arguments, "min_y");
        var maxX = RequiredDouble(arguments, "max_x");
        var maxY = RequiredDouble(arguments, "max_y");
        if (maxX <= minX || maxY <= minY)
            throw new ArgumentException("Rectangle max values must be greater than min values.");
        var radius = OptionalDouble(arguments, "corner_radius", 0);
        if (radius < 0)
            throw new ArgumentOutOfRangeException("corner_radius");

        return new AddRectangleCommand(
            CadRectD.FromLTRB(minX, minY, maxX, maxY),
            radius,
            radius,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Rectangle"));
    }

    private string AddText(JsonElement arguments)
    {
        var command = CreateTextCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddTextCommand CreateTextCommand(JsonElement arguments)
    {
        var text = RequiredString(arguments, "text");
        var rotation = OptionalDouble(arguments, "rotation_degrees", 0) * Math.PI / 180.0;
        return new AddTextCommand(
            text,
            new CadPointD(RequiredDouble(arguments, "x"), RequiredDouble(arguments, "y")),
            RequiredPositive(arguments, "height"),
            rotation,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Text"));
    }

    private string AddPolyline(JsonElement arguments)
    {
        var command = CreatePolylineCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddPolylineCommand CreatePolylineCommand(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("points", out var pointElements) || pointElements.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("points must be an array.");
        var points = pointElements.EnumerateArray()
            .Select(point => new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y")))
            .ToArray();
        var closed = OptionalBool(arguments, "closed");
        return new AddPolylineCommand(
            points,
            closed,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Polyline"));
    }

    private string AddArc(JsonElement arguments)
    {
        var command = CreateArcCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddArcCommand CreateArcCommand(JsonElement arguments)
    {
        var sweepDegrees = RequiredDouble(arguments, "sweep_angle_degrees");
        if (Math.Abs(sweepDegrees) <= 1e-9 || Math.Abs(sweepDegrees) > 360.0)
            throw new ArgumentOutOfRangeException("sweep_angle_degrees", "Sweep must be non-zero and no greater than 360 degrees.");
        return new AddArcCommand(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius"),
            RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0,
            sweepDegrees * Math.PI / 180.0,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Arc"));
    }

    private string AddEllipse(JsonElement arguments)
    {
        var command = CreateEllipseCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddEllipseCommand CreateEllipseCommand(JsonElement arguments) => new(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius_x"),
            RequiredPositive(arguments, "radius_y"),
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Ellipse"));

    private string AddPolygon(JsonElement arguments)
    {
        var command = CreatePolygonCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddPolygonCommand CreatePolygonCommand(JsonElement arguments)
    {
        var points = RequiredPoints(arguments, "points", minimumCount: 3);
        return new AddPolygonCommand(
            points,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Polygon"));
    }

    private string AddSpline(JsonElement arguments)
    {
        var command = CreateSplineCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddSplineCommand CreateSplineCommand(JsonElement arguments)
    {
        var fitPoints = RequiredPoints(arguments, "fit_points", minimumCount: 2);
        var closed = OptionalBool(arguments, "closed");
        if (closed && fitPoints.Length < 3)
            throw new ArgumentException("A closed spline requires at least three fit_points.");
        return new AddSplineCommand(
            fitPoints,
            closed,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Spline"));
    }

    private string SelectEntities(JsonElement arguments)
    {
        var ids = ResolveEntityIds(arguments, allowSelectionFallback: false);
        documentViewModel.SelectEntities(ids);
        return Success(new { selected_entity_ids = ids.Select(id => id.Value).ToArray() });
    }

    private string MoveEntities(JsonElement arguments)
    {
        var ids = ResolveEntityIds(arguments, allowSelectionFallback: true);
        var command = new MoveEntitiesCommand(ids, new CadVectorD(
            RequiredDouble(arguments, "delta_x"),
            RequiredDouble(arguments, "delta_y")));
        documentViewModel.CadEditor.ExecuteInBatch(command, batchId);
        documentViewModel.SelectEntities(ids);
        return Success(new { moved_entity_ids = ids.Select(id => id.Value).ToArray() });
    }

    private string DeleteEntities(JsonElement arguments)
    {
        var ids = ResolveEntityIds(arguments, allowSelectionFallback: true);
        var command = new DeleteEntitiesCommand(ids);
        documentViewModel.CadEditor.ExecuteInBatch(command, batchId);
        documentViewModel.SelectEntities([]);
        return Success(new { deleted_entity_ids = ids.Select(id => id.Value).ToArray() });
    }

    private string ChangeEntityLayer(JsonElement arguments)
    {
        var ids = ResolveEntityIds(arguments, allowSelectionFallback: false);
        var layerName = RequiredString(arguments, "layer");
        var targetLayer = ResolveLayer(layerName);
        var command = new ChangeLayerCommand(ids, targetLayer);
        documentViewModel.CadEditor.ExecuteInBatch(command, batchId);
        documentViewModel.SelectEntities(ids);
        return Success(new { entity_ids = ids.Select(id => id.Value).ToArray(), layer = ResolveLayerName(targetLayer) });
    }

    private string Undo()
    {
        documentViewModel.CadEditor.UndoDocument();
        return Success(new { action = "undo" });
    }

    private string Redo()
    {
        documentViewModel.CadEditor.RedoDocument();
        return Success(new { action = "redo" });
    }

    private string ExecuteCreate(ICadCommand command, Func<EntityId?> getCreatedEntityId)
    {
        documentViewModel.CadEditor.ExecuteInBatch(command, batchId);
        var id = getCreatedEntityId() ?? throw new InvalidOperationException("The CAD command did not create an entity.");
        return Success(new { created_entity_id = id.Value });
    }

    private IEnumerable<CadEntity> GetCurrentSpaceEntities()
    {
        var editor = documentViewModel.CadEditor;
        return editor.Document.GetEntitiesInBlock(editor.ActiveOwnerBlockId)
            .Where(entity => !entity.IsErased);
    }

    private LayerId ResolveLayer(JsonElement arguments)
    {
        var layer = OptionalString(arguments, "layer");
        return layer is null ? documentViewModel.DrawingLayerId : ResolveLayer(layer);
    }

    private LayerId ResolveLayer(string name)
    {
        var layer = documentViewModel.CadEditor.Document.Layers.Values.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Id.Value.ToString(CultureInfo.InvariantCulture), name, StringComparison.Ordinal));
        return layer?.Id ?? throw new ArgumentException($"Layer not found: {name}");
    }

    private string ResolveLayerName(LayerId layerId)
    {
        return documentViewModel.CadEditor.Document.TryGetLayer(layerId, out var layer) && layer is not null
            ? layer.Name
            : layerId.Value.ToString(CultureInfo.InvariantCulture);
    }

    private EntityId[] ResolveEntityIds(JsonElement arguments, bool allowSelectionFallback)
    {
        if (arguments.TryGetProperty("entity_ids", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
        {
            var ids = idsElement.EnumerateArray()
                .Select(item => item.TryGetInt64(out var value)
                    ? new EntityId(value)
                    : throw new ArgumentException("entity_ids must contain integer IDs."))
                .Distinct()
                .ToArray();
            if (ids.Length > 0)
                return ValidateEntityIds(ids);
        }

        if (allowSelectionFallback)
        {
            var selected = documentViewModel.CadEditor.Selection.EntityIds.ToArray();
            if (selected.Length > 0)
                return ValidateEntityIds(selected);
        }

        throw new ArgumentException("At least one entity ID is required.");
    }

    private EntityId[] ValidateEntityIds(EntityId[] ids)
    {
        var document = documentViewModel.CadEditor.Document;
        foreach (var id in ids)
        {
            if (!document.TryGetEntity(id, out var entity) || entity is null || entity.IsErased)
                throw new ArgumentException($"Entity not found: {id.Value}");
            if (!entity.OwnerBlockId.Equals(documentViewModel.CadEditor.ActiveOwnerBlockId))
                throw new InvalidOperationException($"Entity {id.Value} is not in the current editing space.");
        }
        return ids;
    }

    private string ResolveName(JsonElement arguments, string prefix)
    {
        var supplied = OptionalString(arguments, "name");
        if (supplied is not null)
            return supplied;

        var maximum = 0;
        foreach (var entity in documentViewModel.CadEditor.Document.Entities.Values)
        {
            if (!entity.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(entity.Name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var suffix))
                maximum = Math.Max(maximum, suffix);
        }
        return $"{prefix}{maximum + 1}";
    }

    private static AiToolDefinition Tool(string name, string description, object schema) =>
        new(name, description, JsonSerializer.SerializeToElement(schema));

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

    private static object EntityIdArray() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "integer" }
    };

    private static object Number(string description) => new { type = "number", description };
    private static object String(string description) => new { type = "string", description };

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static double RequiredDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static double RequiredPositive(JsonElement element, string name)
    {
        var value = RequiredDouble(element, name);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static double OptionalDouble(JsonElement element, string name, double fallback)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result
            : fallback;
    }

    private static int OptionalInt(JsonElement element, string name, int fallback)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static bool OptionalBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }

    internal static CadPointD[] RequiredPoints(JsonElement arguments, string name, int minimumCount)
    {
        if (!arguments.TryGetProperty(name, out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array.");
        var points = pointsElement.EnumerateArray()
            .Select(point => new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y")))
            .ToArray();
        if (points.Length < minimumCount)
            throw new ArgumentException($"{name} requires at least {minimumCount} points.");
        return points;
    }

    private static object RectDto(CadRectD rect) => rect.IsEmpty
        ? new { empty = true }
        : new { min_x = rect.MinX, min_y = rect.MinY, max_x = rect.MaxX, max_y = rect.MaxY };

    private static string FormatRect(CadRectD rect) => rect.IsEmpty
        ? "empty"
        : FormattableString.Invariant($"[{rect.MinX:0.###}, {rect.MinY:0.###}] to [{rect.MaxX:0.###}, {rect.MaxY:0.###}]");

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

    private static StyleId? GraphicStyleId(CadEntity entity) => entity switch
    {
        CadLine value => value.GraphicStyleId,
        CadCircle value => value.GraphicStyleId,
        CadArc value => value.GraphicStyleId,
        CadEllipse value => value.GraphicStyleId,
        CadEllipseArc value => value.GraphicStyleId,
        CadRectangle value => value.GraphicStyleId,
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

    private static string ProtocolEnum<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private static string Success(object value) => JsonSerializer.Serialize(new { success = true, result = value });
    private static string Error(string message) => JsonSerializer.Serialize(new { success = false, error = message });
}
