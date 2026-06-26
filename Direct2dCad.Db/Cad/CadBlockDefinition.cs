using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Cad;

public sealed class CadBlockDefinition : IEquatable<CadBlockDefinition>
{
    private readonly List<EntityId> _entityIds = new();

    public BlockId Id { get; } 
    public string Name { get; private set; }
    public CadPointD BasePoint { get; private set; }
    public IReadOnlyList<EntityId> EntityIds => _entityIds;

    internal CadBlockDefinition(BlockId id, string name, CadPointD basePoint)
    {
        Id = id;
        Name = GuardName(name);
        BasePoint = basePoint;
    }

    public void Rename(string name) => Name = GuardName(name);
    public void SetBasePoint(CadPointD basePoint) => BasePoint = basePoint;

    internal void AddEntity(EntityId entityId)
    {
        if (!_entityIds.Contains(entityId))
            _entityIds.Add(entityId);
    }

    internal bool RemoveEntity(EntityId entityId) => _entityIds.Remove(entityId);

    public bool Equals(CadBlockDefinition? other) => other is not null && Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is CadBlockDefinition other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    private static string GuardName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Name cannot be empty.", nameof(name))
            : name.Trim();
    }
}
