using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class SetLayerStateCommand : ICadCommand
{
    private readonly LayerId _layerId;
    private readonly bool _isVisible;
    private readonly bool _isLocked;
    private readonly bool _isFrozen;
    private LayerState? _previousState;

    public string Name => "Set Layer State";

    public SetLayerStateCommand(
        LayerId layerId,
        bool isVisible,
        bool isLocked,
        bool isFrozen)
    {
        _layerId = layerId;
        _isVisible = isVisible;
        _isLocked = isLocked;
        _isFrozen = isFrozen;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layer = document.GetLayer(_layerId);
        var current = LayerState.From(layer);
        _previousState ??= current;
        var target = new LayerState(_isVisible, _isLocked, _isFrozen);
        Apply(layer, target);
        return CreateChangeSet(document, current, target);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousState is not { } previousState)
            return CadDocumentChangeSet.Empty;

        var layer = document.GetLayer(_layerId);
        var current = LayerState.From(layer);
        Apply(layer, previousState);
        return CreateChangeSet(document, current, previousState);
    }

    private CadDocumentChangeSet CreateChangeSet(
        CadDocument document,
        LayerState previous,
        LayerState current)
    {
        if (previous.IsVisible == current.IsVisible &&
            previous.IsFrozen == current.IsFrozen)
        {
            return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
        }

        return CadDocumentChangeSet
            .ForEntities(
                document.GetEntityIdsOnLayer(_layerId),
                CadEntityChangeKind.Appearance)
            .WithDocumentStructureChanged();
    }

    private static void Apply(CadLayer layer, LayerState state)
    {
        layer.SetVisible(state.IsVisible);
        layer.SetLocked(state.IsLocked);
        layer.SetFrozen(state.IsFrozen);
    }

    private readonly record struct LayerState(bool IsVisible, bool IsLocked, bool IsFrozen)
    {
        public static LayerState From(CadLayer layer)
        {
            return new LayerState(layer.IsVisible, layer.IsLocked, layer.IsFrozen);
        }
    }
}
