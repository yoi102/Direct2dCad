using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class SetLayerAppearanceCommand : ICadCommand
{
    private readonly LayerId _layerId;
    private readonly CadColor _color;
    private readonly CadLineWeight _lineWeight;
    private LayerAppearance? _previousAppearance;

    public string Name => "Set Layer Appearance";

    public SetLayerAppearanceCommand(
        LayerId layerId,
        CadColor color,
        CadLineWeight lineWeight)
    {
        _layerId = layerId;
        _color = color;
        _lineWeight = lineWeight;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layer = document.GetLayer(_layerId);
        _previousAppearance ??= LayerAppearance.From(layer);
        Apply(
            document,
            new LayerAppearance(
                _layerId,
                _color,
                _lineWeight,
                DefaultGraphicStyleId: null));
        return CreateChangeSet(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousAppearance is not { } previousAppearance)
            return CadDocumentChangeSet.Empty;

        Apply(document, previousAppearance);
        return CreateChangeSet(document);
    }

    private CadDocumentChangeSet CreateChangeSet(CadDocument document)
    {
        return CadDocumentChangeSet
            .ForEntities(document.GetEntityIdsOnLayer(_layerId), CadEntityChangeKind.Appearance)
            .WithDocumentStructureChanged();
    }

    private static void Apply(CadLayer layer, LayerAppearance appearance)
    {
        layer.SetColor(appearance.Color);
        layer.SetLineWeight(appearance.LineWeight);
    }

    private static void Apply(CadDocument document, LayerAppearance appearance)
    {
        var layer = document.GetLayer(appearance.LayerId);
        Apply(layer, appearance);
        document.SetLayerDefaultGraphicStyle(appearance.LayerId, appearance.DefaultGraphicStyleId);
    }

    private readonly record struct LayerAppearance(
        LayerId LayerId,
        CadColor Color,
        CadLineWeight LineWeight,
        StyleId? DefaultGraphicStyleId)
    {
        public static LayerAppearance From(CadLayer layer)
        {
            return new LayerAppearance(
                layer.Id,
                layer.Color,
                layer.LineWeight,
                layer.DefaultGraphicStyleId);
        }
    }
}
