using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class RotateEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadPointD _pivot;
    private readonly double _angleRadians;

    public RotateEntitiesCommand(IEnumerable<EntityId> entityIds, CadPointD pivot, double angleRadians)
    {
        _entityIds = RequireIds(entityIds);
        _pivot = pivot;
        _angleRadians = double.IsFinite(angleRadians)
            ? angleRadians
            : throw new ArgumentOutOfRangeException(nameof(angleRadians));
    }

    public string Name => "Rotate Entities";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        Validate(document);
        foreach (var id in _entityIds)
            CadEntityTransform.Rotate(document.GetEntity(id), _pivot, _angleRadians);
        return ChangeSet(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        foreach (var id in _entityIds)
            CadEntityTransform.Rotate(document.GetEntity(id), _pivot, -_angleRadians);
        return ChangeSet(document);
    }

    private CadDocumentChangeSet ChangeSet(CadDocument document)
    {
        var dependentReferences = document.RefreshBlockReferenceBounds();
        return CadDocumentChangeSet.ForEntities(
            _entityIds.Concat(dependentReferences).Distinct(),
            CadEntityChangeKind.Geometry | CadEntityChangeKind.Rotation);
    }

    private void Validate(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        foreach (var id in _entityIds)
            CadEntityTransform.ValidateRotation(document.GetEntity(id), _angleRadians);
    }

    private static EntityId[] RequireIds(IEnumerable<EntityId> entityIds)
    {
        var ids = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        return ids.Length > 0 ? ids : throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }
}

public sealed class ScaleEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadPointD _pivot;
    private readonly double _factor;

    public ScaleEntitiesCommand(IEnumerable<EntityId> entityIds, CadPointD pivot, double factor)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
        _pivot = pivot;
        _factor = double.IsFinite(factor) && factor > 0
            ? factor
            : throw new ArgumentOutOfRangeException(nameof(factor), "Scale factor must be greater than zero.");
    }

    public string Name => "Scale Entities";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        Validate(document, _factor);
        foreach (var id in _entityIds)
            CadEntityTransform.UniformScale(document.GetEntity(id), _pivot, _factor);
        return ChangeSet(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        var inverse = 1.0 / _factor;
        foreach (var id in _entityIds)
            CadEntityTransform.UniformScale(document.GetEntity(id), _pivot, inverse);
        return ChangeSet(document);
    }

    private void Validate(CadDocument document, double factor)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        foreach (var id in _entityIds)
            CadEntityTransform.ValidateUniformScale(document.GetEntity(id), factor);
    }

    private CadDocumentChangeSet ChangeSet(CadDocument document)
    {
        var dependentReferences = document.RefreshBlockReferenceBounds();
        return CadDocumentChangeSet.ForEntities(
            _entityIds.Concat(dependentReferences).Distinct(),
            CadEntityChangeKind.Geometry);
    }
}

public sealed class MirrorEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadPointD _axisPoint;
    private readonly double _axisAngleRadians;

    public MirrorEntitiesCommand(IEnumerable<EntityId> entityIds, CadPointD axisPoint, double axisAngleRadians)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
        _axisPoint = axisPoint;
        _axisAngleRadians = double.IsFinite(axisAngleRadians)
            ? axisAngleRadians
            : throw new ArgumentOutOfRangeException(nameof(axisAngleRadians));
    }

    public string Name => "Mirror Entities";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        Validate(document);
        Apply(document);
        return ChangeSet(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        Apply(document);
        return ChangeSet(document);
    }

    private void Validate(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        foreach (var id in _entityIds)
            CadEntityTransform.ValidateMirror(document.GetEntity(id), _axisAngleRadians);
    }

    private void Apply(CadDocument document)
    {
        foreach (var id in _entityIds)
            CadEntityTransform.Mirror(document.GetEntity(id), _axisPoint, _axisAngleRadians);
    }

    private CadDocumentChangeSet ChangeSet(CadDocument document)
    {
        var dependentReferences = document.RefreshBlockReferenceBounds();
        return CadDocumentChangeSet.ForEntities(
            _entityIds.Concat(dependentReferences).Distinct(),
            CadEntityChangeKind.Geometry | CadEntityChangeKind.Rotation);
    }
}
