namespace Direct2dCad.Application.Tools;

internal static class CadAgentContract
{
    internal const string Version = "1.8";

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
                coordinates = "World coordinates use +X to the right and +Y upward. Geometry tool inputs and outputs use canonical millimetres regardless of the display unit. Angles are counter-clockwise degrees.",
                view_settings = "Use get_view_settings to inspect the status-bar unit, grid, snap marker, origin display, background, precision, and viewport settings. Use set_view_settings to change document display settings, manage_grid_presets to list/create/rename/delete named grid presets, and set_viewport to fit, fit_bounds, zoom, zoom_entity, pan, or center the active viewport. Display-unit values in the view-settings result are for UI presentation; geometry and measurement coordinates remain in canonical millimetres.",
                document_selection = "Use document_id whenever the user names a document. Never create a document unless explicitly requested.",
                editable_space = "document_id selects the document tab; the current active_space selects the editable owner. Mutations affect only that active model space, paper space, layout viewport model space, or block-edit space. Query scope=document is read-only across spaces.",
                undo = "Document mutations and document view settings are undoable with undo/redo. Viewport and selection changes use the separate editor history and are undone with undo_view/redo_view. Mutations in one request share one undo batch per document.",
                selection_fallback = "Only move_entities, transform_entities, delete_entities, duplicate_entities, and create_block may use the current selection when entity_ids is omitted.",
                appearance = "color and graphic_style are mutually exclusive. By-layer color and line weight resolve from the entity layer. Shared Text and Graphic styles can be changed with their dedicated undoable tools.",
                fill = "Fill mode none clears fill; style requires an existing style; solid and hatch accept optional colors; hatch defaults to ANSI31 when pattern is omitted; gradient requires stops with offsets 0 and 1.",
                creation = "Each add_* tool accepts only the geometry fields for its concrete entity. add_entities uses one compact shared schema, but the executor rejects incompatible geometry, fill, and text-specific fields before creating anything; unsupported stroke fields are filtered after the concrete entity is created.",
                line_types = "Line types are document resources. Continuous is always available; custom line types expose their dash pattern and are rendered by the Direct2D stroke cache. Use list_styles to inspect line types before assigning an ID.",
                stroke_caps = "start_cap and end_cap apply only to open curves. Never send them for circles, ellipses, rectangles, polygons, or closed polylines/splines/composite paths. dash_cap and dash_style apply to stroked curves; line_join applies only to rectangle, polygon, polyline, spline, and composite-path entities. add_entities ignores unsupported stroke fields per entity so a mixed batch remains atomic and drawable.",
                transforms = "move supports all editable entities. rotate supports arbitrary finite angles, including oriented Ellipse, EllipseArc and Rectangle; OleObject rotation is unsupported. scale requires a positive factor. mirror supports arbitrary finite axes except OleObject requires horizontal or vertical axes. The tool validates every selected entity before changing any of them.",
                curve_editing = "edit_curves shares exact pointer-preview plans: offset/trim/extend/join/break support line and circular-arc paths. fillet/chamfer accept two individual curves, endpoint segments of different open paths, or one path ID with two adjacent-segment picks. all_corners=true applies to every sharp corner of one open/closed path without picks. Full circles require trim=false for corners; a broken circle becomes one Arc even across its parameter seam. Unsupported splines/ellipses fail explicitly. Type conversions, split/join return new IDs and never guess reference rebinding. array_entities creates rectangular/polar copies in one undoable command, with a 10000-entity budget.",
                boolean_regions = "boolean_regions requires at least two explicit closed operand IDs. difference also requires subject_entity_id among the operands. Operands must be editable in the current owner space. Exact line/circular boundaries and Regions are supported; splines, ellipses and rounded rectangles are unsupported. Empty or failed results retain the sources. A successful result replaces operands with one Region ID; query its exact contours to verify.",
                snapping = "set_view_settings exposes independent grid_snap_enabled, object_snap_enabled, ortho_enabled, polar_enabled, polar_increment_degrees, snap_screen_tolerance and object_snap_modes. Explicit coordinates bypass pointer constraints.",
                annotations = "add_dimension creates a single undoable linear, aligned, radius, diameter, angular or leader annotation. source_entity_id binds direct line/circle/arc geometry; explicit anchors are detached. set_dimension updates placement, font, arrow, preset, lettering sizes, unit, precision, scale, rotation and text_override while preserving references. null text_override restores measurement text. detach_dimension explicitly detaches a dimension; undo restores association. Broken references retain the last accepted measurement and are never rebound by proximity. Rotation, scale and mirror detach association; undo restores it.",
                locking = "locked is a common entity property. Locked entities cannot be edited; unlocking remains allowed and is undoable. A lock requested together with other properties is applied last.",
                deletion = "delete_entities requires confirm=true. delete_layer requires delete_entities=true and confirm=true.",
                query = "get_entity_statistics is complete and unpaged. list_entities is paged; use total_matches and has_more. Queries use independent snapshots and reject stale or closed-document results. measure_geometry uses analytic line/circular geometry and adaptive complex curves, with approximate/error/budget metadata. Intersection tessellation error is a curve-distance bound, not an intersection-coordinate bound near tangency.",
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
                inspect = new[] { "get_agent_capabilities", "get_document_summary", "get_view_settings", "get_entity_statistics", "list_entities", "list_document_catalog", "measure_geometry" },
                create = new[] { "add_line", "add_circle", "add_arc", "add_ellipse", "add_rectangle", "add_polygon", "add_polyline", "add_spline", "add_composite_path", "add_shape_text", "add_text", "add_entities", "insert_image_from_file", "add_ole_object" },
                geometry = new[] { "get_entity_geometry", "set_entity_geometry", "transform_entities", "duplicate_entities", "move_entities", "measure_geometry", "edit_curves", "array_entities", "boolean_regions" },
                appearance = new[] { "set_entity_common_properties", "set_entity_fill", "set_entity_stroke_style", "set_entity_specific_properties", "set_ole_object_data", "set_text_style_properties", "set_graphic_style_properties", "list_styles", "create_graphic_style", "create_line_type", "rename_line_type", "delete_line_type", "create_text_style", "create_fill_style", "create_hatch_pattern", "rename_style", "delete_style", "delete_hatch_pattern", "list_system_fonts" },
                organization = new[] { "list_layers", "create_layer", "rename_layer", "delete_layer", "set_layer_properties", "reorder_layers", "create_block", "insert_block", "list_blocks", "rename_block", "delete_block", "edit_block", "exit_block_edit" },
                history = new[] { "undo", "redo", "undo_view", "redo_view" },
                selection = new[] { "select_entities", "select_by_bounds", "select_by_polygon", "select_by_filter", "clear_selection" },
                view = new[] { "get_view_settings", "set_view_settings", "manage_grid_presets", "set_viewport", "set_drawing_layer" },
                annotations = new[] { "add_dimension", "set_dimension", "detach_dimension" },
                exchange = new[] { "open_dxf", "export_dxf" },
                workspace = new[] { "list_documents", "create_document", "open_document", "activate_document", "rename_document", "save_document", "close_document" }
            },
            entity_capabilities = EntityCapabilities(),
            examples = includeExamples ? Examples() : Array.Empty<object>()
        };
    }

    private static object? DescribeActiveSpace(CadToolWorkspaceDocument document)
    {
        // Test and headless workspaces may expose a descriptor without an editor tab.
        if (document.Host is null)
            return null;

        var viewModel = document.Session;
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
        new { intent = "Subtract closed operands from an explicit subject", tool = "boolean_regions",
            arguments = new { operation = "difference", entity_ids = new[] { 10, 11 }, subject_entity_id = 10 } },
        new { intent = "Change dimension lettering and arrows", tool = "set_dimension",
            arguments = new { entity_id = 12, shape_font = "simplex", arrow = "Closed", text_height = 3.0 } },
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
        new { type = "Region", supports = new[] { "graphic_style", "stroke_style", "fill", "grip_handles", "transform", "boolean_regions", "get_entity_geometry" }, conditions = new { geometry = "Exact line/circular contours may contain holes or disconnected islands. get_entity_geometry returns every contour and edge; set_entity_geometry does not replace Region contours." } },
        new { type = "Dimension", supports = new[] { "set_dimension", "detach_dimension", "grip_handles", "transform", "get_entity_geometry" }, conditions = new { geometry = "Placement, lettering and arrows use set_dimension. Anchor references are preserved by property edits; rotation/scale/mirror explicitly detach. Broken references never auto-rebind." } },
        new { type = "Line", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "grip_handles", "transform" }, conditions = new { start_end_caps = "Always supported for a line.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "Circle", supports = new[] { "graphic_style", "stroke_style", "fill", "grip_handles", "transform" }, conditions = new { fill = "The circle is closed and can use none, solid, hatch, or gradient fill.", transform = "Rotation and mirror affect the center; uniform scale affects center and radius." } },
        new { type = "Arc", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "grip_handles", "transform" }, conditions = new { start_end_caps = "Supported only when the arc is not a full circle.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "Ellipse", supports = new[] { "graphic_style", "stroke_style", "fill", "grip_handles", "transform" }, conditions = new { fill = "The ellipse is closed and can use none, solid, hatch, or gradient fill.", transform = "Arbitrary rotation and mirror, positive uniform scale, and move preserve exact orientation." } },
        new { type = "EllipseArc", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "grip_handles", "transform" }, conditions = new { start_end_caps = "Supported because an ellipse arc is open.", transform = "Move, arbitrary rotation and mirror, and positive uniform scale preserve oriented ellipse arc geometry." } },
        new { type = "Rectangle", supports = new[] { "graphic_style", "stroke_style", "line_join", "fill", "grip_handles", "transform" }, conditions = new { fill = "The rectangle is closed and can use none, solid, hatch, or gradient fill.", transform = "Move, arbitrary rotation and mirror, and positive uniform scale preserve the rectangle frame." } },
        new { type = "Polyline", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill", "grip_handles", "transform" }, conditions = new { start_end_caps = "Only for an open polyline.", fill = "Only for a closed polyline.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "Spline", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill", "grip_handles", "transform" }, conditions = new { start_end_caps = "Only for an open spline.", fill = "Only for a closed spline.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "CompositePath", supports = new[] { "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill", "grip_handles", "transform" }, conditions = new { start_end_caps = "Only for an open composite path.", fill = "Only for a closed composite path.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "Text", supports = new[] { "graphic_style", "rotation", "text_content", "text_style", "font_family", "inverted", "inverted_margin_factor", "grip_handles", "transform" }, conditions = new { stroke_style = "Text uses its graphic style; entity StrokeStyle is not applicable.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "ShapeText", supports = new[] { "graphic_style", "rotation", "text_content", "shape_font", "inverted", "inverted_margin_factor", "grip_handles", "transform" }, conditions = new { stroke_style = "ShapeText uses its graphic style; entity StrokeStyle is not applicable.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "Image", supports = new[] { "opacity", "rotation", "rotation_handle", "embedded_content", "grip_handles", "transform" }, conditions = new { embedded_content = "Use insert_image_from_file to create it; geometry changes do not replace pixel data.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } },
        new { type = "OleObject", supports = new[] { "opacity", "embedded_content", "grip_handles", "transform" }, conditions = new { embedded_content = "Use add_ole_object or set_ole_object_data with persisted OLE bytes; opening the OLE UI is a host operation.", transform = "Move and positive uniform scale are supported; rotate is not supported and mirror axes must be horizontal or vertical." } },
        new { type = "BlockReference", supports = new[] { "graphic_style", "rotation", "rotation_handle", "grip_handles", "transform" }, conditions = new { graphic_style = "Applies to the reference; definition entities retain their own styles.", transform = "Move, rotate, positive uniform scale, and mirror are supported." } }
    ];
}
