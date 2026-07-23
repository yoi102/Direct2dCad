using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DOwnerRenderPacket
{
    private readonly Direct2DEntityRenderPacket[] _entries;
    private readonly CadEntity[] _entities;
    private readonly Dictionary<EntityId, int> _indices;

    public Direct2DOwnerRenderPacket(
        CadDocument document,
        BlockId ownerBlockId,
        IReadOnlyList<CadEntity> orderedEntities,
        long version)
    {
        OwnerBlockId = ownerBlockId;
        Version = version;
        _entries = new Direct2DEntityRenderPacket[orderedEntities.Count];
        _entities = new CadEntity[orderedEntities.Count];
        _indices = new Dictionary<EntityId, int>(orderedEntities.Count);

        var bounds = CadRectD.Empty;
        for (var index = 0; index < orderedEntities.Count; index++)
        {
            var entity = orderedEntities[index];
            var entry = CreateEntry(document, entity, index);
            _entries[index] = entry;
            _entities[index] = entity;
            _indices[entity.Id] = index;
            if (entry.IsRenderable)
                bounds = bounds.Union(entry.Bounds);
        }

        Bounds = bounds;
    }

    public BlockId OwnerBlockId { get; }
    public long Version { get; private set; }
    public CadRectD Bounds { get; private set; }
    public IReadOnlyList<Direct2DEntityRenderPacket> Entries => _entries;
    public IReadOnlyList<CadEntity> Entities => _entities;

    public bool TryGetIndex(EntityId entityId, out int index) =>
        _indices.TryGetValue(entityId, out index);

    public int GetRank(EntityId entityId) =>
        _indices.GetValueOrDefault(entityId, int.MaxValue);

    public bool TryUpdate(
        CadDocument document,
        EntityId entityId,
        long version)
    {
        if (!_indices.TryGetValue(entityId, out var index) ||
            !document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.OwnerBlockId != OwnerBlockId)
        {
            return false;
        }

        _entries[index] = CreateEntry(document, entity, index);
        _entities[index] = entity;
        Version = version;
        return true;
    }

    public void RecalculateBounds()
    {
        var bounds = CadRectD.Empty;
        foreach (var entry in _entries)
        {
            if (entry.IsRenderable)
                bounds = bounds.Union(entry.Bounds);
        }

        Bounds = bounds;
    }

    private static Direct2DEntityRenderPacket CreateEntry(
        CadDocument document,
        CadEntity entity,
        int rank)
    {
        var layerPriority =
            document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId);
        var layerRenderable =
            document.TryGetLayer(entity.LayerId, out var layer) &&
            layer is { IsVisible: true, IsFrozen: false };
        return new Direct2DEntityRenderPacket(
            entity,
            entity.Bounds,
            rank,
            entity.LayerId,
            layerPriority,
            !entity.IsErased && entity.IsVisible && layerRenderable);
    }
}

internal readonly record struct Direct2DEntityRenderPacket(
    CadEntity Entity,
    CadRectD Bounds,
    int Rank,
    LayerId LayerId,
    int LayerPriority,
    bool IsRenderable);
