using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public abstract class EntityPropertyViewModel : ObservableObject
{
    protected static CadColor ResolveStrokeColor(
        CadDocument document,
        CadEntity entity,
        StyleId? graphicStyleId)
    {
        var layer = document.GetLayer(entity.LayerId);
        return entity.UseLayerColor
            ? ResolveLayerStrokeColor(document, layer)
            : ResolveGraphicStrokeColor(document, graphicStyleId ?? layer.DefaultGraphicStyleId) ??
              ResolveLayerStrokeColor(document, layer);
    }

    protected static CadLineWeight ResolveEntityLineWeight(
        CadDocument document,
        CadEntity entity,
        StyleId? graphicStyleId)
    {
        if (entity.LineWeight is { IsByLayer: false } explicitWeight)
            return NormalizeLineWeight(explicitWeight);

        var layer = document.GetLayer(entity.LayerId);
        var styleWeight = ResolveGraphicLineWeight(document, graphicStyleId ?? layer.DefaultGraphicStyleId);
        return styleWeight is { IsByLayer: false }
            ? NormalizeLineWeight(styleWeight.Value)
            : CadLineWeight.Default;
    }

    protected static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        return ResolveGraphicStrokeColor(document, layer.DefaultGraphicStyleId) ?? layer.Color;
    }

    private static CadColor? ResolveGraphicStrokeColor(CadDocument document, StyleId? styleId)
    {
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : null;
    }

    private static CadLineWeight? ResolveGraphicLineWeight(CadDocument document, StyleId? styleId)
    {
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.LineWeight
            : null;
    }

    private static CadLineWeight NormalizeLineWeight(CadLineWeight lineWeight)
    {
        return lineWeight.IsByLayer || lineWeight.Value <= 0
            ? CadLineWeight.Default
            : lineWeight;
    }
}
