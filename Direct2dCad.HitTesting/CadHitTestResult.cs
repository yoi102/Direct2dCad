using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.HitTesting;

public enum CadHitTestKind
{
    Edge,
    Fill
}

/// <summary>
/// HitTest 结果。
/// EntityPath 用于 BlockReference 嵌套场景。
/// 普通实体：EntityPath = [entityId]
/// 块引用内部实体：EntityPath = [blockReferenceId, childEntityId]
/// 多层块引用：EntityPath = [blockReferenceId, nestedBlockReferenceId, childEntityId]
/// </summary>
public readonly struct CadHitTestResult
{
    private readonly EntityId[]? _entityPath;

    public CadHitTestKind Kind { get; }
    public CadPointD HitPoint { get; }
    public double Distance { get; }

    public IReadOnlyList<EntityId> EntityPath => _entityPath ?? Array.Empty<EntityId>();

    public EntityId TopEntityId => EntityPath.Count == 0
        ? default
        : EntityPath[0];

    public EntityId LeafEntityId => EntityPath.Count == 0
        ? default
        : EntityPath[^1];

    public CadHitTestResult(
        CadHitTestKind kind,
        IEnumerable<EntityId> entityPath,
        CadPointD hitPoint,
        double distance = 0)
    {
        Kind = kind;
        _entityPath = entityPath?.ToArray() ?? throw new ArgumentNullException(nameof(entityPath));
        if (_entityPath.Length == 0)
            throw new ArgumentException("Entity path cannot be empty.", nameof(entityPath));

        HitPoint = hitPoint;
        Distance = distance;
    }

    internal CadHitTestResult Prepend(EntityId entityId)
    {
        var oldPath = EntityPath;
        var newPath = new EntityId[oldPath.Count + 1];

        newPath[0] = entityId;
        for (var i = 0; i < oldPath.Count; i++)
            newPath[i + 1] = oldPath[i];

        return new CadHitTestResult(Kind, newPath, HitPoint, Distance);
    }
}
