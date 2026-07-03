using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class DeleteLayerCommand : ICadCommand
{
    private readonly LayerId _layerId;
    private LayerSnapshot? _snapshot;

    public string Name => "Delete Layer";

    public DeleteLayerCommand(LayerId layerId)
    {
        _layerId = layerId;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.TryGetLayer(_layerId, out var layer) || layer is null)
            return CadDocumentChangeSet.Empty;

        if (document.HasEntitiesOnLayer(_layerId))
            throw new InvalidOperationException("Layer cannot be removed while it contains entities.");

        _snapshot ??= LayerSnapshot.From(document, layer);
        document.DocumentSettings.LayerDrawingPriority.RemovePriority(_layerId);
        document.RemoveLayer(_layerId);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_snapshot is not { } snapshot || document.TryGetLayer(_layerId, out _))
            return CadDocumentChangeSet.Empty;

        document.RestoreLayer(
            snapshot.Id,
            snapshot.Name,
            snapshot.Color,
            snapshot.LineWeight,
            snapshot.IsVisible,
            snapshot.IsLocked,
            snapshot.IsFrozen,
            snapshot.DefaultGraphicStyleId);

        if (snapshot.HasExplicitPriority)
            document.DocumentSettings.LayerDrawingPriority.SetPriority(snapshot.Id, snapshot.Priority);

        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    private sealed record LayerSnapshot(
        LayerId Id,
        string Name,
        bool IsVisible,
        bool IsLocked,
        bool IsFrozen,
        CadColor Color,
        CadLineWeight LineWeight,
        StyleId? DefaultGraphicStyleId,
        bool HasExplicitPriority,
        int Priority)
    {
        public static LayerSnapshot From(CadDocument document, CadLayer layer)
        {
            var priorities = document.DocumentSettings.LayerDrawingPriority.Priorities;
            var hasExplicitPriority = priorities.TryGetValue(layer.Id, out var priority);
            return new LayerSnapshot(
                layer.Id,
                layer.Name,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen,
                layer.Color,
                layer.LineWeight,
                layer.DefaultGraphicStyleId,
                hasExplicitPriority,
                hasExplicitPriority
                    ? priority
                    : document.DocumentSettings.LayerDrawingPriority.DefaultPriority);
        }
    }
}
