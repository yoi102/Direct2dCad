using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal static class Direct2DBlockReferenceStyleResolver
{
    public static bool TryResolve(
        CadDocument document,
        Direct2DBlockReferenceRenderState reference,
        Direct2DBlockRenderStyle? parentStyle,
        out Direct2DBlockRenderStyle style)
    {
        if (!document.TryGetLayer(reference.LayerId, out var ownLayer) || ownLayer is null)
        {
            style = default;
            return false;
        }

        var effectiveLayer =
            reference.LayerId.Equals(LayerId.Default) && parentStyle is { } containingStyle
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

        style = new Direct2DBlockRenderStyle(effectiveLayer, referenceColor);
        return true;
    }

    public static CadColor? ResolveChildStrokeColor(
        CadDocument document,
        CadEntity child,
        Direct2DBlockRenderStyle referenceStyle)
    {
        return child.ColorSource switch
        {
            CadColorSource.ByBlock => referenceStyle.ReferenceColor,
            CadColorSource.ByLayer when child.LayerId.Equals(LayerId.Default) =>
                ResolveLayerStrokeColor(document, referenceStyle.EffectiveLayer),
            _ => null
        };
    }

    public static bool IsVisible(
        CadDocument document,
        CadEntity entity,
        Direct2DBlockRenderStyle referenceStyle,
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

    public static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        return ResolveGraphicStrokeColor(document, layer.DefaultGraphicStyleId) ?? layer.Color;
    }

    public static float ResolveLayerStrokeWidth(CadLayer layer)
    {
        var weight = layer.LineWeight;
        if (weight.IsByLayer || weight.Value <= 0)
            weight = CadLineWeight.Default;

        return (float)Math.Max(weight.Value, 0.01);
    }

    private static CadColor? ResolveGraphicStrokeColor(
        CadDocument document,
        StyleId? styleId)
    {
        return styleId is { } id &&
               document.TryGetStyle(id, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : null;
    }
}

internal readonly record struct Direct2DBlockReferenceRenderState(
    BlockId DefinitionBlockId,
    CadPointD Position,
    double RotationRadians,
    double ScaleX,
    double ScaleY,
    LayerId LayerId,
    CadColorSource ColorSource,
    StyleId? GraphicStyleId)
{
    public static Direct2DBlockReferenceRenderState From(CadBlockReference reference) => new(
        reference.DefinitionBlockId,
        reference.Position,
        reference.RotationRadians,
        reference.ScaleX,
        reference.ScaleY,
        reference.LayerId,
        reference.ColorSource,
        reference.GraphicStyleId);
}
