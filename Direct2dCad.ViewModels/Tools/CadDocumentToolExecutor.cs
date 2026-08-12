using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.ViewModels.Tools;

internal sealed class CadDocumentToolExecutor(CadDocumentViewModel documentViewModel, Guid batchId)
{
    private const int MaximumListedEntities = 200;
    private int _executedCommandCount;

    public static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        Tool("get_document_summary", "Get the active CAD document, layers, selection, and bounds summary.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("get_view_settings", "Get the document unit and the drawing-view settings shown in the status bar.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("set_view_settings", "Set one or more document-view settings shown in the status bar. Geometry values remain in canonical CAD world units (millimetres).",
            ViewSettingsSchema()),
        Tool("manage_grid_presets", "List or modify named grid spacing presets. Changes are undoable document view settings.",
            GridPresetManagementSchema()),
        Tool("set_viewport", "Pan, zoom, center, or fit the active drawing viewport. Viewport coordinates use canonical millimetres.",
            ViewportSchema()),
        Tool("get_entity_statistics", "Get complete unpaged entity counts with optional type, layer, owner, or selection filters, grouped by type, layer, and owner space. Use this for counts and inventories.",
            CadEntityQueryProtocol.CreateSchema(paged: false, MaximumListedEntities)),
        Tool("list_entities", "Query a sorted page of entity details by type, layer, name, state, style, geometry, content, or spatial bounds. Results include total_matches and has_more.",
            CadEntityQueryProtocol.CreateSchema(paged: true, MaximumListedEntities)),
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
                ["corner_radius"] = new { type = "number", minimum = 0.0, description = "Shared X/Y corner radius" },
                ["corner_radius_x"] = new { type = "number", minimum = 0.0 },
                ["corner_radius_y"] = new { type = "number", minimum = 0.0 }
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
        Tool("add_shape_text", "Add CAD stroke-based ShapeText. Rotation and oblique angle are in degrees.", CoordinateSchema(
            new[] { "text", "x", "y", "height" },
            new Dictionary<string, object>
            {
                ["text"] = new { type = "string", minLength = 1 },
                ["x"] = Number("Insertion X"), ["y"] = Number("Insertion Y"),
                ["height"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["rotation_degrees"] = Number("Counter-clockwise rotation in degrees"),
                ["width_factor"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["character_spacing_factor"] = new { type = "number", minimum = 0.0 },
                ["oblique_angle_degrees"] = Number("ShapeText oblique angle in degrees")
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
        Tool("add_ellipse_arc", "Add an elliptical arc. Angles are counter-clockwise degrees; sweep may be negative.", CoordinateSchema(
            new[] { "center_x", "center_y", "radius_x", "radius_y", "start_angle_degrees", "sweep_angle_degrees" },
            new Dictionary<string, object>
            {
                ["center_x"] = Number("Center X"), ["center_y"] = Number("Center Y"),
                ["radius_x"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["radius_y"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["start_angle_degrees"] = Number("Start angle in degrees"),
                ["sweep_angle_degrees"] = Number("Non-zero sweep in degrees, between -360 and 360")
            })),
        Tool("add_polygon", "Add a closed polygon from three or more CAD world-coordinate vertices.", PointCollectionSchema(
            "points", minimumPoints: 3, includeClosed: false)),
        Tool("add_spline", "Add a smooth interpolating spline from fit points.", PointCollectionSchema(
            "fit_points", minimumPoints: 2, includeClosed: true)),
        Tool("select_entities", "Replace the current selection with the supplied entity IDs.", EntityIdsSchema(required: true)),
        Tool("select_by_bounds", "Select entities intersecting or contained by a world-coordinate rectangle.",
            SelectionBoundsSchema()),
        Tool("select_by_polygon", "Select entities intersecting or contained by a world-coordinate polygon.",
            SelectionPolygonSchema()),
        Tool("select_by_filter", "Select entities in the active space by type, layer, name, visibility, or lock state.",
            SelectionFilterSchema()),
        Tool("clear_selection", "Clear the current entity selection. This is undoable in editor selection history.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("set_drawing_layer", "Set the active drawing layer used when creation tools omit layer.",
            new
            {
                type = "object",
                properties = new { layer = String("Existing layer name or ID") },
                required = new[] { "layer" },
                additionalProperties = false
            }),
        Tool("measure_geometry", "Measure points or supplied/currently selected entities. Lengths, coordinates, areas, and angles use canonical millimetres/degrees.",
            MeasurementSchema()),
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
        Tool("delete_entities", "Delete supplied entity IDs, or the current selection when IDs are omitted. Requires explicit confirm=true and is undoable.", DeleteEntitiesSchema()),
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
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("undo_view", "Undo the latest viewport or selection editor action.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        Tool("redo_view", "Redo the latest viewport or selection editor action.",
            new { type = "object", properties = new { }, additionalProperties = false })
    ];

    internal CadDocumentViewModel DocumentViewModel => documentViewModel;

    internal void ExecuteCommand(ICadCommand command)
    {
        documentViewModel.CadEditor.ExecuteInBatch(command, batchId);
        _executedCommandCount++;
    }

    internal T ExecuteAtomically<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var commandCount = _executedCommandCount;
        try
        {
            return operation();
        }
        catch
        {
            var executedSinceStart = _executedCommandCount - commandCount;
            if (executedSinceStart > 0)
            {
                documentViewModel.CadEditor.RollbackDocumentBatch(batchId, executedSinceStart);
                _executedCommandCount -= executedSinceStart;
            }

            throw;
        }
    }

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
            "add_shape_text" => CreateShapeTextCommand(arguments),
            "add_polyline" => CreatePolylineCommand(arguments),
            "add_arc" => CreateArcCommand(arguments),
            "add_ellipse" => CreateEllipseCommand(arguments),
            "add_ellipse_arc" => CreateEllipseArcCommand(arguments),
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
                "get_view_settings" => GetViewSettings(),
                "set_view_settings" => SetViewSettings(arguments.RootElement),
                "manage_grid_presets" => ManageGridPresets(arguments.RootElement),
                "set_viewport" => SetViewport(arguments.RootElement),
                "get_entity_statistics" => GetEntityStatistics(arguments.RootElement),
                "list_entities" => ListEntities(arguments.RootElement),
                "add_line" => AddLine(arguments.RootElement),
                "add_circle" => AddCircle(arguments.RootElement),
                "add_rectangle" => AddRectangle(arguments.RootElement),
                "add_text" => AddText(arguments.RootElement),
                "add_shape_text" => AddShapeText(arguments.RootElement),
                "add_polyline" => AddPolyline(arguments.RootElement),
                "add_arc" => AddArc(arguments.RootElement),
                "add_ellipse" => AddEllipse(arguments.RootElement),
                "add_ellipse_arc" => AddEllipseArc(arguments.RootElement),
                "add_polygon" => AddPolygon(arguments.RootElement),
                "add_spline" => AddSpline(arguments.RootElement),
                "select_entities" => SelectEntities(arguments.RootElement),
                "select_by_bounds" => SelectByBounds(arguments.RootElement),
                "select_by_polygon" => SelectByPolygon(arguments.RootElement),
                "select_by_filter" => SelectByFilter(arguments.RootElement),
                "clear_selection" => ClearSelection(),
                "set_drawing_layer" => SetDrawingLayer(arguments.RootElement),
                "measure_geometry" => MeasureGeometry(arguments.RootElement),
                "move_entities" => MoveEntities(arguments.RootElement),
                "delete_entities" => DeleteEntities(arguments.RootElement),
                "change_entity_layer" => ChangeEntityLayer(arguments.RootElement),
                "undo" => Undo(),
                "redo" => Redo(),
                "undo_view" => UndoView(),
                "redo_view" => RedoView(),
                _ => Error($"Unknown CAD tool: {toolCall.Name}")
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Error(exception.Message);
        }
    }

    public string CreateToolUsageInstructions()
    {
        var editor = documentViewModel.CadEditor;
        var viewport = editor.Viewport.VisibleWorldBounds;
        return $$"""
            You are the CAD editing assistant inside Direct2dCad. Use the supplied tools whenever the user asks to inspect or modify the drawing. Never claim an edit succeeded until its tool result confirms it. CAD coordinates use +X to the right and +Y upward. Angles exposed by tools are counter-clockwise degrees. The current editable owner space is the active model space, paper space, layout viewport model space, or block-edit space reported by get_document_summary. Mutating tools can only edit entities in that active owner space; scope=document is for inspection across spaces and does not grant permission to modify another space. Do not create a new document unless the user explicitly asks for one; always use the document_id of the requested open document.

            For complete entity counts or type inventories, call get_entity_statistics without a type filter. For counts constrained by names, states, styles, geometry, content, or bounds, call list_entities and use total_matches. list_entities is paged detail data, so never treat its returned entities as the whole document. Use get_view_settings before changing grid, snap, origin, background, precision, or display-unit settings. Use manage_grid_presets for named grid preset lifecycle. Use set_viewport for fit, zoom, pan, center, or entity/bounds fitting. Use measure_geometry for distances, angles, lengths, areas, intersections, nearest points, projections, and chords; its coordinates and lengths are canonical millimetres, and sampled curve operations report approximate=true. Geometry tool coordinates remain canonical millimetres regardless of the display unit. Prefer inspecting entities before changing existing ones. Keep replies concise and summarize created or changed entity IDs. The active drawing layer is '{{ResolveLayerName(documentViewModel.DrawingLayerId)}}'. The visible world bounds are {{FormatRect(viewport)}}. All mutating tool calls from this user request share one undo batch.

            Selection fallback is intentionally limited: move_entities, transform_entities, delete_entities, create_block, and duplicate_entities may use the current selection when entity_ids is omitted. Property, fill, stroke, layer-change, geometry, and specific-property tools require explicit entity_ids. delete_entities additionally requires confirm=true; delete_layer requires delete_entities=true and confirm=true because it removes the layer's entities. For common appearance, color and graphic_style are mutually exclusive. By-layer color and line weight are resolved from the entity layer; query results include resolved appearance when available. For fill mode style, supply style; solid and hatch may use their documented defaults, while hatch pattern defaults to ANSI31. Read get_entity_geometry before changing unknown geometry and include entity_type when possible.
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
            active_space_details = ActiveSpaceDetails(),
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
            visible_bounds = RectDto(editor.Viewport.VisibleWorldBounds),
            view_settings = CreateViewSettingsObject(),
            entity_statistics = CadEntityQuery.CreateStatistics(
                document,
                editor.ActiveOwnerBlockId,
                editor.Selection.EntityIds.ToHashSet(),
                new CadEntityQueryOptions(
                    CadEntityQuery.CurrentSpaceScope,
                    null,
                    null,
                    SelectedOnly: false))
        });
    }

    private string GetViewSettings() => Success(CreateViewSettingsObject());

    private string SetViewport(JsonElement arguments)
    {
        var editor = documentViewModel.CadEditor;
        var operation = RequiredString(arguments, "operation").ToLowerInvariant();
        switch (operation)
        {
            case "fit":
                if (arguments.TryGetProperty("padding", out _))
                {
                    editor.Execute(new FitViewportCommand(
                        OptionalNonNegative(arguments, "padding", 32),
                        editor.ActiveOwnerBlockId));
                }
                else
                {
                    documentViewModel.FitToWindow();
                }
                break;
            case "fit_bounds":
            {
                var bounds = arguments.TryGetProperty("entity_id", out var entityIdElement)
                    ? ResolveEntityBounds(entityIdElement)
                    : CadRectD.FromLTRB(
                        RequiredDouble(arguments, "min_x"),
                        RequiredDouble(arguments, "min_y"),
                        RequiredDouble(arguments, "max_x"),
                        RequiredDouble(arguments, "max_y"));
                editor.Execute(new FitViewportBoundsCommand(
                    bounds,
                    OptionalNonNegative(arguments, "padding", 32)));
                break;
            }
            case "zoom_entity":
            {
                var bounds = ResolveEntityBounds(arguments.GetProperty("entity_id"));
                editor.Execute(new FitViewportBoundsCommand(
                    bounds,
                    OptionalNonNegative(arguments, "padding", 32)));
                break;
            }
            case "zoom":
            {
                var factor = RequiredPositive(arguments, "factor");
                var anchor = arguments.TryGetProperty("anchor_x", out _) || arguments.TryGetProperty("anchor_y", out _)
                    ? new CadPointD(
                        RequiredDouble(arguments, "anchor_x"),
                        RequiredDouble(arguments, "anchor_y"))
                    : editor.Viewport.VisibleWorldBounds.Center;
                editor.Execute(new ZoomViewportCommand(editor.Viewport.WorldToScreen(anchor), factor));
                break;
            }
            case "pan":
            {
                var worldDelta = new CadVectorD(
                    RequiredDouble(arguments, "delta_x"),
                    RequiredDouble(arguments, "delta_y"));
                editor.Execute(new PanViewportCommand(new CadVectorD(
                    worldDelta.X * editor.Viewport.Zoom,
                    -worldDelta.Y * editor.Viewport.Zoom)));
                break;
            }
            case "center":
            {
                var point = new CadPointD(
                    RequiredDouble(arguments, "x"),
                    RequiredDouble(arguments, "y"));
                var screenDelta = new CadVectorD(
                    editor.Viewport.ViewWidth * 0.5 - editor.Viewport.WorldToScreen(point).X,
                    editor.Viewport.ViewHeight * 0.5 - editor.Viewport.WorldToScreen(point).Y);
                editor.Execute(new PanViewportCommand(screenDelta));
                break;
            }
            default:
                throw new ArgumentException("operation must be fit, fit_bounds, zoom, zoom_entity, pan, or center.");
        }

        documentViewModel.RequestRender();
        return Success(new
        {
            operation,
            zoom = editor.Viewport.Zoom,
            offset = new { x = editor.Viewport.Offset.X, y = editor.Viewport.Offset.Y },
            visible_bounds = RectDto(editor.Viewport.VisibleWorldBounds)
        });
    }

    private CadRectD ResolveEntityBounds(JsonElement entityIdElement)
    {
        if (!entityIdElement.TryGetInt64(out var rawId))
            throw new ArgumentException("entity_id must be an integer.");
        var entityId = new EntityId(rawId);
        if (!documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null || entity.IsErased ||
            entity.OwnerBlockId != documentViewModel.CadEditor.ActiveOwnerBlockId)
        {
            throw new ArgumentException("The entity was not found in the active drawing space.");
        }
        if (entity.Bounds.IsEmpty)
            throw new ArgumentException("The entity has no drawable bounds.");
        return entity.Bounds;
    }

    private string SelectByBounds(JsonElement arguments)
    {
        var area = CadRectD.FromLTRB(
            RequiredDouble(arguments, "min_x"),
            RequiredDouble(arguments, "min_y"),
            RequiredDouble(arguments, "max_x"),
            RequiredDouble(arguments, "max_y"));
        if (area.IsEmpty || area.Width <= 0 || area.Height <= 0)
            throw new ArgumentException("Selection bounds must have positive width and height.");

        var mode = ParseEnumValue<CadSelectionMode>(OptionalString(arguments, "mode") ?? "Replace");
        documentViewModel.CadEditor.Execute(new BoxSelectCommand(
            area,
            mode,
            OptionalBool(arguments, "require_contained"),
            ownerBlockId: documentViewModel.CadEditor.ActiveOwnerBlockId));
        documentViewModel.RequestRender();
        return SelectionResult();
    }

    private string SelectByPolygon(JsonElement arguments)
    {
        var polygon = RequiredPoints(arguments, "points", 3);
        var mode = ParseEnumValue<CadSelectionMode>(OptionalString(arguments, "mode") ?? "Replace");
        documentViewModel.CadEditor.Execute(new PolygonSelectCommand(
            polygon,
            mode,
            OptionalBool(arguments, "require_contained"),
            ownerBlockId: documentViewModel.CadEditor.ActiveOwnerBlockId));
        documentViewModel.RequestRender();
        return SelectionResult();
    }

    private string SelectByFilter(JsonElement arguments)
    {
        var type = OptionalString(arguments, "type");
        var layerName = OptionalString(arguments, "layer");
        var nameContains = OptionalString(arguments, "name_contains");
        var visible = OptionalNullableBool(arguments, "visible");
        var locked = OptionalNullableBool(arguments, "locked");
        if (type is null && layerName is null && nameContains is null && visible is null && locked is null)
            throw new ArgumentException("select_by_filter requires at least one filter property.");

        var layerId = layerName is null ? (LayerId?)null : ResolveLayer(layerName);
        var normalizedType = type is null ? null : NormalizeEntityType(type);
        var normalizedName = nameContains?.Trim();
        var mode = ParseEnumValue<CadSelectionMode>(OptionalString(arguments, "mode") ?? "Replace");
        documentViewModel.CadEditor.Execute(new FilterSelectionCommand(
            entity =>
                (normalizedType is null || NormalizeEntityType(entity.GetType().Name) == normalizedType) &&
                (layerId is null || entity.LayerId == layerId.Value) &&
                (normalizedName is null || entity.Name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase)) &&
                (visible is null || entity.IsVisible == visible.Value) &&
                (locked is null || entity.IsLocked == locked.Value),
            mode,
            documentViewModel.CadEditor.ActiveOwnerBlockId));
        documentViewModel.RequestRender();
        return SelectionResult();
    }

    private static string NormalizeEntityType(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("Cad", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];
        return normalized;
    }

    private string SetDrawingLayer(JsonElement arguments)
    {
        var layerId = ResolveLayer(RequiredString(arguments, "layer"));
        documentViewModel.DrawingLayerId = layerId;
        var layer = documentViewModel.CadEditor.Document.GetLayer(layerId);
        return Success(new { layer_id = layerId.Value, layer = layer.Name });
    }

    private string SelectionResult(string? action = null) => Success(new
    {
        action,
        selected_entity_ids = documentViewModel.CadEditor.Selection.EntityIds
            .Select(id => id.Value)
            .ToArray(),
        count = documentViewModel.CadEditor.Selection.Count
    });

    private string MeasureGeometry(JsonElement arguments)
    {
        var operation = (OptionalString(arguments, "operation") ?? "measure").ToLowerInvariant();
        if (operation is not "measure")
            return ExecuteAdvancedMeasurement(operation, arguments);

        var pointResults = Array.Empty<object>();
        if (arguments.TryGetProperty("points", out _))
        {
            var points = RequiredPoints(arguments, "points", 2);
            var segments = new List<object>();
            var total = 0.0;
            for (var index = 1; index < points.Length; index++)
            {
                var distance = points[index - 1].DistanceTo(points[index]);
                total += distance;
                segments.Add(new
                {
                    start_index = index - 1,
                    end_index = index,
                    distance_millimeters = distance,
                    angle_degrees = Math.Atan2(
                        points[index].Y - points[index - 1].Y,
                        points[index].X - points[index - 1].X) * 180.0 / Math.PI
                });
            }
            pointResults =
            [
                new
                {
                    point_count = points.Length,
                    total_distance_millimeters = total,
                    segments
                }
            ];
        }

        var entityIds = arguments.TryGetProperty("entity_ids", out _) ||
                        documentViewModel.CadEditor.Selection.Count > 0
            ? ResolveEntityIds(arguments, allowSelectionFallback: true)
            : [];
        var document = documentViewModel.CadEditor.Document;
        var entities = entityIds.Select(document.GetEntity).Select(MeasureEntity).ToArray();
        return Success(new { points = pointResults, entities });
    }

    private string ExecuteAdvancedMeasurement(string operation, JsonElement arguments)
    {
        var entityIds = ResolveEntityIds(arguments, allowSelectionFallback: true);
        var entities = entityIds
            .Select(documentViewModel.CadEditor.Document.GetEntity)
            .ToArray();
        return operation switch
        {
            "intersections" => Success(new
            {
                operation,
                approximate = entities.Any(entity => entity is not CadLine),
                points = FindIntersections(entities)
                    .Select(point => new { x = point.X, y = point.Y })
                    .ToArray()
            }),
            "nearest_point" or "project_point" => ExecuteNearestPoint(operation, arguments, entities),
            "chord" => ExecuteChord(arguments, entities),
            _ => throw new ArgumentException("operation must be measure, intersections, nearest_point, project_point, or chord.")
        };
    }

    private static string ExecuteNearestPoint(
        string operation,
        JsonElement arguments,
        IReadOnlyList<CadEntity> entities)
    {
        var point = RequiredPoint(arguments, "point");
        var candidates = entities
            .SelectMany(entity => FlattenEntity(entity).Zip(
                FlattenEntity(entity).Skip(1),
                (start, end) => ProjectToSegment(point, start, end)))
            .OrderBy(candidate => candidate.DistanceSquared)
            .ToArray();
        var nearest = candidates.FirstOrDefault();
        if (nearest == default)
            throw new ArgumentException("The selected entities have no measurable segments.");
        return Success(new
        {
            operation,
            approximate = entities.Any(entity => entity is not CadLine),
            source_point = new { x = point.X, y = point.Y },
            projected_point = new { x = nearest.Point.X, y = nearest.Point.Y },
            distance_millimeters = Math.Sqrt(nearest.DistanceSquared)
        });
    }

    private static string ExecuteChord(JsonElement arguments, IReadOnlyList<CadEntity> entities)
    {
        if (entities.Count != 1)
            throw new ArgumentException("chord requires exactly one entity.");
        var entity = entities[0];
        CadPointD start;
        CadPointD end;
        var approximate = false;
        switch (entity)
        {
            case CadCircle circle:
                start = circle.Center + new CadVectorD(
                    circle.Radius * Math.Cos(RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0),
                    circle.Radius * Math.Sin(RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0));
                end = circle.Center + new CadVectorD(
                    circle.Radius * Math.Cos(RequiredDouble(arguments, "end_angle_degrees") * Math.PI / 180.0),
                    circle.Radius * Math.Sin(RequiredDouble(arguments, "end_angle_degrees") * Math.PI / 180.0));
                break;
            case CadArc arc:
                start = arc.StartPoint;
                end = arc.EndPoint;
                break;
            case CadEllipseArc ellipseArc:
                start = ellipseArc.StartPoint;
                end = ellipseArc.EndPoint;
                break;
            default:
                var points = FlattenEntity(entity);
                if (points.Count < 2)
                    throw new ArgumentException("The entity has no measurable chord.");
                start = points[0];
                end = points[^1];
                approximate = entity is not CadLine;
                break;
        }
        return Success(new
        {
            operation = "chord",
            approximate,
            start = new { x = start.X, y = start.Y },
            end = new { x = end.X, y = end.Y },
            distance_millimeters = start.DistanceTo(end),
            angle_degrees = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI
        });
    }

    private static IReadOnlyList<CadPointD> FindIntersections(IReadOnlyList<CadEntity> entities)
    {
        var points = new List<CadPointD>();
        for (var leftIndex = 0; leftIndex < entities.Count; leftIndex++)
        {
            var leftSegments = FlattenEntity(entities[leftIndex]).Zip(FlattenEntity(entities[leftIndex]).Skip(1));
            for (var rightIndex = leftIndex + 1; rightIndex < entities.Count; rightIndex++)
            {
                var rightSegments = FlattenEntity(entities[rightIndex]).Zip(FlattenEntity(entities[rightIndex]).Skip(1));
                foreach (var left in leftSegments)
                foreach (var right in rightSegments)
                    if (TryIntersectSegments(left.First, left.Second, right.First, right.Second, out var point) &&
                        points.All(existing => existing.DistanceSquaredTo(point) > 1e-12))
                        points.Add(point);
            }
        }
        return points;
    }

    private static IReadOnlyList<CadPointD> FlattenEntity(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => [line.Start, line.End],
            CadPolyline polyline => CloseIfNeeded(polyline.Points, polyline.Closed),
            CadSpline spline => CloseIfNeeded(spline.EnumerateFlattenedPoints(32).ToArray(), spline.Closed),
            CadCompositePath path => CloseIfNeeded(path.EnumerateFlattenedPoints(32).ToArray(), path.Closed),
            CadArc arc => SampleCircle(arc.Center, arc.Radius, arc.StartAngleRadians, arc.SweepAngleRadians),
            CadCircle circle => SampleCircle(circle.Center, circle.Radius, 0, Math.PI * 2),
            CadEllipse ellipse => SampleEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, 0, Math.PI * 2),
            CadEllipseArc ellipseArc => SampleEllipse(ellipseArc.Center, ellipseArc.RadiusX, ellipseArc.RadiusY, ellipseArc.StartAngleRadians, ellipseArc.SweepAngleRadians),
            CadRectangle rectangle => [
                new CadPointD(rectangle.Bounds.MinX, rectangle.Bounds.MinY),
                new CadPointD(rectangle.Bounds.MaxX, rectangle.Bounds.MinY),
                new CadPointD(rectangle.Bounds.MaxX, rectangle.Bounds.MaxY),
                new CadPointD(rectangle.Bounds.MinX, rectangle.Bounds.MaxY),
                new CadPointD(rectangle.Bounds.MinX, rectangle.Bounds.MinY)],
            _ => []
        };
    }

    private static IReadOnlyList<CadPointD> CloseIfNeeded(IReadOnlyList<CadPointD> points, bool closed) =>
        closed && points.Count > 0 && points[0] != points[^1]
            ? points.Concat([points[0]]).ToArray()
            : points;

    private static IReadOnlyList<CadPointD> SampleCircle(CadPointD center, double radius, double start, double sweep) =>
        SampleParametric(center, radius, radius, start, sweep);

    private static IReadOnlyList<CadPointD> SampleEllipse(CadPointD center, double radiusX, double radiusY, double start, double sweep) =>
        SampleParametric(center, radiusX, radiusY, start, sweep);

    private static IReadOnlyList<CadPointD> SampleParametric(CadPointD center, double radiusX, double radiusY, double start, double sweep)
    {
        var count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 24)), 8, 192);
        return Enumerable.Range(0, count + 1)
            .Select(index =>
            {
                var angle = start + sweep * index / count;
                return new CadPointD(center.X + radiusX * Math.Cos(angle), center.Y + radiusY * Math.Sin(angle));
            })
            .ToArray();
    }

    private static SegmentProjection ProjectToSegment(CadPointD point, CadPointD start, CadPointD end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= 1e-18
            ? 0
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        var projected = new CadPointD(start.X + dx * t, start.Y + dy * t);
        return new SegmentProjection(projected, projected.DistanceSquaredTo(point));
    }

    private static bool TryIntersectSegments(CadPointD a, CadPointD b, CadPointD c, CadPointD d, out CadPointD point)
    {
        var denominator = (a.X - b.X) * (c.Y - d.Y) - (a.Y - b.Y) * (c.X - d.X);
        if (Math.Abs(denominator) <= 1e-12)
        {
            point = default;
            return false;
        }
        var first = a.X * b.Y - a.Y * b.X;
        var second = c.X * d.Y - c.Y * d.X;
        point = new CadPointD(
            (first * (c.X - d.X) - (a.X - b.X) * second) / denominator,
            (first * (c.Y - d.Y) - (a.Y - b.Y) * second) / denominator);
        return IsBetween(point, a, b) && IsBetween(point, c, d);
    }

    private static bool IsBetween(CadPointD point, CadPointD start, CadPointD end) =>
        point.X >= Math.Min(start.X, end.X) - 1e-9 && point.X <= Math.Max(start.X, end.X) + 1e-9 &&
        point.Y >= Math.Min(start.Y, end.Y) - 1e-9 && point.Y <= Math.Max(start.Y, end.Y) + 1e-9;

    private readonly record struct SegmentProjection(CadPointD Point, double DistanceSquared);

    private static object MeasureEntity(CadEntity entity)
    {
        var curve = entity as Curve;
        var area = TryGetArea(entity);
        var result = new Dictionary<string, object?>
        {
            ["entity_id"] = entity.Id.Value,
            ["type"] = entity.GetType().Name[3..],
            ["bounds"] = RectDto(entity.Bounds),
            ["length_millimeters"] = curve?.Length,
            ["closed"] = curve?.IsClosed,
            ["area_square_millimeters"] = area
        };
        switch (entity)
        {
            case CadLine line:
                result["start"] = new { x = line.Start.X, y = line.Start.Y };
                result["end"] = new { x = line.End.X, y = line.End.Y };
                result["angle_degrees"] = Math.Atan2(line.End.Y - line.Start.Y, line.End.X - line.Start.X) * 180.0 / Math.PI;
                break;
            case CadCircle circle:
                result["center"] = new { x = circle.Center.X, y = circle.Center.Y };
                result["radius_millimeters"] = circle.Radius;
                result["circumference_millimeters"] = circle.Length;
                break;
            case CadArc arc:
                result["center"] = new { x = arc.Center.X, y = arc.Center.Y };
                result["radius_millimeters"] = arc.Radius;
                result["start_angle_degrees"] = arc.StartAngleDegrees;
                result["sweep_angle_degrees"] = arc.SweepAngleDegrees;
                break;
            case CadEllipse ellipse:
                result["center"] = new { x = ellipse.Center.X, y = ellipse.Center.Y };
                result["radius_x_millimeters"] = ellipse.RadiusX;
                result["radius_y_millimeters"] = ellipse.RadiusY;
                break;
            case CadEllipseArc ellipseArc:
                result["center"] = new { x = ellipseArc.Center.X, y = ellipseArc.Center.Y };
                result["radius_x_millimeters"] = ellipseArc.RadiusX;
                result["radius_y_millimeters"] = ellipseArc.RadiusY;
                result["start_angle_degrees"] = ellipseArc.StartAngleDegrees;
                result["sweep_angle_degrees"] = ellipseArc.SweepAngleDegrees;
                break;
        }
        return result;
    }

    private static double? TryGetArea(CadEntity entity) => entity switch
    {
        CadCircle circle => Math.PI * circle.Radius * circle.Radius,
        CadEllipse ellipse => Math.PI * ellipse.RadiusX * ellipse.RadiusY,
        CadRectangle rectangle => rectangle.HasRoundedCorners
            ? rectangle.Bounds.Width * rectangle.Bounds.Height - (4 - Math.PI) * rectangle.CornerRadiusX * rectangle.CornerRadiusY
            : rectangle.Bounds.Width * rectangle.Bounds.Height,
        CadArc arc when arc.IsFullCircle => Math.PI * arc.Radius * arc.Radius,
        CadPolyline polyline when polyline.Closed => PolygonArea(polyline.Points),
        CadSpline spline when spline.Closed => PolygonArea(spline.EnumerateFlattenedPoints().ToArray()),
        CadCompositePath path when path.Closed => PolygonArea(path.EnumerateFlattenedPoints().ToArray()),
        _ => null
    };

    private static double PolygonArea(IReadOnlyList<CadPointD> points)
    {
        if (points.Count < 3)
            return 0;
        var area = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            area += points[index].X * next.Y - next.X * points[index].Y;
        }
        return Math.Abs(area) * 0.5;
    }

    private string SetViewSettings(JsonElement arguments)
    {
        var document = documentViewModel.CadEditor.Document;
        if (!arguments.EnumerateObject().Any())
            throw new ArgumentException("At least one view setting is required.");

        var target = CloneViewSettings(document.ViewSettings);
        var grid = target.Grid;
        var origin = target.Origin;
        var requestedUnit = arguments.TryGetProperty("unit", out _)
            ? ParseEnumValue<CadUnit>(RequiredString(arguments, "unit"))
            : (CadUnit?)null;
        var requestedLengthPrecision = arguments.TryGetProperty("length_precision", out _)
            ? RequiredPrecision(arguments, "length_precision")
            : (int?)null;
        var requestedAnglePrecision = arguments.TryGetProperty("angle_precision", out _)
            ? RequiredPrecision(arguments, "angle_precision")
            : (int?)null;
        if (arguments.TryGetProperty("grid_type", out _))
            grid.Type = ParseEnumValue<CadGridType>(RequiredString(arguments, "grid_type"));
        if (arguments.TryGetProperty("snap_marker_type", out _))
            grid.SnapMarkerType = ParseEnumValue<CadSnapMarkerType>(RequiredString(arguments, "snap_marker_type"));
        if (arguments.TryGetProperty("background_color", out _))
            target.BackgroundColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "background_color"));
        if (arguments.TryGetProperty("grid_minor_line_color", out _))
            grid.MinorLineColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "grid_minor_line_color"));
        if (arguments.TryGetProperty("grid_major_line_color", out _))
            grid.MajorLineColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "grid_major_line_color"));
        if (arguments.TryGetProperty("snap_marker_color", out _))
            grid.SnapMarkerColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "snap_marker_color"));
        if (arguments.TryGetProperty("origin_display_type", out _))
            origin.DisplayType = ParseEnumValue<CadOriginDisplayType>(RequiredString(arguments, "origin_display_type"));
        if (arguments.TryGetProperty("origin_marker_type", out _))
            origin.MarkerType = ParseEnumValue<CadOriginMarkerType>(RequiredString(arguments, "origin_marker_type"));
        if (arguments.TryGetProperty("origin_line_pattern", out _))
            origin.LinePattern = ParseEnumValue<CadOriginLinePattern>(RequiredString(arguments, "origin_line_pattern"));
        if (arguments.TryGetProperty("origin_color", out _))
            origin.Color = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "origin_color"));
        if (arguments.TryGetProperty("origin_position_x_millimeters", out _))
            origin.Position = new CadPointD(
                RequiredDouble(arguments, "origin_position_x_millimeters"),
                origin.Position.Y);
        if (arguments.TryGetProperty("origin_position_y_millimeters", out _))
            origin.Position = new CadPointD(
                origin.Position.X,
                RequiredDouble(arguments, "origin_position_y_millimeters"));
        if (arguments.TryGetProperty("origin_size", out _))
            origin.Size = RequiredPositive(arguments, "origin_size");
        if (arguments.TryGetProperty("origin_stroke_width", out _))
            origin.StrokeWidth = RequiredPositive(arguments, "origin_stroke_width");
        if (arguments.TryGetProperty("grid_minimum_screen_spacing", out _))
            grid.MinimumScreenSpacing = RequiredPositive(arguments, "grid_minimum_screen_spacing");
        if (arguments.TryGetProperty("grid_minimum_world_spacing_millimeters", out _))
            grid.MinimumWorldSpacing = RequiredPositive(arguments, "grid_minimum_world_spacing_millimeters");
        if (arguments.TryGetProperty("grid_minor_line_width", out _))
            grid.MinorLineWidth = RequiredPositive(arguments, "grid_minor_line_width");
        if (arguments.TryGetProperty("grid_major_line_width", out _))
            grid.MajorLineWidth = RequiredPositive(arguments, "grid_major_line_width");
        if (arguments.TryGetProperty("snap_marker_length", out _))
            grid.SnapMarkerLength = RequiredPositive(arguments, "snap_marker_length");
        if (arguments.TryGetProperty("snap_marker_stroke_width", out _))
            grid.SnapMarkerStrokeWidth = RequiredPositive(arguments, "snap_marker_stroke_width");
        if (arguments.TryGetProperty("grid_snap_spacing_x_millimeters", out _))
            grid.SnapSpacingX = OptionalNonNegative(arguments, "grid_snap_spacing_x_millimeters", grid.SnapSpacingX);
        if (arguments.TryGetProperty("grid_snap_spacing_y_millimeters", out _))
            grid.SnapSpacingY = OptionalNonNegative(arguments, "grid_snap_spacing_y_millimeters", grid.SnapSpacingY);

        var hasMajorSpacing = arguments.TryGetProperty("grid_spacing_x_millimeters", out _) ||
                              arguments.TryGetProperty("grid_spacing_y_millimeters", out _);
        var hasMinorSpacing = arguments.TryGetProperty("grid_minor_spacing_x_millimeters", out _) ||
                              arguments.TryGetProperty("grid_minor_spacing_y_millimeters", out _);
        if (hasMajorSpacing || hasMinorSpacing)
        {
            if (arguments.TryGetProperty("major_grid_preset", out _) || arguments.TryGetProperty("minor_grid_preset", out _))
                throw new ArgumentException("Grid spacing values cannot be combined with grid presets.");

            grid.SpacingX = arguments.TryGetProperty("grid_spacing_x_millimeters", out _)
                ? RequiredPositive(arguments, "grid_spacing_x_millimeters")
                : grid.SpacingX;
            grid.SpacingY = arguments.TryGetProperty("grid_spacing_y_millimeters", out _)
                ? RequiredPositive(arguments, "grid_spacing_y_millimeters")
                : grid.SpacingY;
            grid.MinorSpacingX = arguments.TryGetProperty("grid_minor_spacing_x_millimeters", out _)
                ? RequiredPositive(arguments, "grid_minor_spacing_x_millimeters")
                : grid.GetMinorSpacingX();
            grid.MinorSpacingY = arguments.TryGetProperty("grid_minor_spacing_y_millimeters", out _)
                ? RequiredPositive(arguments, "grid_minor_spacing_y_millimeters")
                : grid.GetMinorSpacingY();
            if (!TryResolveGridSubdivision(grid.SpacingX, grid.MinorSpacingX, out var subdivisionX) ||
                !TryResolveGridSubdivision(grid.SpacingY, grid.MinorSpacingY, out var subdivisionY))
                throw new ArgumentException("Major and minor grid spacing must have an integer subdivision between 2 and 100.");
            grid.Subdivision = Math.Max(subdivisionX, subdivisionY);
            grid.MinimumWorldSpacing = Math.Min(grid.MinorSpacingX, grid.MinorSpacingY);
            grid.MajorSpacingPresetId = null;
            grid.MinorSpacingPresetId = null;
            grid.EnsurePresetSelections();
        }
        else if (arguments.TryGetProperty("major_grid_preset", out _) || arguments.TryGetProperty("minor_grid_preset", out _))
        {
            var majorPresetId = arguments.TryGetProperty("major_grid_preset", out _)
                ? ResolveGridPresetId(RequiredString(arguments, "major_grid_preset"))
                : grid.MajorSpacingPresetId;
            var minorPresetId = arguments.TryGetProperty("minor_grid_preset", out _)
                ? ResolveGridPresetId(RequiredString(arguments, "minor_grid_preset"))
                : grid.MinorSpacingPresetId;
            var major = grid.SpacingPresets.FirstOrDefault(preset => preset.Id == majorPresetId);
            var minor = grid.SpacingPresets.FirstOrDefault(preset => preset.Id == minorPresetId);
            if (major is null || minor is null ||
                !TryResolveGridSubdivision(major.SpacingX, minor.SpacingX, out var presetSubdivisionX) ||
                !TryResolveGridSubdivision(major.SpacingY, minor.SpacingY, out var presetSubdivisionY))
                throw new ArgumentException("The selected grid presets are incompatible.");
            grid.SpacingX = major.SpacingX;
            grid.SpacingY = major.SpacingY;
            grid.MinorSpacingX = minor.SpacingX;
            grid.MinorSpacingY = minor.SpacingY;
            grid.Subdivision = Math.Max(presetSubdivisionX, presetSubdivisionY);
            grid.MajorSpacingPresetId = major.Id;
            grid.MinorSpacingPresetId = minor.Id;
            grid.SnapSpacingX = 0;
            grid.SnapSpacingY = 0;
            grid.MinimumWorldSpacing = Math.Min(minor.SpacingX, minor.SpacingY);
        }

        var hasViewFields = arguments.EnumerateObject().Any(property =>
            property.Name is not ("unit" or "length_precision" or "angle_precision"));
        if (hasViewFields)
        {
            documentViewModel.CadEditor.ExecuteInBatch(
                new SetViewSettingsCommand(target),
                batchId);
            _executedCommandCount++;
        }
        if (requestedUnit is not null ||
            requestedLengthPrecision is not null ||
            requestedAnglePrecision is not null)
        {
            var settings = document.DocumentSettings;
            documentViewModel.CadEditor.ExecuteInBatch(
                new SetDocumentSettingsCommand(
                    requestedUnit ?? settings.Unit,
                    requestedLengthPrecision ?? settings.LengthPrecision,
                    requestedAnglePrecision ?? settings.AnglePrecision),
                batchId);
            _executedCommandCount++;
        }
        documentViewModel.RequestRender();

        return GetViewSettings();
    }

    private string ManageGridPresets(JsonElement arguments)
    {
        var operation = RequiredString(arguments, "operation").ToLowerInvariant();
        var document = documentViewModel.CadEditor.Document;
        var grid = document.ViewSettings.Grid;
        if (operation == "list")
        {
            return Success(new
            {
                presets = grid.SpacingPresets.Select(GridPresetDto).ToArray(),
                major_preset_id = grid.MajorSpacingPresetId,
                minor_preset_id = grid.MinorSpacingPresetId
            });
        }

        var presets = grid.SpacingPresets.ToList();
        var presetId = arguments.TryGetProperty("preset_id", out var idElement)
            ? ParseGuid(idElement, "preset_id")
            : Guid.Empty;
        switch (operation)
        {
            case "create":
            {
                var name = RequiredString(arguments, "name");
                EnsureGridPresetNameAvailable(presets, name);
                var spacingX = RequiredGridSpacing(arguments, "spacing_x_millimeters");
                var linkAxes = OptionalBool(arguments, "link_axes");
                var spacingY = linkAxes
                    ? spacingX
                    : RequiredGridSpacing(arguments, "spacing_y_millimeters");
                presets.Add(new CadGridSpacingPreset(Guid.NewGuid(), name, spacingX, spacingY, linkAxes));
                break;
            }
            case "rename":
            {
                var name = RequiredString(arguments, "name");
                EnsureGridPresetNameAvailable(presets, name, presetId);
                var index = FindGridPresetIndex(presets, presetId);
                presets[index] = presets[index] with { Name = name };
                break;
            }
            case "delete":
            {
                var index = FindGridPresetIndex(presets, presetId);
                if (presets.Count <= 2)
                    throw new ArgumentException("At least two grid presets must remain.");
                if (grid.MajorSpacingPresetId == presetId || grid.MinorSpacingPresetId == presetId)
                    throw new ArgumentException("Select another major/minor preset before deleting this preset.");
                presets.RemoveAt(index);
                break;
            }
            default:
                throw new ArgumentException("operation must be list, create, rename, or delete.");
        }

        var target = CloneViewSettings(document.ViewSettings);
        target.Grid.ReplaceSpacingPresets(presets, grid.MajorSpacingPresetId, grid.MinorSpacingPresetId);
        documentViewModel.CadEditor.ExecuteInBatch(new SetViewSettingsCommand(target), batchId);
        _executedCommandCount++;
        documentViewModel.RequestRender();
        return Success(new
        {
            operation,
            presets = target.Grid.SpacingPresets.Select(GridPresetDto).ToArray(),
            major_preset_id = target.Grid.MajorSpacingPresetId,
            minor_preset_id = target.Grid.MinorSpacingPresetId
        });
    }

    private static object GridPresetDto(CadGridSpacingPreset preset) => new
    {
        id = preset.Id,
        name = preset.Name,
        spacing_x_millimeters = preset.SpacingX,
        spacing_y_millimeters = preset.SpacingY,
        link_axes = preset.LinkAxes
    };

    private static void EnsureGridPresetNameAvailable(
        IReadOnlyList<CadGridSpacingPreset> presets,
        string name,
        Guid? except = null)
    {
        if (presets.Any(preset => preset.Id != except &&
                                 string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"A grid preset named '{name}' already exists.");
    }

    private static int FindGridPresetIndex(IReadOnlyList<CadGridSpacingPreset> presets, Guid id)
    {
        var index = presets.ToList().FindIndex(preset => preset.Id == id);
        return index >= 0 ? index : throw new ArgumentException("Grid preset was not found.");
    }

    private static Guid ParseGuid(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var value))
            throw new ArgumentException($"{name} must be a valid grid preset ID.");
        return value;
    }

    private static double RequiredGridSpacing(JsonElement arguments, string name)
    {
        var value = RequiredDouble(arguments, name);
        return value is >= CadGridSettings.MinimumSpacingMillimeters and <= CadGridSettings.MaximumSpacingMillimeters
            ? value
            : throw new ArgumentOutOfRangeException(name, "Grid spacing is outside the supported range.");
    }

    private object CreateViewSettingsObject()
    {
        var document = documentViewModel.CadEditor.Document;
        var grid = document.ViewSettings.Grid;
        var origin = document.ViewSettings.Origin;
        var unit = document.DocumentSettings.Unit;
        return new
        {
            unit = unit.ToString(),
            unit_symbol = CadUnitConversion.GetSymbol(unit),
            geometry_input_unit = "millimeter",
            tool_mode = documentViewModel.CadCanvasToolMode.ToString(),
            drawing_layer = ResolveLayerName(documentViewModel.DrawingLayerId),
            pointer = new
            {
                x = CadUnitConversion.FromMillimeters(documentViewModel.CurrentPointerWorldX, unit),
                y = CadUnitConversion.FromMillimeters(documentViewModel.CurrentPointerWorldY, unit),
                x_millimeters = documentViewModel.CurrentPointerWorldX,
                y_millimeters = documentViewModel.CurrentPointerWorldY
            },
            length_precision = document.DocumentSettings.LengthPrecision,
            angle_precision = document.DocumentSettings.AnglePrecision,
            grid = new
            {
                type = grid.Type.ToString(),
                snap_marker_type = grid.SnapMarkerType.ToString(),
                minimum_screen_spacing = grid.MinimumScreenSpacing,
                minimum_world_spacing_millimeters = grid.MinimumWorldSpacing,
                minor_line_color = FormatColor(grid.MinorLineColor),
                major_line_color = FormatColor(grid.MajorLineColor),
                minor_line_width = grid.MinorLineWidth,
                major_line_width = grid.MajorLineWidth,
                snap_marker_color = FormatColor(grid.SnapMarkerColor),
                snap_marker_length = grid.SnapMarkerLength,
                snap_marker_stroke_width = grid.SnapMarkerStrokeWidth,
                snap_spacing_x_millimeters = grid.SnapSpacingX,
                snap_spacing_y_millimeters = grid.SnapSpacingY,
                spacing_x_millimeters = grid.SpacingX,
                spacing_y_millimeters = grid.SpacingY,
                minor_spacing_x_millimeters = grid.GetMinorSpacingX(),
                minor_spacing_y_millimeters = grid.GetMinorSpacingY(),
                major_preset_id = grid.MajorSpacingPresetId,
                minor_preset_id = grid.MinorSpacingPresetId,
                presets = grid.SpacingPresets.Select(preset => new
                {
                    id = preset.Id,
                    preset.Name,
                    spacing_x = CadUnitConversion.FromMillimeters(preset.SpacingX, unit),
                    spacing_y = CadUnitConversion.FromMillimeters(preset.SpacingY, unit),
                    spacing_x_millimeters = preset.SpacingX,
                    spacing_y_millimeters = preset.SpacingY,
                    preset.LinkAxes
                }).ToArray()
            },
            origin = new
            {
                display_type = origin.DisplayType.ToString(),
                marker_type = origin.MarkerType.ToString(),
                line_pattern = origin.LinePattern.ToString(),
                position_x = CadUnitConversion.FromMillimeters(origin.Position.X, unit),
                position_y = CadUnitConversion.FromMillimeters(origin.Position.Y, unit),
                position_x_millimeters = origin.Position.X,
                position_y_millimeters = origin.Position.Y,
                color = FormatColor(origin.Color),
                size = origin.Size,
                stroke_width = origin.StrokeWidth
            },
            background_color = FormatColor(document.ViewSettings.BackgroundColor),
            visible_bounds = RectDto(documentViewModel.CadEditor.Viewport.VisibleWorldBounds)
        };
    }

    private Guid? ResolveGridPresetId(string value)
    {
        var presets = documentViewModel.CadEditor.Document.ViewSettings.Grid.SpacingPresets;
        if (Guid.TryParse(value, out var id))
        {
            if (presets.Any(preset => preset.Id == id))
                return id;

            throw new KeyNotFoundException($"Grid spacing preset not found: {value}");
        }

        var preset = presets.FirstOrDefault(item =>
            string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
        return preset?.Id ?? throw new KeyNotFoundException($"Grid spacing preset not found: {value}");
    }

    private Guid? ResolveCurrentGridPresetId(Guid? id, double spacingX, double spacingY)
    {
        var presets = documentViewModel.CadEditor.Document.ViewSettings.Grid.SpacingPresets;
        return presets.FirstOrDefault(preset => preset.Id == id)?.Id ??
               presets.FirstOrDefault(preset =>
                   NearlyEqual(preset.SpacingX, spacingX) && NearlyEqual(preset.SpacingY, spacingY))?.Id;
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;

    private static T ParseEnumValue<T>(string value) where T : struct, Enum
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new ArgumentException($"Unsupported {typeof(T).Name} value: {value}");
    }

    private object ActiveSpaceDetails()
    {
        var editor = documentViewModel.CadEditor;
        var document = editor.Document;
        var owner = document.TryGetBlock(editor.ActiveOwnerBlockId, out var block) && block is not null
            ? block.Name
            : editor.ActiveOwnerBlockId.ToString();
        var kind = documentViewModel.IsEditingBlock
            ? "block_edit"
            : documentViewModel.IsModelSpaceActive
                ? "model_space"
                : documentViewModel.IsLayoutViewportActive
                    ? "layout_viewport_model_space"
                    : "paper_space";

        CadLayout? layout = documentViewModel.ActiveLayoutId is { } layoutId
            ? document.GetLayout(layoutId)
            : null;
        return new
        {
            kind,
            owner_block_id = editor.ActiveOwnerBlockId.Value,
            owner,
            editing_block_id = documentViewModel.EditingBlockId?.Value,
            editing_block_name = documentViewModel.IsEditingBlock
                ? documentViewModel.EditingBlockName
                : null,
            layout_id = layout?.Id.Value,
            layout_name = layout?.Name,
            viewport_id = documentViewModel.ActiveLayoutViewportId?.Value,
            is_model_space = documentViewModel.IsModelSpaceActive || documentViewModel.IsLayoutViewportActive,
            is_paper_space = documentViewModel.IsPaperSpaceActive
        };
    }

    private string GetEntityStatistics(JsonElement arguments)
    {
        var editor = documentViewModel.CadEditor;
        return Success(CadEntityQuery.CreateStatistics(
            editor.Document,
            editor.ActiveOwnerBlockId,
            editor.Selection.EntityIds.ToHashSet(),
            CadEntityQueryProtocol.Parse(arguments, paged: false, MaximumListedEntities)));
    }

    private string ListEntities(JsonElement arguments)
    {
        var editor = documentViewModel.CadEditor;
        return Success(CadEntityQuery.CreatePage(
            editor.Document,
            editor.ActiveOwnerBlockId,
            editor.Selection.EntityIds.ToHashSet(),
            CadEntityQueryProtocol.Parse(arguments, paged: true, MaximumListedEntities)));
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
        var sharedRadius = OptionalNonNegative(arguments, "corner_radius", 0);
        var radiusX = OptionalNonNegative(arguments, "corner_radius_x", sharedRadius);
        var radiusY = OptionalNonNegative(arguments, "corner_radius_y", sharedRadius);

        return new AddRectangleCommand(
            CadRectD.FromLTRB(minX, minY, maxX, maxY),
            radiusX,
            radiusY,
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

    private string AddShapeText(JsonElement arguments)
    {
        var command = CreateShapeTextCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddShapeTextCommand CreateShapeTextCommand(JsonElement arguments) => new(
        RequiredString(arguments, "text"),
        new CadPointD(RequiredDouble(arguments, "x"), RequiredDouble(arguments, "y")),
        RequiredPositive(arguments, "height"),
        OptionalDouble(arguments, "rotation_degrees", 0) * Math.PI / 180.0,
        OptionalPositive(arguments, "width_factor", CadStrokeFont.DefaultWidthFactor),
        OptionalNonNegative(arguments, "character_spacing_factor", CadStrokeFont.DefaultCharacterSpacingFactor),
        OptionalDouble(arguments, "oblique_angle_degrees", 0) * Math.PI / 180.0,
        ResolveLayer(arguments),
        name: ResolveName(arguments, "ShapeText"));

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

    private string AddEllipseArc(JsonElement arguments)
    {
        var command = CreateEllipseArcCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddEllipseArcCommand CreateEllipseArcCommand(JsonElement arguments)
    {
        var sweepDegrees = RequiredDouble(arguments, "sweep_angle_degrees");
        if (Math.Abs(sweepDegrees) <= 1e-9 || Math.Abs(sweepDegrees) > 360.0)
            throw new ArgumentOutOfRangeException("sweep_angle_degrees", "Sweep must be non-zero and no greater than 360 degrees.");
        return new AddEllipseArcCommand(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius_x"),
            RequiredPositive(arguments, "radius_y"),
            RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0,
            sweepDegrees * Math.PI / 180.0,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "EllipseArc"));
    }

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
        documentViewModel.CadEditor.Execute(new SetSelectionCommand(ids));
        documentViewModel.RequestRender();
        return SelectionResult();
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
        if (!arguments.TryGetProperty("confirm", out var confirmation) ||
            confirmation.ValueKind != JsonValueKind.True ||
            !confirmation.GetBoolean())
        {
            throw new ArgumentException("delete_entities requires confirm=true.");
        }
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

    private string ClearSelection()
    {
        documentViewModel.CadEditor.Execute(new ClearSelectionCommand());
        documentViewModel.RequestRender();
        return SelectionResult();
    }

    private string UndoView()
    {
        documentViewModel.CadEditor.UndoEditor();
        documentViewModel.RequestRender();
        return SelectionResult(action: "undo_view");
    }

    private string RedoView()
    {
        documentViewModel.CadEditor.RedoEditor();
        documentViewModel.RequestRender();
        return SelectionResult(action: "redo_view");
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

    private static double OptionalPositive(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out _))
            return fallback;
        var value = RequiredDouble(element, name);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static double OptionalNonNegative(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out _))
            return fallback;
        var value = RequiredDouble(element, name);
        return value >= 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must not be negative.");
    }

    private static bool OptionalBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }

    private static bool? OptionalNullableBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
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

    private static CadPointD RequiredPoint(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} is required.");
        return new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y"));
    }

    private static object RectDto(CadRectD rect) => rect.IsEmpty
        ? new { empty = true }
        : new { min_x = rect.MinX, min_y = rect.MinY, max_x = rect.MaxX, max_y = rect.MaxY };

    private static string FormatColor(CadColor color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static object ViewSettingsSchema()
    {
        return new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["unit"] = new { type = "string", @enum = Enum.GetNames<CadUnit>() },
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
            ["operation"] = new { type = "string", @enum = new[] { "list", "create", "rename", "delete" } },
            ["preset_id"] = new { type = "string", format = "uuid" },
            ["name"] = String("Preset name"),
            ["spacing_x_millimeters"] = new { type = "number", minimum = CadGridSettings.MinimumSpacingMillimeters, maximum = CadGridSettings.MaximumSpacingMillimeters },
            ["spacing_y_millimeters"] = new { type = "number", minimum = CadGridSettings.MinimumSpacingMillimeters, maximum = CadGridSettings.MaximumSpacingMillimeters },
            ["link_axes"] = new { type = "boolean" }
        },
        required = new[] { "operation" },
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

    private static CadViewSettings CloneViewSettings(CadViewSettings source)
    {
        var result = new CadViewSettings
        {
            BackgroundColor = source.BackgroundColor,
            Grid = new CadGridSettings
            {
                Type = source.Grid.Type,
                SpacingX = source.Grid.SpacingX,
                SpacingY = source.Grid.SpacingY,
                MinorSpacingX = source.Grid.MinorSpacingX,
                MinorSpacingY = source.Grid.MinorSpacingY,
                Subdivision = source.Grid.Subdivision,
                SnapSpacingX = source.Grid.SnapSpacingX,
                SnapSpacingY = source.Grid.SnapSpacingY,
                MinimumScreenSpacing = source.Grid.MinimumScreenSpacing,
                MinimumWorldSpacing = source.Grid.MinimumWorldSpacing,
                MinorLineColor = source.Grid.MinorLineColor,
                MajorLineColor = source.Grid.MajorLineColor,
                MinorLineWidth = source.Grid.MinorLineWidth,
                MajorLineWidth = source.Grid.MajorLineWidth,
                SnapMarkerColor = source.Grid.SnapMarkerColor,
                SnapMarkerLength = source.Grid.SnapMarkerLength,
                SnapMarkerStrokeWidth = source.Grid.SnapMarkerStrokeWidth,
                SnapMarkerType = source.Grid.SnapMarkerType
            },
            Origin = new CadOriginSettings
            {
                Position = source.Origin.Position,
                DisplayType = source.Origin.DisplayType,
                MarkerType = source.Origin.MarkerType,
                LinePattern = source.Origin.LinePattern,
                Color = source.Origin.Color,
                Size = source.Origin.Size,
                StrokeWidth = source.Origin.StrokeWidth
            }
        };
        result.Grid.ReplaceSpacingPresets(
            source.Grid.SpacingPresets,
            source.Grid.MajorSpacingPresetId,
            source.Grid.MinorSpacingPresetId);
        return result;
    }

    private static bool TryResolveGridSubdivision(double major, double minor, out int subdivision)
    {
        subdivision = 0;
        if (!double.IsFinite(major) || !double.IsFinite(minor) || minor <= 0)
            return false;
        var ratio = major / minor;
        var rounded = Math.Round(ratio);
        if (ratio < CadGridSettings.MinimumSubdivision ||
            ratio > CadGridSettings.MaximumSubdivision ||
            Math.Abs(ratio - rounded) > 1e-9)
            return false;
        subdivision = (int)rounded;
        return true;
    }

    private static int RequiredPrecision(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetInt32(out var precision) || precision is < 0 or > 12)
            throw new ArgumentOutOfRangeException(name, "Precision must be an integer from 0 to 12.");
        return precision;
    }

    private static string FormatRect(CadRectD rect) => rect.IsEmpty
        ? "empty"
        : FormattableString.Invariant($"[{rect.MinX:0.###}, {rect.MinY:0.###}] to [{rect.MaxX:0.###}, {rect.MaxY:0.###}]");

    private static string Success(object value) => JsonSerializer.Serialize(new { success = true, result = value });
    private static string Error(string message) => JsonSerializer.Serialize(new { success = false, error = message });
}
