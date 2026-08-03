using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal readonly struct CadClipboardInteractionService(
    CadEditor editor)
{
    public CadDocument Document => editor.Document;

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
        var blockDefinitions = clipboard.BlockDefinitions.ToDictionary(
            definition => definition.SourceBlockId);
        foreach (var item in clipboard.Items)
        {
            AddPreviewItem(
                items,
                item,
                delta,
                targetLayer,
                editor.Document,
                blockDefinitions,
                [],
                parentStyle: null);
        }
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
        CadDocument document,
        IReadOnlyDictionary<BlockId, CadBlockDefinitionClipboardSnapshot> blockDefinitions,
        HashSet<BlockId> visitingBlocks,
        PastePreviewBlockStyleContext? parentStyle)
    {
        var effectiveLayer = ResolvePreviewLayer(item.Layer, targetLayer, document, parentStyle);
        if (!item.Entity.State.IsVisible ||
            !effectiveLayer.IsVisible ||
            effectiveLayer.IsFrozen)
        {
            return;
        }

        var style = CreatePreviewStyle(item, effectiveLayer, document, parentStyle);
        switch (item.Entity)
        {
            case CadBlockReferenceClipboardSnapshot blockReference:
                AddBlockPreview(
                    items,
                    blockReference,
                    delta,
                    style,
                    document,
                    blockDefinitions,
                    visitingBlocks,
                    new PastePreviewBlockStyleContext(effectiveLayer, style.StrokeColor));
                break;

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
                    null,
                    text.RotationRadians,
                    CreateTransientTextFormat(item.TextStyle)));
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
                    style,
                    Opacity: image.Opacity,
                    RotationRadians: image.RotationRadians));
                break;

            case CadOleObjectClipboardSnapshot oleObject:
                items.Add(new CadTransientOleObject(
                    oleObject.Bounds.Translate(delta),
                    oleObject.OleBytes,
                    style,
                    SourceEntityId: null,
                    oleObject.RenderId,
                    oleObject.Opacity));
                break;
        }
    }

    private static void AddBlockPreview(
        List<CadTransientItem> items,
        CadBlockReferenceClipboardSnapshot reference,
        CadVectorD delta,
        CadTransientStyle style,
        CadDocument document,
        IReadOnlyDictionary<BlockId, CadBlockDefinitionClipboardSnapshot> blockDefinitions,
        HashSet<BlockId> visitingBlocks,
        PastePreviewBlockStyleContext blockStyle)
    {
        if (!blockDefinitions.TryGetValue(reference.SourceDefinitionBlockId, out var definition) ||
            !visitingBlocks.Add(reference.SourceDefinitionBlockId))
        {
            return;
        }

        try
        {
            var children = new List<CadTransientItem>();
            foreach (var child in definition.Entities)
            {
                AddPreviewItem(
                    children,
                    child,
                    CadVectorD.Zero,
                    null,
                    document,
                    blockDefinitions,
                    visitingBlocks,
                    blockStyle);
            }

            var transform = CadMatrixD.CreateTranslation(-definition.BasePoint.X, -definition.BasePoint.Y) *
                            CadMatrixD.CreateScale(reference.ScaleX, reference.ScaleY) *
                            CadMatrixD.CreateRotation(reference.RotationRadians) *
                            CadMatrixD.CreateTranslation(
                                reference.Position.X + delta.X,
                                reference.Position.Y + delta.Y);
            items.Add(new CadTransientGroup(children, transform, style));
        }
        finally
        {
            visitingBlocks.Remove(reference.SourceDefinitionBlockId);
        }
    }

    private static CadTransientStyle CreatePreviewStyle(
        CadClipboardEntityItem item,
        PastePreviewLayerContext effectiveLayer,
        CadDocument document,
        PastePreviewBlockStyleContext? parentStyle)
    {
        var graphic = item.GraphicStyle as CadGraphicStyleClipboardSnapshot;
        var layerColor = effectiveLayer.Layer is not null
            ? ResolveLayerStrokeColor(document, effectiveLayer.Layer)
            : effectiveLayer.Snapshot.DefaultGraphicStyle?.StrokeColor ?? effectiveLayer.Snapshot.Color;
        var strokeColor = item.Entity.State.ColorSource switch
        {
            CadColorSource.Explicit => graphic?.StrokeColor ?? layerColor,
            CadColorSource.ByBlock when parentStyle is { } containingBlock => containingBlock.ReferenceColor,
            _ => layerColor
        };
        var strokeWidth = ResolveStrokeWidth(item, graphic, effectiveLayer);
        CadStrokeStyle? strokeStyle = item.Entity.State.StrokeStyle == CadStrokeStyle.Default
            ? null
            : item.Entity.State.StrokeStyle;
        CadLineTypeDefinition? lineType = null;
        if (strokeStyle is null &&
            graphic is not null &&
            graphic.LineTypeId != LineTypeId.Continuous &&
            document.LineTypes.TryGetValue(graphic.LineTypeId, out var resolvedLineType))
        {
            lineType = resolvedLineType;
        }

        return new CadTransientStyle(
            strokeColor,
            strokeWidth,
            CadTransientLinePattern.Solid,
            ResolvePreviewFillColor(item.FillStyle),
            HatchFill: ResolvePreviewHatchFill(item.FillStyle),
            StrokeStyle: strokeStyle,
            LineType: lineType);
    }

    private static double ResolveStrokeWidth(
        CadClipboardEntityItem item,
        CadGraphicStyleClipboardSnapshot? graphic,
        PastePreviewLayerContext effectiveLayer)
    {
        if (item.Entity.State.UseLayerLineWeight)
            return ResolveLineWeight(effectiveLayer.Layer?.LineWeight ?? effectiveLayer.Snapshot.LineWeight);

        if (item.Entity.State.LineWeight is { } entityLineWeight)
            return ResolveLineWeight(entityLineWeight);

        if (graphic is not null)
            return ResolveLineWeight(graphic.LineWeight);

        return ResolveLineWeight(effectiveLayer.Layer?.LineWeight ?? effectiveLayer.Snapshot.LineWeight);
    }

    private static PastePreviewLayerContext ResolvePreviewLayer(
        CadLayerClipboardSnapshot snapshot,
        CadLayer? targetLayer,
        CadDocument document,
        PastePreviewBlockStyleContext? parentStyle)
    {
        if (targetLayer is not null)
            return PastePreviewLayerContext.FromLayer(targetLayer, snapshot);

        if (snapshot.IsDefault && parentStyle is { } containingBlock)
            return containingBlock.EffectiveLayer;

        CadLayer? resolvedLayer = null;
        if (snapshot.IsDefault)
            document.TryGetLayer(LayerId.Default, out resolvedLayer);
        else
            resolvedLayer = document.Layers.Values.FirstOrDefault(layer =>
                string.Equals(layer.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));

        return resolvedLayer is not null
            ? PastePreviewLayerContext.FromLayer(resolvedLayer, snapshot)
            : new PastePreviewLayerContext(null, snapshot);
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

    private static CadTransientTextFormat? CreateTransientTextFormat(CadStyleClipboardSnapshot? textStyle)
    {
        return textStyle is CadTextStyleClipboardSnapshot text
            ? new CadTransientTextFormat(text.FontFamily, text.IsBold, text.IsItalic)
            : null;
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

    private readonly record struct PastePreviewLayerContext(
        CadLayer? Layer,
        CadLayerClipboardSnapshot Snapshot)
    {
        public bool IsVisible => Layer?.IsVisible ?? Snapshot.IsVisible;
        public bool IsFrozen => Layer?.IsFrozen ?? Snapshot.IsFrozen;

        public static PastePreviewLayerContext FromLayer(
            CadLayer layer,
            CadLayerClipboardSnapshot fallbackSnapshot) =>
            new(layer, fallbackSnapshot);
    }

    private readonly record struct PastePreviewBlockStyleContext(
        PastePreviewLayerContext EffectiveLayer,
        CadColor ReferenceColor);
}
