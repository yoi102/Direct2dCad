using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class CreateBlockCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly string _blockName;
    private readonly CadPointD _basePoint;
    private readonly BlockId _ownerBlockId;
    private readonly LayerId _referenceLayerId;
    private readonly string _referenceName;
    private readonly Dictionary<EntityId, BlockId> _originalOwners = [];
    private BlockId? _createdBlockId;
    private EntityId? _createdReferenceId;

    public CreateBlockCommand(
        IEnumerable<EntityId> entityIds,
        string blockName,
        CadPointD basePoint,
        BlockId ownerBlockId,
        LayerId referenceLayerId,
        string referenceName = "")
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
        _blockName = string.IsNullOrWhiteSpace(blockName)
            ? throw new ArgumentException("Block name cannot be empty.", nameof(blockName))
            : blockName.Trim();
        _basePoint = basePoint;
        _ownerBlockId = ownerBlockId;
        _referenceLayerId = referenceLayerId;
        _referenceName = referenceName;
    }

    public string Name => "Create Block";
    public BlockId? CreatedBlockId => _createdBlockId;
    public EntityId? CreatedReferenceId => _createdReferenceId;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        CadEntityAccessPolicy.EnsureCanAddToLayer(document, _referenceLayerId);

        if (_originalOwners.Count == 0)
        {
            foreach (var entityId in _entityIds)
            {
                var entity = document.GetEntity(entityId);
                if (!entity.OwnerBlockId.Equals(_ownerBlockId))
                    throw new InvalidOperationException("All entities must belong to the active drawing space.");
                _originalOwners.Add(entityId, entity.OwnerBlockId);
            }
        }

        BlockId blockId;
        if (_createdBlockId is { } existingBlockId)
        {
            blockId = existingBlockId;
            if (!document.Blocks.ContainsKey(blockId))
                document.RestoreBlockDefinition(blockId, _blockName, _basePoint);
        }
        else
        {
            blockId = document.CreateBlockDefinition(_blockName, _basePoint);
            _createdBlockId = blockId;
        }

        foreach (var entityId in _entityIds)
            document.MoveEntityToBlock(entityId, blockId);

        CadBlockReference reference;
        if (_createdReferenceId is { } referenceId &&
            document.TryGetEntity(referenceId, out var existingEntity) &&
            existingEntity is CadBlockReference existingReference)
        {
            existingReference.Restore();
            reference = existingReference;
        }
        else
        {
            reference = document.AddBlockReference(
                blockId,
                _basePoint,
                _referenceLayerId,
                name: string.IsNullOrWhiteSpace(_referenceName) ? _blockName : _referenceName,
                ownerBlockId: _ownerBlockId);
            _createdReferenceId = reference.Id;
        }

        document.RefreshBlockReferenceBounds();
        return CreateChangeSet(reference.Id, CadEntityChangeKind.Created | CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_createdBlockId is not { } blockId)
            return CadDocumentChangeSet.Empty;

        if (_createdReferenceId is { } referenceId &&
            document.TryGetEntity(referenceId, out var reference) &&
            reference is not null)
        {
            reference.Erase();
        }

        foreach (var (entityId, ownerBlockId) in _originalOwners)
            document.MoveEntityToBlock(entityId, ownerBlockId);
        document.RemoveBlockDefinition(blockId);
        document.RefreshBlockReferenceBounds();

        return CreateChangeSet(
            _createdReferenceId,
            CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }

    private CadDocumentChangeSet CreateChangeSet(EntityId? referenceId, CadEntityChangeKind referenceKind)
    {
        var changes = _entityIds
            .Select(id => new CadEntityChange(id, CadEntityChangeKind.Geometry | CadEntityChangeKind.Metadata))
            .ToList();
        if (referenceId is { } id)
            changes.Add(new CadEntityChange(id, referenceKind));
        return new CadDocumentChangeSet(changes) { AffectsDocumentStructure = true };
    }
}

public sealed class InsertBlockReferenceCommand : ICadCommand
{
    private readonly BlockId _definitionBlockId;
    private readonly BlockId _ownerBlockId;
    private readonly CadPointD _position;
    private readonly LayerId _layerId;
    private readonly double _rotationRadians;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly string _name;
    private EntityId? _createdEntityId;

    public InsertBlockReferenceCommand(
        BlockId definitionBlockId,
        BlockId ownerBlockId,
        CadPointD position,
        LayerId layerId,
        double rotationRadians = 0,
        double scaleX = 1,
        double scaleY = 1,
        string name = "")
    {
        _definitionBlockId = definitionBlockId;
        _ownerBlockId = ownerBlockId;
        _position = position;
        _layerId = layerId;
        _rotationRadians = rotationRadians;
        _scaleX = scaleX;
        _scaleY = scaleY;
        _name = name;
    }

    public string Name => "Insert Block";
    public EntityId? CreatedEntityId => _createdEntityId;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadEntityAccessPolicy.EnsureCanAddToLayer(document, _layerId);
        CadBlockReference reference;
        if (_createdEntityId is { } entityId &&
            document.TryGetEntity(entityId, out var existing) &&
            existing is CadBlockReference existingReference)
        {
            existingReference.Restore();
            reference = existingReference;
        }
        else
        {
            reference = document.AddBlockReference(
                _definitionBlockId,
                _position,
                _layerId,
                rotationRadians: _rotationRadians,
                scaleX: _scaleX,
                scaleY: _scaleY,
                name: _name,
                ownerBlockId: _ownerBlockId);
            _createdEntityId = reference.Id;
        }

        document.RefreshBlockReferenceBounds();
        return CadDocumentChangeSet.ForEntity(
            reference.Id,
            CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_createdEntityId is not { } entityId ||
            !document.TryGetEntity(entityId, out var entity) ||
            entity is null)
        {
            return CadDocumentChangeSet.Empty;
        }

        entity.Erase();
        document.RefreshBlockReferenceBounds();
        return CadDocumentChangeSet.ForEntity(entityId, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }
}

public sealed class RenameBlockCommand(BlockId blockId, string name) : ICadCommand
{
    private string? _oldName;
    public string Name => "Rename Block";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _oldName ??= document.GetBlock(blockId).Name;
        document.RenameBlockDefinition(blockId, name);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.BlockMetadata);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_oldName is null)
            return CadDocumentChangeSet.Empty;
        document.RenameBlockDefinition(blockId, _oldName);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.BlockMetadata);
    }
}

public sealed class DeleteBlockDefinitionCommand(BlockId blockId) : ICadCommand
{
    private CadDetachedBlockDefinition? _snapshot;
    public string Name => "Delete Block";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _snapshot = document.DetachBlockDefinition(blockId);
        return CadDocumentChangeSet
            .ForEntities(_snapshot.Entities.Select(entity => entity.Id), CadEntityChangeKind.Deleted)
            .WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_snapshot is null)
            return CadDocumentChangeSet.Empty;
        document.RestoreBlockDefinition(_snapshot);
        return CadDocumentChangeSet
            .ForEntities(_snapshot.Entities.Select(entity => entity.Id), CadEntityChangeKind.Created)
            .WithDocumentStructureChanged();
    }
}

public sealed class SetBlockReferenceTransformCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _position;
    private readonly double _rotationRadians;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private CadPointD _oldPosition;
    private double _oldRotationRadians;
    private double _oldScaleX;
    private double _oldScaleY;
    private bool _captured;

    public SetBlockReferenceTransformCommand(
        EntityId entityId,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY)
    {
        _entityId = entityId;
        _position = position;
        _rotationRadians = rotationRadians;
        _scaleX = scaleX;
        _scaleY = scaleY;
    }

    public string Name => "Set Block Transform";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var reference = GetReference(document);
        if (!_captured)
        {
            _oldPosition = reference.Position;
            _oldRotationRadians = reference.RotationRadians;
            _oldScaleX = reference.ScaleX;
            _oldScaleY = reference.ScaleY;
            _captured = true;
        }

        Apply(document, reference, _position, _rotationRadians, _scaleX, _scaleY);
        return ChangeSet();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        Apply(document, GetReference(document), _oldPosition, _oldRotationRadians, _oldScaleX, _oldScaleY);
        return ChangeSet();
    }

    private void Apply(
        CadDocument document,
        CadBlockReference reference,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY)
    {
        reference.SetPosition(position);
        reference.SetRotation(rotationRadians);
        reference.SetScale(scaleX, scaleY);
        document.RefreshBlockReferenceBounds();
    }

    private CadBlockReference GetReference(CadDocument document) =>
        document.GetEntity(_entityId) as CadBlockReference ??
        throw new InvalidOperationException($"Entity is not a block reference: {_entityId}");

    private CadDocumentChangeSet ChangeSet() => CadDocumentChangeSet.ForEntity(
        _entityId,
        CadEntityChangeKind.Geometry | CadEntityChangeKind.Rotation);
}

public sealed class SetBlockReferenceDefinitionCommand(EntityId entityId, BlockId definitionBlockId) : ICadCommand
{
    private BlockId? _oldDefinitionBlockId;
    public string Name => "Set Block Definition";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, entityId);
        var reference = document.GetEntity(entityId) as CadBlockReference ??
                        throw new InvalidOperationException($"Entity is not a block reference: {entityId}");
        _oldDefinitionBlockId ??= reference.DefinitionBlockId;
        document.ChangeBlockReferenceDefinition(entityId, definitionBlockId);
        return ChangeSet();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_oldDefinitionBlockId is not { } oldDefinitionBlockId)
            return CadDocumentChangeSet.Empty;
        document.ChangeBlockReferenceDefinition(entityId, oldDefinitionBlockId);
        return ChangeSet();
    }

    private CadDocumentChangeSet ChangeSet() => CadDocumentChangeSet.ForEntity(
        entityId,
        CadEntityChangeKind.Geometry | CadEntityChangeKind.Metadata);
}

public sealed class SetBlockDefinitionBasePointCommand(BlockId blockId, CadPointD basePoint) : ICadCommand
{
    private CadPointD _oldBasePoint;
    private bool _captured;
    public string Name => "Set Block Base Point";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if (!_captured)
        {
            _oldBasePoint = document.GetBlock(blockId).BasePoint;
            _captured = true;
        }
        document.SetBlockDefinitionBasePoint(blockId, basePoint);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        document.SetBlockDefinitionBasePoint(blockId, _oldBasePoint);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }
}
