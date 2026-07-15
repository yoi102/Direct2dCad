using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Clipboard;

public sealed record CadClipboardSnapshot(
    IReadOnlyList<CadClipboardEntityItem> Items,
    CadPointD BasePoint,
    CadRectD Bounds)
{
    public bool IsEmpty => Items.Count == 0;

    public IReadOnlyList<CadBlockDefinitionClipboardSnapshot> BlockDefinitions { get; init; } = [];
}

public sealed record CadBlockDefinitionClipboardSnapshot(
    BlockId SourceBlockId,
    string Name,
    CadPointD BasePoint,
    IReadOnlyList<CadClipboardEntityItem> Entities);

public sealed record CadClipboardEntityItem(
    CadEntityClipboardSnapshot Entity,
    CadLayerClipboardSnapshot Layer,
    CadStyleClipboardSnapshot? GraphicStyle,
    CadStyleClipboardSnapshot? FillStyle,
    CadStyleClipboardSnapshot? TextStyle);

public sealed record CadLayerClipboardSnapshot(
    string Name,
    CadColor Color,
    CadLineWeight LineWeight,
    bool IsVisible,
    bool IsLocked,
    bool IsFrozen);

public sealed record CadEntityStateClipboardSnapshot(
    string Name,
    CadLineWeight? LineWeight,
    bool UseLayerColor,
    bool UseLayerLineWeight,
    bool IsVisible,
    bool IsLocked,
    CadStrokeStyle StrokeStyle,
    int ZIndex);

public abstract record CadEntityClipboardSnapshot(CadEntityStateClipboardSnapshot State);

public sealed record CadBlockReferenceClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    BlockId SourceDefinitionBlockId,
    CadPointD Position,
    double RotationRadians,
    double ScaleX,
    double ScaleY) : CadEntityClipboardSnapshot(State);

public sealed record CadLineClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadPointD Start,
    CadPointD End) : CadEntityClipboardSnapshot(State);

public sealed record CadCircleClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadPointD Center,
    double Radius) : CadEntityClipboardSnapshot(State);

public sealed record CadEllipseClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadPointD Center,
    double RadiusX,
    double RadiusY) : CadEntityClipboardSnapshot(State);

public sealed record CadEllipseArcClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadPointD Center,
    double RadiusX,
    double RadiusY,
    double StartAngleRadians,
    double SweepAngleRadians) : CadEntityClipboardSnapshot(State);

public sealed record CadArcClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadPointD Center,
    double Radius,
    double StartAngleRadians,
    double SweepAngleRadians) : CadEntityClipboardSnapshot(State);

public sealed record CadRectangleClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadRectD Bounds,
    double CornerRadiusX,
    double CornerRadiusY) : CadEntityClipboardSnapshot(State);

public sealed record CadPolylineClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    IReadOnlyList<CadPointD> Points,
    bool Closed) : CadEntityClipboardSnapshot(State);

public sealed record CadSplineClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    IReadOnlyList<CadPointD> FitPoints,
    bool Closed) : CadEntityClipboardSnapshot(State);

public sealed record CadTextClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    string Text,
    CadPointD Position,
    double Height,
    double RotationRadians,
    bool IsInverted,
    double InvertedMarginFactor,
    CadRectD LocalBounds,
    bool RequiresBoundsMeasurement) : CadEntityClipboardSnapshot(State);

public sealed record CadShapeTextClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    string Text,
    CadPointD Position,
    double Height,
    double RotationRadians,
    double WidthFactor,
    double CharacterSpacingFactor,
    double ObliqueAngleRadians,
    bool IsInverted,
    double InvertedMarginFactor,
    CadShapeFontId ShapeFontId) : CadEntityClipboardSnapshot(State);

public sealed record CadImageClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadRectD Bounds,
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels,
    string ContentType,
    string SourceName,
    double Opacity = 1.0,
    double RotationRadians = 0.0) : CadEntityClipboardSnapshot(State);

public sealed record CadOleObjectClipboardSnapshot(
    CadEntityStateClipboardSnapshot State,
    CadRectD Bounds,
    byte[] OleBytes,
    string ContentType,
    string SourceName,
    Guid RenderId,
    double Opacity = 1.0) : CadEntityClipboardSnapshot(State);

public abstract record CadStyleClipboardSnapshot(string Name);

public sealed record CadGraphicStyleClipboardSnapshot(
    string Name,
    CadColor StrokeColor,
    CadLineWeight LineWeight,
    LineTypeId LineTypeId) : CadStyleClipboardSnapshot(Name);

public sealed record CadTextStyleClipboardSnapshot(
    string Name,
    string FontFamily,
    double TextHeight,
    double WidthFactor,
    double ObliqueAngle,
    bool IsBold,
    bool IsItalic) : CadStyleClipboardSnapshot(Name);

public sealed record CadGradientFillStyleClipboardSnapshot(
    string Name,
    CadGradientKind GradientKind,
    IReadOnlyList<CadGradientStop> Stops,
    double GradientAngle,
    double GradientScale,
    CadPointD GradientOrigin,
    bool IsCentered) : CadStyleClipboardSnapshot(Name);

public sealed record CadHatchFillStyleClipboardSnapshot(
    string Name,
    CadHatchPatternClipboardSnapshot Pattern,
    CadColor ForegroundColor,
    double HatchScale,
    double HatchAngle,
    CadPointD HatchOrigin,
    bool IsAnnotative) : CadStyleClipboardSnapshot(Name);

public sealed record CadHatchPatternClipboardSnapshot(
    string Name,
    string Description,
    IReadOnlyList<CadHatchLineDefinition> Lines);

public static class CadClipboardSnapshotFactory
{
    public static CadClipboardSnapshot? Create(CadDocument document, IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entityIds);

        var items = new List<CadClipboardEntityItem>();
        var blockDefinitions = new Dictionary<BlockId, CadBlockDefinitionClipboardSnapshot>();
        var blockDefinitionOrder = new List<BlockId>();
        var visitingBlocks = new HashSet<BlockId>();
        var bounds = CadRectD.Empty;

        foreach (var entityId in entityIds.Distinct())
        {
            if (!document.TryGetEntity(entityId, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !TryCreateEntitySnapshot(
                    document,
                    entity,
                    blockDefinitions,
                    blockDefinitionOrder,
                    visitingBlocks,
                    out var item) ||
                item is null)
            {
                continue;
            }

            items.Add(item);
            bounds = bounds.Union(entity.Bounds);
        }

        return items.Count == 0
            ? null
            : new CadClipboardSnapshot(items, bounds.Center, bounds)
            {
                BlockDefinitions = blockDefinitionOrder
                    .Select(blockId => blockDefinitions[blockId])
                    .ToArray()
            };
    }

    private static bool TryCreateEntitySnapshot(
        CadDocument document,
        CadEntity entity,
        Dictionary<BlockId, CadBlockDefinitionClipboardSnapshot> blockDefinitions,
        List<BlockId> blockDefinitionOrder,
        HashSet<BlockId> visitingBlocks,
        out CadClipboardEntityItem? item)
    {
        item = null;

        if (!document.TryGetLayer(entity.LayerId, out var layer) || layer is null)
            return false;

        if (entity is CadBlockReference blockReference &&
            !TryCreateBlockDefinitionSnapshot(
                document,
                blockReference.DefinitionBlockId,
                blockDefinitions,
                blockDefinitionOrder,
                visitingBlocks))
        {
            return false;
        }

        if (!TryCreateEntitySnapshot(entity, out var entitySnapshot) || entitySnapshot is null)
            return false;

        var layerSnapshot = new CadLayerClipboardSnapshot(
            layer.Name,
            layer.Color,
            layer.LineWeight,
            layer.IsVisible,
            layer.IsLocked,
            layer.IsFrozen);

        item = new CadClipboardEntityItem(
            entitySnapshot,
            layerSnapshot,
            CreateStyleSnapshot(document, ResolveGraphicStyleId(entity)),
            CreateStyleSnapshot(document, ResolveFillStyleId(entity)),
            CreateStyleSnapshot(document, ResolveTextStyleId(entity)));
        return true;
    }

    private static bool TryCreateBlockDefinitionSnapshot(
        CadDocument document,
        BlockId blockId,
        Dictionary<BlockId, CadBlockDefinitionClipboardSnapshot> blockDefinitions,
        List<BlockId> blockDefinitionOrder,
        HashSet<BlockId> visitingBlocks)
    {
        if (blockDefinitions.ContainsKey(blockId))
            return true;
        if (!visitingBlocks.Add(blockId) ||
            !document.TryGetBlock(blockId, out var definition) ||
            definition is null)
        {
            return false;
        }

        try
        {
            var entities = new List<CadClipboardEntityItem>();
            foreach (var entity in document.GetEntitiesInBlock(blockId)
                         .Where(entity => !entity.IsErased)
                         .OrderBy(entity => entity.Id.Value))
            {
                if (!TryCreateEntitySnapshot(
                        document,
                        entity,
                        blockDefinitions,
                        blockDefinitionOrder,
                        visitingBlocks,
                        out var item) ||
                    item is null)
                {
                    return false;
                }

                entities.Add(item);
            }

            blockDefinitions[blockId] = new CadBlockDefinitionClipboardSnapshot(
                blockId,
                definition.Name,
                definition.BasePoint,
                entities);
            blockDefinitionOrder.Add(blockId);
            return true;
        }
        finally
        {
            visitingBlocks.Remove(blockId);
        }
    }

    private static bool TryCreateEntitySnapshot(CadEntity entity, out CadEntityClipboardSnapshot? snapshot)
    {
        var state = new CadEntityStateClipboardSnapshot(
            entity.Name,
            entity.LineWeight,
            entity.UseLayerColor,
            entity.UseLayerLineWeight,
            entity.IsVisible,
            entity.IsLocked,
            entity.StrokeStyle,
            entity.ZIndex);

        snapshot = entity switch
        {
            CadBlockReference blockReference => new CadBlockReferenceClipboardSnapshot(
                state,
                blockReference.DefinitionBlockId,
                blockReference.Position,
                blockReference.RotationRadians,
                blockReference.ScaleX,
                blockReference.ScaleY),
            CadLine line => new CadLineClipboardSnapshot(state, line.Start, line.End),
            CadCircle circle => new CadCircleClipboardSnapshot(state, circle.Center, circle.Radius),
            CadEllipse ellipse => new CadEllipseClipboardSnapshot(state, ellipse.Center, ellipse.RadiusX, ellipse.RadiusY),
            CadEllipseArc ellipseArc => new CadEllipseArcClipboardSnapshot(
                state,
                ellipseArc.Center,
                ellipseArc.RadiusX,
                ellipseArc.RadiusY,
                ellipseArc.StartAngleRadians,
                ellipseArc.SweepAngleRadians),
            CadArc arc => new CadArcClipboardSnapshot(
                state,
                arc.Center,
                arc.Radius,
                arc.StartAngleRadians,
                arc.SweepAngleRadians),
            CadRectangle rectangle => new CadRectangleClipboardSnapshot(
                state,
                rectangle.Bounds,
                rectangle.CornerRadiusX,
                rectangle.CornerRadiusY),
            CadPolyline polyline => new CadPolylineClipboardSnapshot(
                state,
                polyline.Points.ToArray(),
                polyline.Closed),
            CadSpline spline => new CadSplineClipboardSnapshot(
                state,
                spline.FitPoints.ToArray(),
                spline.Closed),
            CadText text => new CadTextClipboardSnapshot(
                state,
                text.Text,
                text.Position,
                text.Height,
                text.RotationRadians,
                text.IsInverted,
                text.InvertedMarginFactor,
                text.LocalBounds,
                text.RequiresBoundsMeasurement),
            CadShapeText shapeText => new CadShapeTextClipboardSnapshot(
                state,
                shapeText.Text,
                shapeText.Position,
                shapeText.Height,
                shapeText.RotationRadians,
                shapeText.WidthFactor,
                shapeText.CharacterSpacingFactor,
                shapeText.ObliqueAngleRadians,
                shapeText.IsInverted,
                shapeText.InvertedMarginFactor,
                shapeText.ShapeFontId),
            CadImage image => new CadImageClipboardSnapshot(
                state,
                image.FrameBounds,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride,
                image.CopyPixels(),
                image.ContentType,
                image.SourceName,
                image.Opacity,
                image.RotationRadians),
            CadOleObject oleObject => new CadOleObjectClipboardSnapshot(
                state,
                oleObject.Bounds,
                oleObject.CopyOleBytes(),
                oleObject.ContentType,
                oleObject.SourceName,
                Guid.NewGuid(),
                oleObject.Opacity),
            _ => null
        };

        return snapshot is not null;
    }

    private static CadStyleClipboardSnapshot? CreateStyleSnapshot(CadDocument document, StyleId? styleId)
    {
        if (styleId is null ||
            !document.TryGetStyle(styleId.Value, out var style) ||
            style is null)
        {
            return null;
        }

        return style switch
        {
            CadGraphicStyle graphic => new CadGraphicStyleClipboardSnapshot(
                graphic.Name,
                graphic.StrokeColor,
                graphic.LineWeight,
                graphic.LineTypeId),
            CadTextStyle text => new CadTextStyleClipboardSnapshot(
                text.Name,
                text.FontFamily,
                text.TextHeight,
                text.WidthFactor,
                text.ObliqueAngle,
                text.IsBold,
                text.IsItalic),
            CadGradientFillStyle gradient => new CadGradientFillStyleClipboardSnapshot(
                gradient.Name,
                gradient.GradientKind,
                gradient.Stops.ToArray(),
                gradient.GradientAngle,
                gradient.GradientScale,
                gradient.GradientOrigin,
                gradient.IsCentered),
            CadHatchFillStyle hatch when document.TryGetHatchPattern(hatch.PatternId, out var pattern) && pattern is not null =>
                new CadHatchFillStyleClipboardSnapshot(
                    hatch.Name,
                    new CadHatchPatternClipboardSnapshot(
                        pattern.Name,
                        pattern.Description,
                        pattern.Lines.ToArray()),
                    hatch.ForegroundColor,
                    hatch.HatchScale,
                    hatch.HatchAngle,
                    hatch.HatchOrigin,
                    hatch.IsAnnotative),
            _ => null
        };
    }

    private static StyleId? ResolveGraphicStyleId(CadEntity entity)
        => entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };

    private static StyleId? ResolveFillStyleId(CadEntity entity)
        => entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline polyline => polyline.FillStyleId,
            CadSpline spline => spline.FillStyleId,
            _ => null
        };

    private static StyleId? ResolveTextStyleId(CadEntity entity)
        => entity is CadText text ? text.TextStyleId : null;
}
