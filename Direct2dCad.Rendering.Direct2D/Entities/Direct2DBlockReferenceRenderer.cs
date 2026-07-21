using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal sealed class Direct2DBlockReferenceRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DEntityRenderer entityRenderer,
    Direct2DOleRenderer oleRenderer,
    Direct2DStyleResourceCache styleResources,
    Direct2DEntityOrderCache entityOrderCache,
    Direct2DRenderStatisticsCollector statistics)
{
    private readonly HashSet<BlockId> _visitedBlocks = [];

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadBlockReference reference,
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        if (TryDrawProxy(context, document, reference, options, parentStyle: null))
            return;

        Draw(
            context,
            document,
            viewport,
            ReferenceRenderState.From(reference),
            options,
            parentStyle: null,
            _visitedBlocks);
    }

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        BlockId definitionBlockId,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY,
        LayerId layerId,
        CadColorSource colorSource,
        StyleId? graphicStyleId,
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        Draw(
            context,
            document,
            viewport,
            new ReferenceRenderState(
                definitionBlockId,
                position,
                rotationRadians,
                scaleX,
                scaleY,
                layerId,
                colorSource,
                graphicStyleId),
            options,
            parentStyle: null,
            _visitedBlocks);
    }

    private void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        ReferenceRenderState reference,
        CadRenderOptions options,
        BlockRenderStyleContext? parentStyle,
        HashSet<BlockId> visited)
    {
        if (!visited.Add(reference.DefinitionBlockId) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null ||
            !TryResolveReferenceStyle(document, reference, parentStyle, out var referenceStyle))
        {
            return;
        }

        var previousTransform = context.Transform;
        context.Transform = CreateTransform(
            definition.BasePoint,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY) * previousTransform;
        try
        {
            foreach (var child in Direct2DBlockEntityVisibility.Resolve(
                         document,
                         reference.DefinitionBlockId,
                         context.Transform,
                         viewport,
                         options,
                         entityOrderCache))
            {
                if (!IsVisible(document, child, referenceStyle, options))
                    continue;

                if (child is CadBlockReference nested)
                {
                    var detail = Direct2DEntityLevelOfDetail.Resolve(
                        nested,
                        resources: null,
                        context.Transform,
                        options);
                    if (detail == Direct2DEntityRenderDetail.Skip)
                        continue;

                    statistics.RecordExpandedBlockEntity();
                    statistics.RecordEntitySubmission();
                    statistics.RecordBlockReference();
                    if (TryDrawProxy(context, document, nested, options, referenceStyle))
                        continue;

                    Draw(
                        context,
                        document,
                        viewport,
                        ReferenceRenderState.From(nested),
                        options,
                        referenceStyle,
                        visited);
                    continue;
                }

                if (child is CadOleObject oleObject)
                {
                    if (Direct2DEntityLevelOfDetail.ResolveOle(
                            oleObject.Bounds,
                            context.Transform,
                            options) == Direct2DEntityRenderDetail.Skip)
                    {
                        continue;
                    }

                    statistics.RecordExpandedBlockEntity();
                    statistics.RecordEntitySubmission();
                    CadColor? proxyColor = child.LayerId.Equals(LayerId.Default)
                        ? referenceStyle.ReferenceColor
                        : null;
                    oleRenderer.DrawEntity(
                        context,
                        document,
                        oleObject,
                        viewport,
                        options,
                        proxyColor);
                    continue;
                }

                if (!resourceCache.TryGetEntityResources(child.Id, out var resources) || resources is null)
                    continue;
                float? strokeWidthOverride = child.UseLayerLineWeight && child.LayerId.Equals(LayerId.Default)
                    ? ResolveLayerStrokeWidth(referenceStyle.EffectiveLayer)
                    : null;
                if (Direct2DEntityLevelOfDetail.Resolve(
                        child,
                        resources,
                        context.Transform,
                        options,
                        strokeWidthOverride) == Direct2DEntityRenderDetail.Skip)
                {
                    continue;
                }

                statistics.RecordExpandedBlockEntity();
                statistics.RecordEntitySubmission();

                var colorOverride = ResolveChildStrokeColor(document, child, referenceStyle);
                var brushOverride = colorOverride is { } color
                    ? styleResources.GetBrush(context, color)
                    : null;

                entityRenderer.Draw(
                    context,
                    document,
                    child,
                    resources,
                    viewport,
                    options,
                    brushOverride,
                    strokeWidthOverride);
            }
        }
        finally
        {
            context.Transform = previousTransform;
            visited.Remove(reference.DefinitionBlockId);
        }
    }

    private bool TryDrawProxy(
        ID2D1DeviceContext context,
        CadDocument document,
        CadBlockReference reference,
        CadRenderOptions options,
        BlockRenderStyleContext? parentStyle)
    {
        if (Direct2DEntityLevelOfDetail.Resolve(
                reference,
                resources: null,
                context.Transform,
                options) != Direct2DEntityRenderDetail.Simplified ||
            !TryResolveReferenceStyle(
                document,
                ReferenceRenderState.From(reference),
                parentStyle,
                out var referenceStyle))
        {
            return false;
        }

        var brush = styleResources.GetBrush(context, referenceStyle.ReferenceColor);
        Direct2DEntityRenderer.DrawRectangularProxy(context, reference.Bounds, brush);
        return true;
    }

    private static bool TryResolveReferenceStyle(
        CadDocument document,
        ReferenceRenderState reference,
        BlockRenderStyleContext? parentStyle,
        out BlockRenderStyleContext style)
    {
        if (!document.TryGetLayer(reference.LayerId, out var ownLayer) || ownLayer is null)
        {
            style = default;
            return false;
        }

        var effectiveLayer = reference.LayerId.Equals(LayerId.Default) && parentStyle is { } containingStyle
            ? containingStyle.EffectiveLayer
            : ownLayer;
        var layerColor = ResolveLayerStrokeColor(document, effectiveLayer);
        var referenceColor = reference.ColorSource switch
        {
            CadColorSource.Explicit =>
                ResolveGraphicStrokeColor(document, reference.GraphicStyleId) ?? layerColor,
            CadColorSource.ByBlock when parentStyle is { } containingReferenceStyle =>
                containingReferenceStyle.ReferenceColor,
            _ => layerColor
        };

        style = new BlockRenderStyleContext(effectiveLayer, referenceColor);
        return true;
    }

    private static CadColor? ResolveChildStrokeColor(
        CadDocument document,
        CadEntity child,
        BlockRenderStyleContext referenceStyle)
    {
        return child.ColorSource switch
        {
            CadColorSource.ByBlock => referenceStyle.ReferenceColor,
            CadColorSource.ByLayer when child.LayerId.Equals(LayerId.Default) =>
                ResolveLayerStrokeColor(document, referenceStyle.EffectiveLayer),
            _ => null
        };
    }

    private static bool IsVisible(
        CadDocument document,
        CadEntity entity,
        BlockRenderStyleContext referenceStyle,
        CadRenderOptions options)
    {
        var layer = entity.LayerId.Equals(LayerId.Default)
            ? referenceStyle.EffectiveLayer
            : document.TryGetLayer(entity.LayerId, out var childLayer)
                ? childLayer
                : null;

        return !entity.IsErased &&
               entity.IsVisible &&
               !options.HiddenEntityIds.Contains(entity.Id) &&
               layer is { IsVisible: true, IsFrozen: false };
    }

    private static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        return ResolveGraphicStrokeColor(document, layer.DefaultGraphicStyleId) ?? layer.Color;
    }

    private static CadColor? ResolveGraphicStrokeColor(CadDocument document, StyleId? styleId)
    {
        return styleId is { } id &&
               document.TryGetStyle(id, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : null;
    }

    private static float ResolveLayerStrokeWidth(CadLayer layer)
    {
        var weight = layer.LineWeight;
        if (weight.IsByLayer || weight.Value <= 0)
            weight = CadLineWeight.Default;

        return (float)Math.Max(weight.Value, 0.01);
    }

    private static Matrix3x2 CreateTransform(
        CadPointD basePoint,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY)
    {
        return Matrix3x2.CreateTranslation((float)-basePoint.X, (float)-basePoint.Y) *
               Matrix3x2.CreateScale((float)scaleX, (float)scaleY) *
               Matrix3x2.CreateRotation((float)rotationRadians) *
               Matrix3x2.CreateTranslation((float)position.X, (float)position.Y);
    }

    private readonly record struct BlockRenderStyleContext(
        CadLayer EffectiveLayer,
        CadColor ReferenceColor);

    private readonly record struct ReferenceRenderState(
        BlockId DefinitionBlockId,
        CadPointD Position,
        double RotationRadians,
        double ScaleX,
        double ScaleY,
        LayerId LayerId,
        CadColorSource ColorSource,
        StyleId? GraphicStyleId)
    {
        public static ReferenceRenderState From(CadBlockReference reference) => new(
            reference.DefinitionBlockId,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY,
            reference.LayerId,
            reference.ColorSource,
            reference.GraphicStyleId);
    }
}
