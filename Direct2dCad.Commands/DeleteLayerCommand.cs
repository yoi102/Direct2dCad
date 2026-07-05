using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class DeleteLayerCommand : ICadCommand
{
    private readonly LayerId _layerId;
    private readonly Dictionary<EntityId, bool> _previousEntityErasedStates = [];
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

        var entityIdsOnLayer = document.Entities.Values
            .Where(x => x.LayerId.Equals(_layerId))
            .Select(x => x.Id)
            .ToArray();

        _previousEntityErasedStates.Clear();
        foreach (var entityId in entityIdsOnLayer)
            _previousEntityErasedStates[entityId] = document.GetEntity(entityId).IsErased;

        _snapshot ??= LayerSnapshot.From(document, layer);
        document.DocumentSettings.LayerDrawingPriority.RemovePriority(_layerId);
        document.RemoveLayerAndDeleteEntities(_layerId);

        return CreateEntityChangeSet(
            entityIdsOnLayer,
            CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility)
            .WithDocumentStructureChanged();
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

        foreach (var (entityId, wasErased) in _previousEntityErasedStates)
        {
            if (!document.TryGetEntity(entityId, out var entity) || entity is null)
                continue;

            if (wasErased)
                entity.Erase();
            else
                entity.Restore();
        }

        return CreateEntityChangeSet(
            _previousEntityErasedStates.Keys,
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Fill |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.DrawOrder)
            .WithDocumentStructureChanged();
    }

    private static CadDocumentChangeSet CreateEntityChangeSet(
        IEnumerable<EntityId> entityIds,
        CadEntityChangeKind kind)
    {
        var ids = entityIds.ToArray();
        return ids.Length == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(ids, kind);
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
