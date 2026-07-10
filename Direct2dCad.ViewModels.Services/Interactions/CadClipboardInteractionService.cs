using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadClipboardInteractionService(
    CadEditor editor)
{
    public CadClipboardSnapshot? CreateSelectionSnapshot()
    {
        if (editor.Selection.Count == 0)
            return null;

        return CadClipboardSnapshotFactory.Create(editor.Document, editor.Selection.EntityIds);
    }

    public void AddPastePreview(
        List<CadTransientItem> items,
        CadClipboardSnapshot? clipboard,
        bool isPastePreviewActive,
        CadPointD mouseWorld,
        LayerId targetLayerId)
    {
        if (!isPastePreviewActive || clipboard is null)
            return;

        var delta = mouseWorld - clipboard.BasePoint;
        var targetLayer = editor.Document.TryGetLayer(targetLayerId, out var resolvedLayer) && resolvedLayer is not null
            ? resolvedLayer
            : null;
        foreach (var item in clipboard.Items)
            AddPreviewItem(items, item, delta, targetLayer, editor.Document);
    }

    public IReadOnlyList<EntityId> CommitPaste(
        CadClipboardSnapshot clipboard,
        CadPointD target,
        LayerId targetLayerId)
    {
        var delta = target - clipboard.BasePoint;
        return editor.PasteEntities(clipboard, delta, targetLayerId);
    }

    private static void AddPreviewItem(
        List<CadTransientItem> items,
        CadClipboardEntityItem item,
        CadVectorD delta,
        CadLayer? targetLayer,
        CadDocument document)
    {
        var style = CreatePreviewStyle(item, targetLayer, document);
        switch (item.Entity)
        {
            case CadLineClipboardSnapshot line:
                items.Add(new CadTransientLine(line.Start + delta, line.End + delta, style));
                break;

            case CadCircleClipboardSnapshot circle:
                items.Add(new CadTransientCircle(circle.Center + delta, circle.Radius, style));
                break;

            case CadEllipseClipboardSnapshot ellipse:
                items.Add(new CadTransientEllipse(ellipse.Center + delta, ellipse.RadiusX, ellipse.RadiusY, style));
                break;

            case CadEllipseArcClipboardSnapshot ellipseArc:
                items.Add(new CadTransientEllipseArc(
                    ellipseArc.Center + delta,
                    ellipseArc.RadiusX,
                    ellipseArc.RadiusY,
                    ellipseArc.StartAngleRadians,
                    ellipseArc.SweepAngleRadians,
                    style));
                break;

            case CadArcClipboardSnapshot arc:
                items.Add(new CadTransientArc(
                    arc.Center + delta,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style));
                break;

            case CadRectangleClipboardSnapshot rectangle:
                items.Add(new CadTransientRectangle(
                    rectangle.Bounds.Translate(delta),
                    style,
                    rectangle.CornerRadiusX,
                    rectangle.CornerRadiusY));
                break;

            case CadPolylineClipboardSnapshot polyline:
                items.Add(new CadTransientPolyline(
                    polyline.Points.Select(x => x + delta).ToArray(),
                    polyline.Closed,
                    style));
                break;

            case CadSplineClipboardSnapshot spline:
                items.Add(new CadTransientSpline(
                    spline.FitPoints.Select(x => x + delta).ToArray(),
                    spline.Closed,
                    style));
                break;

            case CadTextClipboardSnapshot text:
                items.Add(new CadTransientText(
                    text.Text,
                    text.Position + delta,
                    text.Height,
                    text.LocalBounds.Translate(text.Position + delta - CadPointD.Origin),
                    style,
                    text.IsInverted,
                    text.InvertedMarginFactor,
                    null));
                break;

            case CadShapeTextClipboardSnapshot shapeText:
                items.Add(new CadTransientShapeText(
                    shapeText.Text,
                    shapeText.Position + delta,
                    shapeText.Height,
                    shapeText.RotationRadians,
                    shapeText.WidthFactor,
                    shapeText.CharacterSpacingFactor,
                    shapeText.ObliqueAngleRadians,
                    style,
                    shapeText.IsInverted,
                    shapeText.InvertedMarginFactor,
                    shapeText.ShapeFontId));
                break;

            case CadImageClipboardSnapshot image:
                items.Add(new CadTransientImage(
                    image.Bounds.Translate(delta),
                    image.PixelWidth,
                    image.PixelHeight,
                    image.Stride,
                    image.Pixels,
                    style));
                break;

            case CadOleObjectClipboardSnapshot oleObject:
                items.Add(new CadTransientOleObject(
                    oleObject.Bounds.Translate(delta),
                    oleObject.OleBytes,
                    style,
                    SourceEntityId: null,
                    oleObject.RenderId));
                break;
        }
    }

    private static CadTransientStyle CreatePreviewStyle(
        CadClipboardEntityItem item,
        CadLayer? targetLayer,
        CadDocument document)
    {
        var graphic = item.GraphicStyle as CadGraphicStyleClipboardSnapshot;
        var layerColor = targetLayer is not null
            ? ResolveLayerStrokeColor(document, targetLayer)
            : item.Layer.Color;
        var strokeColor = item.Entity.State.UseLayerColor
            ? layerColor
            : graphic?.StrokeColor ?? layerColor;
        var strokeWidth = ResolveStrokeWidth(item, graphic, targetLayer);

        return new CadTransientStyle(
            strokeColor,
            strokeWidth,
            CadTransientLinePattern.Solid,
            ResolvePreviewFillColor(item.FillStyle),
            HatchFill: ResolvePreviewHatchFill(item.FillStyle));
    }

    private static double ResolveStrokeWidth(
        CadClipboardEntityItem item,
        CadGraphicStyleClipboardSnapshot? graphic,
        CadLayer? targetLayer)
    {
        if (item.Entity.State.UseLayerLineWeight)
            return ResolveLineWeight(targetLayer?.LineWeight ?? item.Layer.LineWeight);

        if (item.Entity.State.LineWeight is { } entityLineWeight)
            return ResolveLineWeight(entityLineWeight);

        if (graphic is not null)
            return ResolveLineWeight(graphic.LineWeight);

        return ResolveLineWeight(item.Layer.LineWeight);
    }

    private static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        return layer.DefaultGraphicStyleId is { } styleId &&
               document.TryGetStyle(styleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : layer.Color;
    }

    private static double ResolveLineWeight(CadLineWeight lineWeight)
    {
        return lineWeight.IsByLayer || lineWeight.Value <= 0
            ? CadLineWeight.Default.Value
            : lineWeight.Value;
    }

    private static CadColor? ResolvePreviewFillColor(CadStyleClipboardSnapshot? fillStyle)
    {
        if (fillStyle is CadGradientFillStyleClipboardSnapshot { Stops.Count: > 0 } gradient &&
            gradient.Stops.All(x => x.Color == gradient.Stops[0].Color))
        {
            var color = gradient.Stops[0].Color;
            return color.IsTransparent ? null : color;
        }

        return null;
    }

    private static CadTransientHatchFill? ResolvePreviewHatchFill(CadStyleClipboardSnapshot? fillStyle)
    {
        return fillStyle is CadHatchFillStyleClipboardSnapshot hatch && !hatch.ForegroundColor.IsTransparent
            ? new CadTransientHatchFill(
                hatch.ForegroundColor,
                hatch.HatchScale,
                hatch.HatchAngle,
                hatch.HatchOrigin,
                hatch.Pattern.Lines)
            : null;
    }
}
