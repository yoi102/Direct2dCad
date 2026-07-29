using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.AI;

internal static class CadAiEditorStateBuilder
{
    private const int MaximumSelectedEntities = 100;

    internal static object Build(CadDocumentViewModel viewModel)
    {
        var editor = viewModel.CadEditor;
        var document = editor.Document;
        var currentEntities = document.GetEntitiesInBlock(editor.ActiveOwnerBlockId)
            .Where(entity => !entity.IsErased)
            .ToArray();
        var selectedEntities = editor.Selection.EntityIds
            .Select(id => document.TryGetEntity(id, out var entity) ? entity : null)
            .Where(entity => entity is not null && !entity.IsErased)
            .Cast<CadEntity>()
            .Take(MaximumSelectedEntities)
            .Select(entity => EntityDto(document, entity))
            .ToArray();
        var drawingLayer = document.GetLayer(viewModel.DrawingLayerId);
        var settings = document.ViewSettings;
        var grid = settings.Grid;
        var origin = settings.Origin;
        var activeLayout = viewModel.ActiveLayoutId is { } layoutId
            ? document.GetLayout(layoutId)
            : null;
        var activeLayoutViewport = activeLayout is not null &&
                                   viewModel.ActiveLayoutViewportId is { } viewportId
            ? activeLayout.GetViewport(viewportId)
            : null;

        return new
        {
            document = new
            {
                cad_document_id = document.Id.Value,
                document.Name,
                active_owner_block_id = editor.ActiveOwnerBlockId.Value,
                space_kind = ResolveSpaceKind(viewModel),
                editing_block = viewModel.EditingBlockId is { } blockId
                    ? new { id = blockId.Value, name = viewModel.EditingBlockName }
                    : null,
                active_layout = activeLayout is null
                    ? null
                    : new
                    {
                        id = activeLayout.Id.Value,
                        activeLayout.Name,
                        paper_space_block_id = activeLayout.PaperSpaceBlockId.Value,
                        paper_width = activeLayout.PaperWidth,
                        paper_height = activeLayout.PaperHeight
                    },
                active_layout_viewport = activeLayoutViewport is null
                    ? null
                    : new
                    {
                        id = activeLayoutViewport.Id.Value,
                        bounds = RectDto(activeLayoutViewport.Bounds),
                        model_center = PointDto(activeLayoutViewport.ModelCenter),
                        activeLayoutViewport.Scale,
                        rotation_degrees = activeLayoutViewport.RotationRadians * 180.0 / Math.PI,
                        activeLayoutViewport.IsLocked
                    }
            },
            interaction = new
            {
                tool_mode = viewModel.CadCanvasToolMode.ToString(),
                selection_count = editor.Selection.EntityIds.Count,
                selection_truncated = editor.Selection.EntityIds.Count > selectedEntities.Length,
                selected_entities = selectedEntities,
                can_undo = editor.DocumentCommands.CanUndo,
                can_redo = editor.DocumentCommands.CanRedo,
                is_panning = viewModel.IsPanning,
                is_paste_preview_active = viewModel.IsPastePreviewActive
            },
            viewport = new
            {
                editor.Viewport.Zoom,
                offset = PointDto(editor.Viewport.Offset),
                width_pixels = editor.Viewport.ViewWidth,
                height_pixels = editor.Viewport.ViewHeight,
                visible_world_bounds = RectDto(editor.Viewport.VisibleWorldBounds)
            },
            drawing_layer = new
            {
                id = drawingLayer.Id.Value,
                drawingLayer.Name,
                color = ColorText(drawingLayer.Color),
                line_weight = drawingLayer.LineWeight.IsByLayer
                    ? (object)"by_layer"
                    : drawingLayer.LineWeight.Value,
                drawingLayer.IsVisible,
                drawingLayer.IsLocked,
                drawingLayer.IsFrozen
            },
            drawing_defaults = CreateDrawingDefaultsDto(viewModel),
            view_settings = new
            {
                background_color = ColorText(settings.BackgroundColor),
                grid = new
                {
                    type = ProtocolEnum(grid.Type),
                    major_spacing_x = grid.SpacingX,
                    major_spacing_y = grid.SpacingY,
                    minor_spacing_x = grid.GetMinorSpacingX(),
                    minor_spacing_y = grid.GetMinorSpacingY(),
                    snap_spacing_x = grid.GetSnapSpacingX(),
                    snap_spacing_y = grid.GetSnapSpacingY(),
                    snap_marker_type = ProtocolEnum(grid.SnapMarkerType)
                },
                origin = new
                {
                    position = PointDto(origin.Position),
                    display_type = ProtocolEnum(origin.DisplayType),
                    marker_type = ProtocolEnum(origin.MarkerType)
                }
            },
            current_space = new
            {
                entity_count = currentEntities.Length,
                entity_types = currentEntities
                    .GroupBy(EntityType)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new { type = group.Key, count = group.Count() })
                    .ToArray(),
                content_bounds = RectDto(currentEntities.Aggregate(
                    CadRectD.Empty,
                    (bounds, entity) => bounds.Union(entity.Bounds)))
            },
            capabilities = new
            {
                maximum_entities_per_bulk_call = 200,
                composite_path_segments = new[] { "line", "arc", "cubic_bezier", "spline" },
                coordinates = "+X right, +Y up",
                angle_unit = "counter-clockwise degrees"
            }
        };
    }

    private static object EntityDto(CadDocument document, CadEntity entity) => new
    {
        id = entity.Id.Value,
        type = EntityType(entity),
        entity.Name,
        layer_id = entity.LayerId.Value,
        layer = document.TryGetLayer(entity.LayerId, out var layer)
            ? layer?.Name
            : null,
        bounds = RectDto(entity.Bounds),
        entity.IsVisible,
        entity.IsLocked
    };

    private static string ResolveSpaceKind(CadDocumentViewModel viewModel)
    {
        if (viewModel.IsEditingBlock)
            return "block_definition";
        if (viewModel.IsLayoutViewportActive)
            return "layout_model_viewport";
        if (viewModel.IsPaperSpaceActive)
            return "paper_space";
        return "model_space";
    }

    private static object[] CreateDrawingDefaultsDto(CadDocumentViewModel viewModel)
    {
        var defaults = viewModel.DrawingDefaults;
        object Item(
            string type,
            CadColor color,
            bool useLayerColor,
            double lineWeight,
            bool useLayerLineWeight,
            int zIndex,
            bool visible,
            bool? closed = null,
            StyleId? fillStyleId = null) => new
        {
            type,
            color_source = useLayerColor ? "by_layer" : "explicit",
            color = ColorText(color),
            line_weight = useLayerLineWeight ? (object)"by_layer" : lineWeight,
            z_index = zIndex,
            visible,
            closed,
            fill_style_id = fillStyleId?.Value
        };

        return
        [
            Item("line", defaults.LineStrokeColor, defaults.LineUseLayerColor,
                defaults.LineLineWeight, defaults.LineUseLayerLineWeight,
                defaults.LineZIndex, defaults.LineIsVisible),
            Item("polyline", defaults.PolylineStrokeColor, defaults.PolylineUseLayerColor,
                defaults.PolylineLineWeight, defaults.PolylineUseLayerLineWeight,
                defaults.PolylineZIndex, defaults.PolylineIsVisible,
                defaults.PolylineClosed, defaults.PolylineFillStyleId),
            Item("polygon", defaults.PolygonStrokeColor, defaults.PolygonUseLayerColor,
                defaults.PolygonLineWeight, defaults.PolygonUseLayerLineWeight,
                defaults.PolygonZIndex, defaults.PolygonIsVisible,
                true, defaults.PolygonFillStyleId),
            Item("spline", defaults.SplineStrokeColor, defaults.SplineUseLayerColor,
                defaults.SplineLineWeight, defaults.SplineUseLayerLineWeight,
                defaults.SplineZIndex, defaults.SplineIsVisible,
                defaults.SplineClosed, defaults.SplineFillStyleId),
            Item("circle", defaults.CircleStrokeColor, defaults.CircleUseLayerColor,
                defaults.CircleLineWeight, defaults.CircleUseLayerLineWeight,
                defaults.CircleZIndex, defaults.CircleIsVisible,
                true, defaults.CircleFillStyleId),
            Item("ellipse", defaults.EllipseStrokeColor, defaults.EllipseUseLayerColor,
                defaults.EllipseLineWeight, defaults.EllipseUseLayerLineWeight,
                defaults.EllipseZIndex, defaults.EllipseIsVisible,
                true, defaults.EllipseFillStyleId),
            Item("rectangle", defaults.RectangleStrokeColor, defaults.RectangleUseLayerColor,
                defaults.RectangleLineWeight, defaults.RectangleUseLayerLineWeight,
                defaults.RectangleZIndex, defaults.RectangleIsVisible,
                true, defaults.RectangleFillStyleId),
            Item("text", defaults.TextStrokeColor, defaults.TextUseLayerColor,
                defaults.TextLineWeight, defaults.TextUseLayerLineWeight,
                defaults.TextZIndex, defaults.TextIsVisible),
            Item("arc", defaults.ArcStrokeColor, defaults.ArcUseLayerColor,
                defaults.ArcLineWeight, defaults.ArcUseLayerLineWeight,
                defaults.ArcZIndex, defaults.ArcIsVisible)
        ];
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

    private static object PointDto(CadPointD point) => new { x = point.X, y = point.Y };

    private static object RectDto(CadRectD rect) => rect.IsEmpty
        ? new { empty = true }
        : new { min_x = rect.MinX, min_y = rect.MinY, max_x = rect.MaxX, max_y = rect.MaxY };

    private static string ColorText(CadColor color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ProtocolEnum<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().SelectMany((character, index) =>
            index > 0 && char.IsUpper(character)
                ? new[] { '_', char.ToLowerInvariant(character) }
                : new[] { char.ToLowerInvariant(character) }));
}
