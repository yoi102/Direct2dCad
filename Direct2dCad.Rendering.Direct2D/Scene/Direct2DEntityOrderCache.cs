using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DEntityOrderCache
{
    private readonly Dictionary<BlockId, IReadOnlyList<CadEntity>> _entitiesByOwner = [];
    private readonly Dictionary<BlockId, IReadOnlyList<CadEntity>> _oleEntitiesByOwner = [];
    private CadDocument? _document;

    public IReadOnlyList<CadEntity> GetOrderedEntities(
        CadDocument document,
        BlockId ownerBlockId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!ReferenceEquals(_document, document))
        {
            _document = document;
            _entitiesByOwner.Clear();
            _oleEntitiesByOwner.Clear();
        }

        if (_entitiesByOwner.TryGetValue(ownerBlockId, out var entities))
            return entities;

        entities = document.Entities.Values
            .Where(entity => entity.OwnerBlockId.Equals(ownerBlockId))
            .OrderBy(entity =>
                document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenBy(entity => entity.ZIndex)
            .ThenBy(entity => entity.Id.Value)
            .ToArray();
        _entitiesByOwner[ownerBlockId] = entities;
        return entities;
    }

    public IReadOnlyList<CadEntity> GetOrderedOleEntities(
        CadDocument document,
        BlockId ownerBlockId)
    {
        if (_oleEntitiesByOwner.TryGetValue(ownerBlockId, out var oleEntities) &&
            ReferenceEquals(_document, document))
        {
            return oleEntities;
        }

        oleEntities = GetOrderedEntities(document, ownerBlockId)
            .Where(static entity => entity is CadOleObject)
            .ToArray();
        _oleEntitiesByOwner[ownerBlockId] = oleEntities;
        return oleEntities;
    }

    public void Invalidate()
    {
        _entitiesByOwner.Clear();
        _oleEntitiesByOwner.Clear();
    }
}
