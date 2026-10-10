using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadWorkspaceToolExecutor
{
    private static void AddPresentationToolDefinitions(List<AiToolDefinition> tools)
    {
        object Id() => new { type = "integer", minimum = 1 };
        object Positive() => new { type = "number", exclusiveMinimum = 0 };
        Dictionary<string, object> Properties(params (string Key, object Schema)[] fields)
        {
            var properties = new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() };
            foreach (var (key, schema) in fields) properties[key] = schema;
            return properties;
        }
        void Add(string name, string description, Dictionary<string, object> properties, string[]? required = null) =>
            tools.Add(Tool(name, description, ObjectSchema(properties, required ?? [])));
        Add("list_layouts", "List layout IDs, paper settings, viewport IDs and active space. Dimensions are millimetres.", Properties());
        Add("create_layout", "Create an undoable paper layout. Does not switch active space. Default paper is 420 by 297 mm.",
            Properties(("name", StringSchema("Unique layout name")), ("width", Positive()), ("height", Positive())), ["name"]);
        Add("rename_layout", "Rename an existing paper layout with undo.",
            Properties(("layout_id", Id()), ("name", StringSchema("Unique layout name"))), ["layout_id", "name"]);
        Add("delete_layout", "Delete a layout and its paper entities with undo. Cannot delete the last layout. Requires confirm=true.",
            Properties(("layout_id", Id()), ("confirm", new { type = "boolean", @const = true })), ["layout_id", "confirm"]);
        var paper = Properties(("layout_id", Id()), ("width", Positive()), ("height", Positive()));
        foreach (var key in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            paper[key] = new { type = "number", minimum = 0 };
        paper["color"] = StringSchema("Paper color, #RRGGBB or #AARRGGBB");
        Add("set_layout_paper", "Set paper size, margins and color with undo. Omitted values are preserved; margins must leave positive printable area.", paper, ["layout_id"]);
        Add("activate_space", "Switch editable space. model needs no IDs; paper requires layout_id; viewport requires layout_id and viewport_id. Clears transient drawing and selection.",
            Properties(("space", new { type = "string", @enum = new[] { "model", "paper", "viewport" } }), ("layout_id", Id()), ("viewport_id", Id())), ["space"]);
        Dictionary<string, object> ViewportProperties(bool update)
        {
            var fields = Properties(("layout_id", Id()), ("x", NumberSchema("Paper left in mm")), ("y", NumberSchema("Paper bottom in mm")),
                ("width", Positive()), ("height", Positive()), ("model_center_x", NumberSchema("Model center in mm")),
                ("model_center_y", NumberSchema("Model center in mm")), ("scale", Positive()),
                ("rotation_degrees", NumberSchema("Counter-clockwise degrees")));
            if (update)
            {
                fields["viewport_id"] = Id();
                fields["visible"] = new { type = "boolean" };
                fields["locked"] = new { type = "boolean" };
            }
            return fields;
        }
        Add("add_layout_viewport", "Create an undoable paper viewport of the model. Scale is paper mm per model mm (0.01 is 1:100).",
            ViewportProperties(false), ["layout_id", "x", "y", "width", "height", "model_center_x", "model_center_y", "scale"]);
        Add("set_layout_viewport", "Update a paper viewport with undo; omitted fields are preserved. A locked viewport must be explicitly unlocked before changing its view.",
            ViewportProperties(true), ["layout_id", "viewport_id"]);
        Add("delete_layout_viewport", "Remove a paper viewport with undo; model entities are preserved.",
            Properties(("layout_id", Id()), ("viewport_id", Id())), ["layout_id", "viewport_id"]);
        tools.Add(Tool("reassociate_dimension", "Rebind an existing dimension to explicit source geometry, preserving placement and style. Use source_entity_id for a line or circle/arc; use references for explicit endpoints/vertices. Never guesses topology.",
            new
            {
                type = "object", properties = Properties(("entity_id", Id()), ("source_entity_id", Id()),
                    ("references", new { type = "array", minItems = 2, maxItems = 3, items = new
                    {
                        type = "object", properties = new Dictionary<string, object>
                        {
                            ["entity_id"] = Id(), ["feature"] = new { type = "string", @enum = Enum.GetNames<CadReferenceFeature>() },
                            ["angle_degrees"] = NumberSchema("CirclePoint angle; only valid for CirclePoint"),
                            ["vertex_index"] = new { type = "integer", minimum = 0 }
                        }, required = new[] { "entity_id", "feature" }, additionalProperties = false
                    } })), required = new[] { "entity_id" }, additionalProperties = false,
                oneOf = new[] { new { required = new[] { "source_entity_id" } }, new { required = new[] { "references" } } }
            }));
        Add("capture_view", "Render a PNG of the committed drawing in the current canvas view, including layout viewports. Returns a real image for visual inspection, plus document_version. Transient previews/cursor/grips are excluded. Requires a rendering host.",
            Properties(("maximum_size", new { type = "integer", minimum = 128, maximum = 1024 })));
        Add("print_document", "Open the host's print preview for the current model/layout. The user chooses printer/settings and submits or cancels. submitted means queued, not physically printed. Requires a desktop printing host.", Properties());
    }

    private static object ExecutePresentationTool(CadDocumentToolExecutor executor, string name, JsonElement args)
    {
        var session = executor.Session;
        var document = session.CadEditor.Document;
        if (name == "list_layouts") return LayoutsDto(session);
        if (document.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
        if (name == "reassociate_dimension") return ReassociateDimension(executor, args);
        if (name == "create_layout")
        {
            var command = new CreateLayoutCommand(RequiredString(args, "name"), PresentationNumber(args, "width", 420), PresentationNumber(args, "height", 297));
            executor.ExecuteCommand(command);
            return new { layout_id = command.CreatedLayoutId!.Value.Value };
        }
        var layout = document.GetLayout(new LayoutId(args.GetProperty("layout_id").GetInt64()));
        switch (name)
        {
            case "rename_layout": executor.ExecuteCommand(new RenameLayoutCommand(layout.Id, RequiredString(args, "name"))); break;
            case "delete_layout":
                if (document.Layouts.Count <= 1) throw new InvalidOperationException("The last layout cannot be deleted.");
                executor.ExecuteCommand(new DeleteLayoutCommand(layout.Id));
                if (session.ActiveLayoutId == layout.Id) session.ActivateModelSpace();
                break;
            case "set_layout_paper":
                RequirePresentationChange(args, "layout_id");
                var color = args.TryGetProperty("color", out var value) ? ParseColor(value.GetString()!) : (CadColor?)null;
                executor.ExecuteCommand(new SetLayoutPaperCommand(layout.Id, new(
                    PresentationNumber(args, "width", layout.PaperWidth), PresentationNumber(args, "height", layout.PaperHeight),
                    PresentationNumber(args, "margin_left", layout.MarginLeft), PresentationNumber(args, "margin_top", layout.MarginTop),
                    PresentationNumber(args, "margin_right", layout.MarginRight), PresentationNumber(args, "margin_bottom", layout.MarginBottom))));
                if (color is { } paperColor) executor.ExecuteCommand(new SetLayoutPaperColorCommand(layout.Id, paperColor));
                break;
            case "add_layout_viewport":
                var add = new AddLayoutViewportCommand(layout.Id,
                    CadRectD.FromXYWH(args.GetProperty("x").GetDouble(), args.GetProperty("y").GetDouble(), args.GetProperty("width").GetDouble(), args.GetProperty("height").GetDouble()),
                    new(args.GetProperty("model_center_x").GetDouble(), args.GetProperty("model_center_y").GetDouble()),
                    args.GetProperty("scale").GetDouble(), PresentationNumber(args, "rotation_degrees", 0) * Math.PI / 180);
                executor.ExecuteCommand(add);
                return new { layout_id = layout.Id.Value, viewport_id = add.CreatedViewportId!.Value.Value };
            case "delete_layout_viewport":
                var deleteId = new LayoutViewportId(args.GetProperty("viewport_id").GetInt64());
                executor.ExecuteCommand(new RemoveLayoutViewportCommand(layout.Id, deleteId));
                if (session.ActiveLayoutId == layout.Id && session.ActiveLayoutViewportId == deleteId) session.ExitLayoutViewport();
                break;
            case "set_layout_viewport":
                RequirePresentationChange(args, "layout_id", "viewport_id");
                var viewport = layout.GetViewport(new(args.GetProperty("viewport_id").GetInt64()));
                var changesView = args.EnumerateObject().Any(p => p.Name is not ("document_id" or "layout_id" or "viewport_id" or "visible" or "locked"));
                if (viewport.IsLocked && changesView && OptionalBool(args, "locked", true))
                    throw new InvalidOperationException("Unlock the viewport before changing its view.");
                var target = new CadLayoutViewportSnapshot(
                    CadRectD.FromXYWH(PresentationNumber(args, "x", viewport.Bounds.MinX), PresentationNumber(args, "y", viewport.Bounds.MinY),
                        PresentationNumber(args, "width", viewport.Bounds.Width), PresentationNumber(args, "height", viewport.Bounds.Height)),
                    new(PresentationNumber(args, "model_center_x", viewport.ModelCenter.X), PresentationNumber(args, "model_center_y", viewport.ModelCenter.Y)),
                    PresentationNumber(args, "scale", viewport.Scale), PresentationNumber(args, "rotation_degrees", viewport.RotationRadians * 180 / Math.PI) * Math.PI / 180,
                    OptionalBool(args, "visible", viewport.IsVisible), OptionalBool(args, "locked", viewport.IsLocked));
                executor.ExecuteCommand(new SetLayoutViewportCommand(layout.Id, viewport.Id, target));
                if (!target.IsVisible && session.ActiveLayoutId == layout.Id && session.ActiveLayoutViewportId == viewport.Id) session.ExitLayoutViewport();
                break;
            default: throw new ArgumentException($"Unknown presentation tool: {name}");
        }
        session.RequestRender();
        return LayoutsDto(session);
    }

    private string ActivateSpace(JsonElement args)
    {
        var document = ResolveDocument(args);
        var session = document.Session;
        var space = RequiredString(args, "space");
        if (space == "model")
        {
            if (args.TryGetProperty("layout_id", out _) || args.TryGetProperty("viewport_id", out _))
                throw new ArgumentException("Model space does not accept layout_id or viewport_id.");
            session.ActivateModelSpace();
        }
        else
        {
            var layout = session.CadEditor.Document.GetLayout(new(args.GetProperty("layout_id").GetInt64()));
            if (space == "paper" && args.TryGetProperty("viewport_id", out _))
                throw new ArgumentException("Paper space does not accept viewport_id.");
            CadLayoutViewport? viewport = space == "viewport" ? layout.GetViewport(new(args.GetProperty("viewport_id").GetInt64())) : null;
            if (viewport is { IsVisible: false }) throw new InvalidOperationException("A hidden viewport cannot be activated.");
            session.ActivateLayout(layout.Id);
            if (viewport is not null) session.ActivateLayoutViewport(viewport.Id);
        }
        return Success(new { document_id = document.DocumentId, result = LayoutsDto(session) });
    }

    private static object LayoutsDto(ICadToolDocumentSession session) => new
    {
        active_layout_id = session.ActiveLayoutId?.Value, active_viewport_id = session.ActiveLayoutViewportId?.Value,
        active_owner_block_id = session.CadEditor.ActiveOwnerBlockId.Value,
        layouts = session.CadEditor.Document.Layouts.Values.Select(layout => new
        {
            layout_id = layout.Id.Value, name = layout.Name, owner_block_id = layout.PaperSpaceBlockId.Value,
            width = layout.PaperWidth, height = layout.PaperHeight, margin_left = layout.MarginLeft, margin_top = layout.MarginTop,
            margin_right = layout.MarginRight, margin_bottom = layout.MarginBottom, color = ColorText(layout.PaperColor),
            viewports = layout.Viewports.Select(viewport => new
            {
                viewport_id = viewport.Id.Value, x = viewport.Bounds.MinX, y = viewport.Bounds.MinY,
                width = viewport.Bounds.Width, height = viewport.Bounds.Height, model_center_x = viewport.ModelCenter.X,
                model_center_y = viewport.ModelCenter.Y, scale = viewport.Scale, rotation_degrees = viewport.RotationRadians * 180 / Math.PI,
                visible = viewport.IsVisible, locked = viewport.IsLocked
            }).ToArray()
        }).ToArray()
    };

    private static object ReassociateDimension(CadDocumentToolExecutor executor, JsonElement args)
    {
        var document = executor.Session.CadEditor.Document;
        var dimension = executor.GetEntityForTool(RequiredEntityId(args)) as CadDimension ?? throw new ArgumentException("entity_id must be a dimension.");
        CadEntityAccessPolicy.EnsureEditable(document, dimension);
        CadDimensionAnchor Anchor(CadEntity entity, CadReferenceFeature feature, double parameter = 0)
        {
            var reference = new CadGeometryReference(entity.Id.Value, entity.OwnerBlockId.Value, feature, parameter,
                entity is CadPolyline polyline ? polyline.Points.Count : 0);
            if (!reference.TryResolve(document, out var point)) throw new ArgumentException("The explicit reference does not resolve to this source geometry.");
            return new(point, reference);
        }
        CadDimensionAnchor[] anchors;
        if (args.TryGetProperty("source_entity_id", out var source))
        {
            var entity = executor.GetEntityForTool(new(source.GetInt64()));
            anchors = entity switch
            {
                CadLine when dimension.Definition.Kind is CadDimensionKind.LinearX or CadDimensionKind.LinearY or CadDimensionKind.Aligned =>
                    [Anchor(entity, CadReferenceFeature.LineStart), Anchor(entity, CadReferenceFeature.LineEnd)],
                CadCircle or CadArc when dimension.Definition.Kind is CadDimensionKind.Radius or CadDimensionKind.Diameter =>
                    [Anchor(entity, CadReferenceFeature.CircleCenter), Anchor(entity, CadReferenceFeature.CirclePoint)],
                _ => throw new ArgumentException("Source geometry does not match the dimension kind. Use explicit references for other kinds.")
            };
        }
        else anchors = args.GetProperty("references").EnumerateArray().Select(item =>
        {
            var feature = Enum.Parse<CadReferenceFeature>(RequiredString(item, "feature"));
            if (item.TryGetProperty("angle_degrees", out _) && feature != CadReferenceFeature.CirclePoint)
                throw new ArgumentException("angle_degrees is only valid for CirclePoint.");
            if (item.TryGetProperty("vertex_index", out _) && feature != CadReferenceFeature.PolylineVertex)
                throw new ArgumentException("vertex_index is only valid for PolylineVertex.");
            var parameter = feature == CadReferenceFeature.PolylineVertex ? item.GetProperty("vertex_index").GetInt32() :
                PresentationNumber(item, "angle_degrees", 0) * Math.PI / 180;
            return Anchor(executor.GetEntityForTool(new(item.GetProperty("entity_id").GetInt64())), feature, parameter);
        }).ToArray();
        var definition = dimension.Definition with { Anchors = anchors };
        definition.Validate();
        executor.ExecuteCommand(new SetDimensionCommand(dimension.Id, definition));
        return new { entity_id = dimension.Id.Value, association = dimension.AssociationState.ToString(), measurement = dimension.Measurement, text = dimension.DisplayText };
    }

    private async Task<string> CaptureViewAsync(JsonElement args, CancellationToken token)
    {
        var document = ResolveDocument(args);
        var session = document.Session;
        var editor = session.CadEditor;
        var version = editor.DocumentChangeVersion;
        var owner = editor.ActiveOwnerBlockId;
        var size = args.TryGetProperty("maximum_size", out var element) ? element.GetInt32() : 1024;
        var image = await _workspace.CaptureViewAsync(document.DocumentId, size, token);
        token.ThrowIfCancellationRequested();
        if (session.IsDisposed || !ReferenceEquals(_workspace.GetRequiredDocument(document.DocumentId).Session, session) ||
            !ReferenceEquals(session.CadEditor, editor) || editor.DocumentChangeVersion != version || editor.ActiveOwnerBlockId != owner)
            throw new InvalidOperationException("The document changed while capturing. Capture again.");
        if (image.Data.Length is 0 or > AiToolResultContent.MaximumImageBytes || image.MimeType != "image/png" || image.Width <= 0 || image.Height <= 0 || image.Width > size || image.Height > size)
            throw new InvalidDataException("The rendering host returned an invalid or oversized PNG.");
        return Success(new { document_id = document.DocumentId, document_version = version, active_owner_block_id = owner.Value,
            image = new { mime_type = image.MimeType, data_base64 = Convert.ToBase64String(image.Data), width = image.Width, height = image.Height } });
    }

    private async Task<string> PrintDocumentAsync(JsonElement args, CancellationToken token)
    {
        var document = ResolveDocument(args);
        var submitted = await _workspace.PrintDocumentAsync(document.DocumentId, token);
        if (!submitted) token.ThrowIfCancellationRequested();
        return submitted ? Success(new { document_id = document.DocumentId, submitted = true, status = "submitted", completed = false }) :
            Direct2dCad.AI.Contracts.CadJson.Serialize(new { success = false, error = "Print preview was cancelled; no job was submitted.", code = "cancelled", document_id = document.DocumentId, submitted = false });
    }

    private static double PresentationNumber(JsonElement args, string key, double fallback) => args.TryGetProperty(key, out var value) ? value.GetDouble() : fallback;
    private static void RequirePresentationChange(JsonElement args, params string[] identifiers)
    {
        if (!args.EnumerateObject().Any(p => p.Name != "document_id" && !identifiers.Contains(p.Name)))
            throw new ArgumentException("Supply at least one setting to change.");
    }
}
