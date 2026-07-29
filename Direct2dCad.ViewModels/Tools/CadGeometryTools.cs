using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadGeometryTools
{
    private const double DegreesPerRadian = 180.0 / Math.PI;

    internal static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        Tool("get_entity_geometry", "Read the exact typed geometry of one entity before editing it.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_id"] = EntityIdSchema()
            }, ["entity_id"])),
        Tool("set_entity_geometry", "Replace one entity's exact geometry with an undoable command. Supply all geometry fields for its type.",
            GeometryMutationSchema()),
        Tool("transform_entities", "Move, rotate, uniformly scale, or mirror entities as one undoable document batch.",
            TransformSchema()),
        Tool("duplicate_entities", "Duplicate entities in the current editing space with a world-coordinate offset.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema(),
                ["delta_x"] = Number("World-coordinate X offset"),
                ["delta_y"] = Number("World-coordinate Y offset")
            }, ["entity_ids", "delta_x", "delta_y"]))
    ];

    internal static object Execute(CadDocumentToolExecutor executor, string toolName, JsonElement arguments) => toolName switch
    {
        "get_entity_geometry" => GetEntityGeometry(executor, arguments),
        "set_entity_geometry" => SetEntityGeometry(executor, arguments),
        "transform_entities" => TransformEntities(executor, arguments),
        "duplicate_entities" => DuplicateEntities(executor, arguments),
        _ => throw new ArgumentException($"Unknown geometry tool: {toolName}")
    };

    private static object GetEntityGeometry(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var entity = GetEntity(executor, RequiredEntityId(arguments));
        return new
        {
            entity_id = entity.Id.Value,
            type = EntityType(entity),
            bounds = RectDto(entity.Bounds),
            geometry = GeometryDto(executor, entity)
        };
    }

    private static object SetEntityGeometry(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var id = RequiredEntityId(arguments);
        var entity = GetEntity(executor, id);
        foreach (var command in CreateGeometryCommands(entity, arguments))
            executor.ExecuteCommand(command);
        return new
        {
            entity_id = id.Value,
            type = EntityType(entity),
            geometry = GeometryDto(executor, GetEntity(executor, id))
        };
    }

    private static object TransformEntities(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: true);
        var operation = RequiredString(arguments, "operation").ToLowerInvariant();
        ICadCommand command = operation switch
        {
            "move" => new MoveEntitiesCommand(ids, new CadVectorD(
                RequiredDouble(arguments, "delta_x"),
                RequiredDouble(arguments, "delta_y"))),
            "rotate" => new RotateEntitiesCommand(ids, RequiredCoordinatePair(arguments, "pivot"),
                RequiredDouble(arguments, "angle_degrees") / DegreesPerRadian),
            "scale" => new ScaleEntitiesCommand(ids, RequiredCoordinatePair(arguments, "pivot"),
                RequiredPositive(arguments, "factor")),
            "mirror" => new MirrorEntitiesCommand(ids, RequiredCoordinatePair(arguments, "axis"),
                RequiredDouble(arguments, "axis_angle_degrees") / DegreesPerRadian),
            _ => throw new ArgumentException($"Unsupported transform operation: {operation}")
        };

        executor.ExecuteCommand(command);
        executor.DocumentViewModel.SelectEntities(ids);
        return new
        {
            operation,
            entity_ids = ids.Select(id => id.Value).ToArray()
        };
    }

    private static object DuplicateEntities(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var command = new DuplicateEntitiesCommand(
            ids,
            new CadVectorD(RequiredDouble(arguments, "delta_x"), RequiredDouble(arguments, "delta_y")),
            executor.DocumentViewModel.CadEditor.ActiveOwnerBlockId);
        executor.ExecuteCommand(command);
        var createdIds = command.CreatedEntityIds.ToArray();
        executor.DocumentViewModel.SelectEntities(createdIds);
        return new
        {
            source_entity_ids = ids.Select(id => id.Value).ToArray(),
            created_entity_ids = createdIds.Select(id => id.Value).ToArray()
        };
    }

    private static IReadOnlyList<ICadCommand> CreateGeometryCommands(CadEntity entity, JsonElement arguments) => entity switch
    {
        CadLine => [new SetLineGeometryCommand(entity.Id,
            RequiredPoint(arguments, "start"), RequiredPoint(arguments, "end"))],
        CadCircle => [new SetCircleGeometryCommand(entity.Id,
            RequiredPoint(arguments, "center"), RequiredPositive(arguments, "radius"))],
        CadArc => [new SetArcGeometryCommand(entity.Id,
            RequiredPoint(arguments, "center"),
            RequiredPositive(arguments, "radius"),
            RequiredDouble(arguments, "start_angle_degrees") / DegreesPerRadian,
            RequiredSweep(arguments, "sweep_angle_degrees") / DegreesPerRadian)],
        CadEllipse => [new SetEllipseGeometryCommand(entity.Id,
            RequiredPoint(arguments, "center"),
            RequiredPositive(arguments, "radius_x"),
            RequiredPositive(arguments, "radius_y"))],
        CadRectangle => CreateRectangleGeometryCommands(entity.Id, arguments),
        CadPolyline => [new SetPolylineGeometryCommand(entity.Id,
            CadDocumentToolExecutor.RequiredPoints(arguments, "points", 2), RequiredBool(arguments, "closed"))],
        CadSpline => [new SetSplineGeometryCommand(entity.Id,
            CadDocumentToolExecutor.RequiredPoints(arguments, "fit_points", 2), RequiredBool(arguments, "closed"))],
        CadCompositePath => CreateCompositePathGeometryCommands(entity.Id, arguments),
        CadText => [new SetTextGeometryCommand(entity.Id,
            RequiredPoint(arguments, "position"),
            RequiredPositive(arguments, "height"),
            RequiredDouble(arguments, "rotation_degrees") / DegreesPerRadian)],
        CadShapeText shapeText => [new SetShapeTextGeometryCommand(entity.Id,
            RequiredPoint(arguments, "position"),
            RequiredPositive(arguments, "height"),
            RequiredDouble(arguments, "rotation_degrees") / DegreesPerRadian,
            OptionalPositive(arguments, "width_factor", shapeText.WidthFactor),
            OptionalNonNegative(arguments, "character_spacing_factor", shapeText.CharacterSpacingFactor),
            OptionalDouble(arguments, "oblique_angle_degrees", shapeText.ObliqueAngleRadians * DegreesPerRadian) / DegreesPerRadian)],
        CadImage =>
        [
            new SetImageBoundsCommand(entity.Id, RequiredRect(arguments, "frame_bounds")),
            new SetImageRotationCommand(entity.Id, RequiredDouble(arguments, "rotation_degrees") / DegreesPerRadian)
        ],
        CadOleObject => [new SetOleObjectBoundsCommand(entity.Id, RequiredRect(arguments, "bounds"))],
        CadBlockReference => [new SetBlockReferenceTransformCommand(entity.Id,
            RequiredPoint(arguments, "position"),
            RequiredDouble(arguments, "rotation_degrees") / DegreesPerRadian,
            RequiredNonZero(arguments, "scale_x"),
            RequiredNonZero(arguments, "scale_y"))],
        _ => throw new NotSupportedException($"Exact geometry editing is not supported for {entity.GetType().Name}.")
    };

    private static IReadOnlyList<ICadCommand> CreateRectangleGeometryCommands(EntityId id, JsonElement arguments)
    {
        var bounds = RequiredRect(arguments, "bounds");
        var radiusX = OptionalNonNegative(arguments, "corner_radius_x", 0);
        var radiusY = OptionalNonNegative(arguments, "corner_radius_y", radiusX);
        return [new SetRectangleGeometryCommand(id, bounds), new SetRectangleCornerRadiusCommand(id, radiusX, radiusY)];
    }

    private static IReadOnlyList<ICadCommand> CreateCompositePathGeometryCommands(EntityId id, JsonElement arguments)
    {
        var geometry = CadCompositePathTools.Parse(arguments);
        return [new SetCompositePathGeometryCommand(id, geometry.StartPoint, geometry.Segments, geometry.Closed)];
    }

    private static object GeometryDto(CadDocumentToolExecutor executor, CadEntity entity) => entity switch
    {
        CadLine line => new { start = PointDto(line.Start), end = PointDto(line.End) },
        CadCircle circle => new { center = PointDto(circle.Center), radius = circle.Radius },
        CadArc arc => new
        {
            center = PointDto(arc.Center),
            radius = arc.Radius,
            start_angle_degrees = arc.StartAngleRadians * DegreesPerRadian,
            sweep_angle_degrees = arc.SweepAngleRadians * DegreesPerRadian,
            start = PointDto(arc.StartPoint),
            end = PointDto(arc.EndPoint)
        },
        CadEllipse ellipse => new { center = PointDto(ellipse.Center), radius_x = ellipse.RadiusX, radius_y = ellipse.RadiusY },
        CadEllipseArc ellipseArc => new
        {
            center = PointDto(ellipseArc.Center),
            radius_x = ellipseArc.RadiusX,
            radius_y = ellipseArc.RadiusY,
            start_angle_degrees = ellipseArc.StartAngleRadians * DegreesPerRadian,
            sweep_angle_degrees = ellipseArc.SweepAngleRadians * DegreesPerRadian
        },
        CadRectangle rectangle => new
        {
            bounds = RectDto(rectangle.Bounds),
            corner_radius_x = rectangle.CornerRadiusX,
            corner_radius_y = rectangle.CornerRadiusY
        },
        CadPolyline polyline => new { points = polyline.Points.Select(PointDto).ToArray(), closed = polyline.Closed },
        CadSpline spline => new
        {
            fit_points = spline.FitPoints.Select(PointDto).ToArray(),
            closed = spline.Closed,
            bezier_segments = spline.GetBezierSegments().Select(segment => new
            {
                start = PointDto(segment.Start),
                control1 = PointDto(segment.Control1),
                control2 = PointDto(segment.Control2),
                end = PointDto(segment.End)
            }).ToArray()
        },
        CadCompositePath path => CadCompositePathTools.ToDto(path),
        CadText text => new
        {
            text.Text,
            position = PointDto(text.Position),
            height = text.Height,
            rotation_degrees = text.RotationRadians * DegreesPerRadian,
            local_bounds = RectDto(text.LocalBounds)
        },
        CadShapeText text => new
        {
            text.Text,
            position = PointDto(text.Position),
            height = text.Height,
            rotation_degrees = text.RotationRadians * DegreesPerRadian,
            width_factor = text.WidthFactor,
            character_spacing_factor = text.CharacterSpacingFactor,
            oblique_angle_degrees = text.ObliqueAngleRadians * DegreesPerRadian
        },
        CadImage image => new
        {
            frame_bounds = RectDto(image.FrameBounds),
            rotation_degrees = image.RotationRadians * DegreesPerRadian,
            pixel_width = image.PixelWidth,
            pixel_height = image.PixelHeight,
            opacity = image.Opacity,
            source_name = image.SourceName,
            content_type = image.ContentType
        },
        CadOleObject ole => new
        {
            bounds = RectDto(ole.Bounds),
            opacity = ole.Opacity,
            source_name = ole.SourceName,
            content_type = ole.ContentType
        },
        CadBlockReference block => new
        {
            definition_block_id = block.DefinitionBlockId.Value,
            definition_name = executor.DocumentViewModel.CadEditor.Document.TryGetBlock(block.DefinitionBlockId, out var definition)
                ? definition?.Name
                : null,
            position = PointDto(block.Position),
            rotation_degrees = block.RotationRadians * DegreesPerRadian,
            scale_x = block.ScaleX,
            scale_y = block.ScaleY
        },
        _ => new { bounds = RectDto(entity.Bounds) }
    };

    private static CadEntity GetEntity(CadDocumentToolExecutor executor, EntityId id)
    {
        var editor = executor.DocumentViewModel.CadEditor;
        if (!editor.Document.TryGetEntity(id, out var entity) || entity is null || entity.IsErased)
            throw new ArgumentException($"Entity not found: {id.Value}");
        if (entity.OwnerBlockId != editor.ActiveOwnerBlockId)
            throw new InvalidOperationException($"Entity {id.Value} is not in the current editing space.");
        return entity;
    }

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

    private static object GeometryMutationSchema()
    {
        var properties = GeometryProperties();
        properties["document_id"] = DocumentIdSchema();
        properties["entity_id"] = EntityIdSchema();
        return ObjectSchema(properties, ["entity_id"]);
    }

    private static object TransformSchema() => ObjectSchema(new Dictionary<string, object>
    {
        ["document_id"] = DocumentIdSchema(),
        ["entity_ids"] = EntityIdsSchema(),
        ["operation"] = new { type = "string", @enum = new[] { "move", "rotate", "scale", "mirror" } },
        ["delta_x"] = Number("Required for move"),
        ["delta_y"] = Number("Required for move"),
        ["pivot_x"] = Number("Required for rotate and scale"),
        ["pivot_y"] = Number("Required for rotate and scale"),
        ["angle_degrees"] = Number("Counter-clockwise angle required for rotate"),
        ["factor"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["axis_x"] = Number("A point on the mirror axis"),
        ["axis_y"] = Number("A point on the mirror axis"),
        ["axis_angle_degrees"] = Number("Counter-clockwise mirror-axis direction")
    }, ["operation"]);

    private static Dictionary<string, object> GeometryProperties() => new()
    {
        ["start"] = PointSchema("Line start"),
        ["end"] = PointSchema("Line end"),
        ["center"] = PointSchema("Circle, arc, or ellipse center"),
        ["position"] = PointSchema("Text or block insertion position"),
        ["radius"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["radius_x"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["radius_y"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["start_angle_degrees"] = Number("Arc start angle"),
        ["sweep_angle_degrees"] = Number("Arc sweep angle"),
        ["bounds"] = RectSchema("Rectangle or OLE bounds"),
        ["frame_bounds"] = RectSchema("Unrotated image frame bounds"),
        ["corner_radius_x"] = new { type = "number", minimum = 0.0 },
        ["corner_radius_y"] = new { type = "number", minimum = 0.0 },
        ["points"] = PointArraySchema(2),
        ["fit_points"] = PointArraySchema(2),
        ["segments"] = new { type = "array", minItems = 1 },
        ["closed"] = new { type = "boolean" },
        ["height"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["rotation_degrees"] = Number("Counter-clockwise rotation"),
        ["width_factor"] = new { type = "number", exclusiveMinimum = 0.0 },
        ["character_spacing_factor"] = new { type = "number", minimum = 0.0 },
        ["oblique_angle_degrees"] = Number("Shape-text oblique angle"),
        ["scale_x"] = Number("Non-zero block X scale"),
        ["scale_y"] = Number("Non-zero block Y scale")
    };

    private static CadPointD RequiredPoint(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} must be a point object.");
        return new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y"));
    }

    private static CadPointD RequiredCoordinatePair(JsonElement arguments, string prefix) => new(
        RequiredDouble(arguments, $"{prefix}_x"),
        RequiredDouble(arguments, $"{prefix}_y"));

    private static CadRectD RequiredRect(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var bounds) || bounds.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} must be a bounds object.");
        var minX = RequiredDouble(bounds, "min_x");
        var minY = RequiredDouble(bounds, "min_y");
        var maxX = RequiredDouble(bounds, "max_x");
        var maxY = RequiredDouble(bounds, "max_y");
        if (maxX <= minX || maxY <= minY)
            throw new ArgumentException("max_x and max_y must be greater than min_x and min_y.");
        return CadRectD.FromLTRB(minX, minY, maxX, maxY);
    }

    private static EntityId RequiredEntityId(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("entity_id", out var value) || !value.TryGetInt64(out var id))
            throw new ArgumentException("entity_id must be an integer.");
        return new EntityId(id);
    }

    private static string RequiredString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static double RequiredDouble(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static double RequiredPositive(JsonElement arguments, string name)
    {
        var value = RequiredDouble(arguments, name);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static double RequiredNonZero(JsonElement arguments, string name)
    {
        var value = RequiredDouble(arguments, name);
        return Math.Abs(value) > 1e-9 ? value : throw new ArgumentOutOfRangeException(name, "Value must be non-zero.");
    }

    private static double RequiredSweep(JsonElement arguments, string name)
    {
        var value = RequiredDouble(arguments, name);
        return Math.Abs(value) > 1e-9 && Math.Abs(value) <= 360
            ? value
            : throw new ArgumentOutOfRangeException(name, "Sweep must be non-zero and no greater than 360 degrees.");
    }

    private static bool RequiredBool(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException($"{name} must be a boolean.");
        return value.GetBoolean();
    }

    private static double OptionalDouble(JsonElement arguments, string name, double fallback)
    {
        if (!arguments.TryGetProperty(name, out var value))
            return fallback;
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static double OptionalPositive(JsonElement arguments, string name, double fallback)
    {
        var value = OptionalDouble(arguments, name, fallback);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static double OptionalNonNegative(JsonElement arguments, string name, double fallback)
    {
        var value = OptionalDouble(arguments, name, fallback);
        return value >= 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must not be negative.");
    }

    private static object PointDto(CadPointD point) => new { x = point.X, y = point.Y };
    private static object RectDto(CadRectD rect) => rect.IsEmpty
        ? new { empty = true }
        : new { min_x = rect.MinX, min_y = rect.MinY, max_x = rect.MaxX, max_y = rect.MaxY };

    private static object PointSchema(string description) => new
    {
        description,
        type = "object",
        properties = new { x = Number("X"), y = Number("Y") },
        required = new[] { "x", "y" },
        additionalProperties = false
    };

    private static object PointArraySchema(int minimum) => new
    {
        type = "array",
        minItems = minimum,
        items = PointSchema("CAD world-coordinate point")
    };

    private static object RectSchema(string description) => new
    {
        description,
        type = "object",
        properties = new
        {
            min_x = Number("Minimum X"),
            min_y = Number("Minimum Y"),
            max_x = Number("Maximum X"),
            max_y = Number("Maximum Y")
        },
        required = new[] { "min_x", "min_y", "max_x", "max_y" },
        additionalProperties = false
    };

    private static object ObjectSchema(IReadOnlyDictionary<string, object> properties, IReadOnlyList<string>? required = null) => new
    {
        type = "object",
        properties,
        required = required ?? [],
        additionalProperties = false
    };

    private static object EntityIdSchema() => new { type = "integer" };
    private static object DocumentIdSchema() => new { type = "string", description = "Stable open-document ID" };
    private static object EntityIdsSchema() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = EntityIdSchema()
    };
    private static object Number(string description) => new { type = "number", description };
    private static AiToolDefinition Tool(string name, string description, object schema) =>
        new(name, description, JsonSerializer.SerializeToElement(schema));
}
