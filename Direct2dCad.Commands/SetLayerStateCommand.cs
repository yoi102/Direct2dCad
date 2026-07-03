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
        _previousState ??= LayerState.From(layer);
        Apply(layer, new LayerState(_isVisible, _isLocked, _isFrozen));
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousState is not { } previousState)
            return CadDocumentChangeSet.Empty;

        Apply(document.GetLayer(_layerId), previousState);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
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
