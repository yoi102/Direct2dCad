using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;
using static Direct2dCad.ViewModels.Services.Geometry.CadGripDragGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal readonly struct CadGripDragPreviewBuilder(
    CadEditor editor,
    CadPreviewStyleService styleService,
    CadTextMeasurementService textMeasurementService)
{
    public void AddPreview(List<CadTransientItem> items, GripDragState? drag)
    {
        if (drag is null ||
            !editor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return;
        }

        if (drag.Handle.Type == CadHandleType.Center)
        {
            AddMoveGripPreview(items, drag);
            return;
        }

        var style = styleService.CreateEntityPreviewStyle(entity);
        var auxiliaryStyle = styleService.CreateGripAuxiliaryStyle();
        switch (entity)
        {
            case CadLine line:
                AddLineGripPreview(items, line, drag, style);
                break;

            case CadCircle circle:
                AddCircleGripPreview(items, circle, drag, style, auxiliaryStyle);
                break;

            case CadEllipse ellipse:
                AddEllipseGripPreview(items, ellipse, drag, style, auxiliaryStyle);
                break;

            case CadArc arc:
                AddArcGripPreview(items, arc, drag, style, auxiliaryStyle);
                break;

            case CadRectangle rectangle:
                AddRectangleGripPreview(items, rectangle, drag, style);
                break;

            case CadPolyline polyline:
                AddPolylineGripPreview(items, polyline, drag, style);
                break;

            case CadSpline spline:
                AddSplineGripPreview(items, spline, drag, style);
                break;

            case CadText text:
                AddTextGripPreview(items, text, drag, style, auxiliaryStyle);
                break;

            case CadShapeText shapeText:
                AddShapeTextGripPreview(items, shapeText, drag, style, auxiliaryStyle);
                break;

            case CadImage image:
                AddImageGripPreview(items, image, drag, style);
                break;

            case CadOleObject oleObject:
                AddOleObjectGripPreview(items, oleObject, drag, style);
                break;

            case CadBlockReference blockReference:
                AddBlockReferenceGripPreview(items, blockReference, drag, style);
                break;
        }
    }

    private void AddMoveGripPreview(
        List<CadTransientItem> items,
        GripDragState drag)
    {
        var previewItems = drag.MovePreviewItems;
        if (previewItems is null)
        {
            var createdItems = new List<CadTransientItem>(drag.HiddenEntityIds.Count);
            var previewBounds = CadRectD.Empty;
            CadTransientStyle? maximumStyle = null;
            foreach (var entityId in drag.HiddenEntityIds)
            {
                if (editor.Document.TryGetEntity(entityId, out var entity) &&
                    entity is not null &&
                    !entity.IsErased)
                {
                    var style = styleService.CreateEntityPreviewStyle(entity);
                    previewBounds = previewBounds.Union(entity.Bounds);
                    maximumStyle = ResolveMaximumPaddingStyle(maximumStyle, style);
                    if (entity is CadBlockReference reference)
                    {
                        createdItems.Add(new CadTransientBlockReference(
                            reference.DefinitionBlockId,
                            reference.Position,
                            reference.RotationRadians,
                            reference.ScaleX,
                            reference.ScaleY,
                            reference.LayerId,
                            reference.ColorSource,
                            reference.GraphicStyleId,
                            style));
                    }
                    else
                    {
                        createdItems.Add(new CadTransientEntityReference(
                            entityId,
                            CadVectorD.Zero,
                            style));
                    }
                }
            }

            previewItems = createdItems;
            drag.MovePreviewItems = previewItems;
            drag.MovePreviewBounds = previewBounds;
            drag.MovePreviewStyle = maximumStyle ?? default;
        }

        if (previewItems.Count > 0)
            items.Add(new CadTransientGroup(
                previewItems,
                CadMatrixD.CreateTranslation(drag.Delta),
                drag.MovePreviewStyle,
                drag.MovePreviewBounds));
    }

    private static CadTransientStyle ResolveMaximumPaddingStyle(
        CadTransientStyle? current,
        CadTransientStyle candidate)
    {
        if (current is not { } resolved)
            return candidate;

        return resolved with
        {
            StrokeWidth = Math.Max(resolved.StrokeWidth, candidate.StrokeWidth),
            MinimumScreenStrokeWidth = Math.Max(
                resolved.MinimumScreenStrokeWidth,
                candidate.MinimumScreenStrokeWidth),
            LinePattern = resolved.LinePattern == CadTransientLinePattern.Solid &&
                          candidate.LinePattern == CadTransientLinePattern.Solid
                ? CadTransientLinePattern.Solid
                : CadTransientLinePattern.Dash
        };
    }

    private static void AddLineGripPreview(
        List<CadTransientItem> items,
        CadLine line,
        GripDragState drag,
        CadTransientStyle style)
    {
        var moveStart = IsLineStartGrip(line, drag.Handle.Position);
        items.Add(new CadTransientLine(
            moveStart ? drag.DraggedGripPosition : line.Start,
            moveStart ? line.End : drag.DraggedGripPosition,
            style));
    }

    private static void AddCircleGripPreview(
        List<CadTransientItem> items,
        CadCircle circle,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius <= double.Epsilon)
            return;

        items.Add(new CadTransientCircle(circle.Center, radius, style));
        items.Add(new CadTransientLine(circle.Center, drag.DraggedGripPosition, auxiliaryStyle));
    }

    private static void AddEllipseGripPreview(
        List<CadTransientItem> items,
        CadEllipse ellipse,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateEllipseGripGeometry(ellipse, drag, out var center, out var radiusX, out var radiusY))
            return;

        items.Add(new CadTransientEllipse(center, radiusX, radiusY, style));
        items.Add(new CadTransientLine(center, drag.DraggedGripPosition, auxiliaryStyle));
    }

    private static void AddArcGripPreview(
        List<CadTransientItem> items,
        CadArc arc,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateArcGripGeometry(arc, drag, out var center, out var radius, out var startAngle, out var sweepAngle))
            return;

        items.Add(new CadTransientArc(center, radius, startAngle, sweepAngle, style));
        items.Add(new CadTransientLine(center, GetArcPoint(center, radius, startAngle), auxiliaryStyle));
        items.Add(new CadTransientLine(center, GetArcPoint(center, radius, startAngle + sweepAngle), auxiliaryStyle));
    }

    private static void AddRectangleGripPreview(
        List<CadTransientItem> items,
        CadRectangle rectangle,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreateRectangleGripGeometry(rectangle, drag, out var bounds))
            items.Add(new CadTransientRectangle(
                bounds,
                style,
                rectangle.CornerRadiusX,
                rectangle.CornerRadiusY));
    }

    private static void AddPolylineGripPreview(
        List<CadTransientItem> items,
        CadPolyline polyline,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreatePolylineGripGeometry(polyline, drag, out var points, out var closed))
            items.Add(new CadTransientPolyline(points, closed, style));
    }

    private static void AddSplineGripPreview(
        List<CadTransientItem> items,
        CadSpline spline,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreateSplineGripGeometry(spline, drag, out var fitPoints, out var closed))
            items.Add(new CadTransientSpline(fitPoints, closed, style));
    }

    private void AddTextGripPreview(
        List<CadTransientItem> items,
        CadText text,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var grid = editor.Document.ViewSettings.Grid;
        if (!TryCreateTextGripGeometry(
            text,
            drag,
            grid.GetSnapSpacingX(),
            grid.GetSnapSpacingY(),
            textMeasurementService,
            out var position,
            out var height))
        {
            return;
        }

        var bounds = textMeasurementService.CreateTextBounds(text.Text, position, height, text.TextStyleId);
        items.Add(new CadTransientText(
            text.Text,
            position,
            height,
            bounds,
            style,
            text.IsInverted,
            text.InvertedMarginFactor,
            text.TextStyleId,
            text.RotationRadians));
        items.Add(new CadTransientRectangle(
            text.IsInverted ? bounds.Inflate(height * text.InvertedMarginFactor) : bounds,
            auxiliaryStyle));
    }

    private static void AddShapeTextGripPreview(
        List<CadTransientItem> items,
        CadShapeText text,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateShapeTextGripGeometry(text, drag, out var position, out var height))
            return;

        items.Add(new CadTransientShapeText(
            text.Text,
            position,
            height,
            text.RotationRadians,
            text.WidthFactor,
            text.CharacterSpacingFactor,
            text.ObliqueAngleRadians,
            style,
            text.IsInverted,
            text.InvertedMarginFactor,
            text.ShapeFontId));
        items.Add(new CadTransientRectangle(
            CadTextMeasurementService.CreateShapeTextPreviewBounds(
                text.Text,
                position,
                height,
                text.WidthFactor,
                text.CharacterSpacingFactor,
                text.ObliqueAngleRadians,
                text.RotationRadians,
                text.IsInverted,
                text.InvertedMarginFactor,
                text.ShapeFontId),
            auxiliaryStyle));
    }

    private static void AddImageGripPreview(
        List<CadTransientItem> items,
        CadImage image,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (!TryCreateImageGripGeometry(image, drag, out var bounds, out var rotationRadians))
            return;

        items.Add(new CadTransientImage(
            bounds,
            image.PixelWidth,
            image.PixelHeight,
            image.Stride,
            image.CopyPixels(),
            style,
            image.Id,
            image.Opacity,
            rotationRadians));
    }

    private static void AddOleObjectGripPreview(
        List<CadTransientItem> items,
        CadOleObject oleObject,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (!TryCreateImageGripGeometry(oleObject.Bounds, drag, out var bounds))
            return;

        items.Add(new CadTransientOleObject(
            bounds,
            oleObject.CopyOleBytes(),
            style,
            oleObject.Id,
            Guid.Empty,
            oleObject.Opacity));
    }

    private void AddBlockReferenceGripPreview(
        List<CadTransientItem> items,
        CadBlockReference reference,
        GripDragState drag,
        CadTransientStyle style)
    {
        var definition = editor.Document.GetBlock(reference.DefinitionBlockId);
        if (!TryCreateBlockReferenceGripTransform(
                definition,
                reference,
                drag,
                out var position,
                out var rotationRadians,
                out var scaleX,
                out var scaleY))
        {
            position = reference.Position;
            rotationRadians = reference.RotationRadians;
            scaleX = reference.ScaleX;
            scaleY = reference.ScaleY;
        }

        items.Add(new CadTransientBlockReference(
            reference.DefinitionBlockId,
            position,
            rotationRadians,
            scaleX,
            scaleY,
            reference.LayerId,
            reference.ColorSource,
            reference.GraphicStyleId,
            style));
    }
}
