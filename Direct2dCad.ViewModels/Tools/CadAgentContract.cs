namespace Direct2dCad.ViewModels.Tools;

internal static class CadAgentContract
{
    internal const string Version = "1.2";

    internal static object CreateCapabilities(
        IReadOnlyList<CadToolWorkspaceDocument> documents,
        string? defaultDocumentId,
        bool includeExamples)
    {
        var active = documents.FirstOrDefault(document => document.IsActive);
        return new
        {
            contract_version = Version,
            purpose = "Use CAD tools to inspect, plan, modify, and verify a Direct2dCad drawing.",
            current_workspace = new
            {
                default_document_id = defaultDocumentId,
                active_document_id = active?.DocumentId,
                active_document_space = active is null ? null : DescribeActiveSpace(active),
                documents = documents.Select(document => new
                {
                    document_id = document.DocumentId,
                    document.Name,
                    document.IsModified,
                    document.IsActive,
                    active_space = DescribeActiveSpace(document)
                }).ToArray()
            },
            rules = new
            {
                coordinates = "World coordinates use +X to the right and +Y upward. Angles are counter-clockwise degrees.",
                document_selection = "Use document_id whenever the user names a document. Never create a document unless explicitly requested.",
                editable_space = "document_id selects the document tab; the current active_space selects the editable owner. Mutations affect only that active model space, paper space, layout viewport model space, or block-edit space. Query scope=document is read-only across spaces.",
                undo = "All document mutations are undoable. Mutations in one request share one undo batch per document.",
                selection_fallback = "Only move_entities, transform_entities, delete_entities, duplicate_entities, and create_block may use the current selection when entity_ids is omitted.",
                appearance = "color and graphic_style are mutually exclusive. By-layer color and line weight resolve from the entity layer. Shared Text and Graphic styles can be changed with their dedicated undoable tools.",
                fill = "Fill mode none clears fill; style requires an existing style; solid and hatch accept optional colors; hatch defaults to ANSI31 when pattern is omitted; gradient requires stops with offsets 0 and 1.",
                line_types = "Only LineTypeId.Continuous is currently rendered and editable through the Agent tools. Other persisted IDs are reported but are not claimed to have visual effect.",
                locking = "locked is a common entity property. Locked entities cannot be edited; unlocking remains allowed and is undoable. A lock requested together with other properties is applied last.",
                deletion = "delete_entities requires confirm=true. delete_layer requires delete_entities=true and confirm=true.",
                query = "get_entity_statistics is complete and unpaged. list_entities is paged; use total_matches and has_more and never assume one page is the whole drawing.",
                verification = "After a mutation, inspect the tool result and query the document when the operation is complex or batch-sized."
            },
            common_entity_properties = new[]
            {
                "layer", "name", "color_source", "color", "graphic_style",
                "line_weight", "z_index", "visible", "locked"
            },
            workflow = new[]
            {
                "1. Read current workspace and document state.",
                "2. Query entities, layers, or styles before editing existing content.",
                "3. For multi-part drawings, prefer add_entities so the request is one coherent undo batch.",
                "4. Execute the smallest suitable mutation tool.",
                "5. Verify returned IDs and summarize only confirmed changes."
            },
            tool_groups = new
            {
                inspect = new[] { "get_agent_capabilities", "get_document_summary", "get_entity_statistics", "list_entities", "list_document_catalog" },
                create = new[] { "add_line", "add_circle", "add_arc", "add_ellipse", "add_rectangle", "add_polygon", "add_polyline", "add_spline", "add_composite_path", "add_shape_text", "add_text", "add_entities", "insert_image_from_file", "add_ole_object" },
                geometry = new[] { "get_entity_geometry", "set_entity_geometry", "transform_entities", "duplicate_entities", "move_entities" },
                appearance = new[] { "set_entity_common_properties", "set_entity_fill", "set_entity_stroke_style", "set_entity_specific_properties", "set_ole_object_data", "set_text_style_properties", "set_graphic_style_properties" },
                organization = new[] { "list_layers", "create_layer", "rename_layer", "delete_layer", "set_layer_properties", "reorder_layers", "create_block", "insert_block" },
                history = new[] { "undo", "redo" },
                workspace = new[] { "list_documents", "create_document", "open_document", "activate_document", "rename_document", "save_document", "close_document" }
            },
            entity_capabilities = EntityCapabilities(),
            examples = includeExamples ? Examples() : Array.Empty<object>()
        };
    }

    private static object? DescribeActiveSpace(CadToolWorkspaceDocument document)
    {
        // Test and headless workspaces may expose a descriptor without an editor tab.
        if (document.EditorTab is null)
            return null;

        var viewModel = document.DocumentViewModel;
        var editor = viewModel.CadEditor;
        var cadDocument = editor.Document;
        var ownerBlockId = editor.ActiveOwnerBlockId;
        var ownerBlock = cadDocument.TryGetBlock(ownerBlockId, out var block) && block is not null
            ? block
            : null;
        var kind = viewModel.IsEditingBlock
            ? "block_edit"
            : viewModel.IsModelSpaceActive
                ? "model_space"
                : viewModel.IsLayoutViewportActive
                    ? "layout_viewport_model_space"
                    : "paper_space";
        var layout = viewModel.ActiveLayoutId is { } layoutId
            ? cadDocument.GetLayout(layoutId)
            : null;

        return new
        {
            kind,
            owner_block_id = ownerBlockId.Value,
            owner_block_name = ownerBlock?.Name ?? ownerBlockId.ToString(),
            selected_entity_ids = editor.Selection.EntityIds.Select(id => id.Value).ToArray(),
            drawing_layer_id = viewModel.DrawingLayerId.Value,
            drawing_layer = cadDocument.GetLayer(viewModel.DrawingLayerId).Name,
            editing_block_id = viewModel.EditingBlockId?.Value,
            editing_block_name = viewModel.IsEditingBlock ? viewModel.EditingBlockName : null,
            layout_id = layout?.Id.Value,
            layout_name = layout?.Name,
            viewport_id = viewModel.ActiveLayoutViewportId?.Value,
            is_model_space = viewModel.IsModelSpaceActive || viewModel.IsLayoutViewportActive,
            is_paper_space = viewModel.IsPaperSpaceActive
        };
    }

    private static object[] Examples() =>
    [
        new
        {
            intent = "Create one styled circle",
            tool = "add_circle",
            arguments = new
            {
                center_x = 10,
                center_y = 20,
                radius = 5,
                color = "#FF00FF00",
                line_weight = 0.25,
                fill = new { mode = "solid", color = "#402080FF" }
            }
        },
        new
        {
            intent = "Create a multi-entity outline",
            tool = "add_entities",
            arguments = new
            {
                entities = new[]
                {
                    new { type = "line", x1 = 0, y1 = 0, x2 = 10, y2 = 0 },
                    new { type = "line", x1 = 10, y1 = 0, x2 = 10, y2 = 10 },
                    new { type = "line", x1 = 10, y1 = 10, x2 = 0, y2 = 10 }
                }
            }
        },
        new
        {
            intent = "Find every spline in the current space",
            tool = "list_entities",
            arguments = new { scope = "current_space", type = "Spline", limit = 200 }
        },
        new
        {
            intent = "Delete selected entities",
            tool = "delete_entities",
            arguments = new { confirm = true }
        }
    ];

    private static object[] EntityCapabilities() =>
    [
        new { type = "Line", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "grip_handles" }, conditions = new { start_end_caps = "Always supported for a line." } },
        new { type = "Circle", supports = new[] { "graphic_style", "stroke_style", "fill", "grip_handles" }, conditions = new { fill = "The circle is closed and can use none, solid, hatch, or gradient fill." } },
        new { type = "Arc", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "grip_handles" }, conditions = new { start_end_caps = "Supported only when the arc is not a full circle." } },
        new { type = "Ellipse", supports = new[] { "graphic_style", "stroke_style", "fill", "grip_handles" }, conditions = new { fill = "The ellipse is closed and can use none, solid, hatch, or gradient fill." } },
        new { type = "EllipseArc", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "grip_handles" }, conditions = new { start_end_caps = "Supported because an ellipse arc is open." } },
        new { type = "Rectangle", supports = new[] { "graphic_style", "stroke_style", "line_join", "fill", "grip_handles" }, conditions = new { fill = "The rectangle is closed and can use none, solid, hatch, or gradient fill." } },
        new { type = "Polyline", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill", "grip_handles" }, conditions = new { start_end_caps = "Only for an open polyline.", fill = "Only for a closed polyline." } },
        new { type = "Spline", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill", "grip_handles" }, conditions = new { start_end_caps = "Only for an open spline.", fill = "Only for a closed spline." } },
        new { type = "CompositePath", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill", "grip_handles" }, conditions = new { start_end_caps = "Only for an open composite path.", fill = "Only for a closed composite path." } },
        new { type = "Text", supports = new[] { "graphic_style", "rotation", "text_content", "text_style", "font_family", "inverted", "inverted_margin_factor", "grip_handles" }, conditions = new { stroke_style = "Text uses its graphic style; entity StrokeStyle is not applicable." } },
        new { type = "ShapeText", supports = new[] { "graphic_style", "rotation", "text_content", "shape_font", "inverted", "inverted_margin_factor", "grip_handles" }, conditions = new { stroke_style = "ShapeText uses its graphic style; entity StrokeStyle is not applicable." } },
        new { type = "Image", supports = new[] { "opacity", "rotation", "rotation_handle", "embedded_content", "grip_handles" }, conditions = new { embedded_content = "Use insert_image_from_file to create it; geometry changes do not replace pixel data." } },
        new { type = "OleObject", supports = new[] { "opacity", "embedded_content", "grip_handles" }, conditions = new { embedded_content = "Use add_ole_object or set_ole_object_data with persisted OLE bytes; opening the OLE UI is a host operation." } },
        new { type = "BlockReference", supports = new[] { "graphic_style", "rotation", "rotation_handle", "grip_handles" }, conditions = new { graphic_style = "Applies to the reference; definition entities retain their own styles." } }
    ];
}
