using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetCircleGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _center;
    private readonly double _radius;
    private CadPointD? _previousCenter;
    private double? _previousRadius;

    public string Name => "Set Circle Geometry";

    public SetCircleGeometryCommand(EntityId entityId, CadPointD center, double radius)
    {
        _entityId = entityId;
        _center = center;
        _radius = radius;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var circle = GetCircle(document);
        _previousCenter = circle.Center;
        _previousRadius = circle.Radius;
        circle.SetGeometry(_center, _radius);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousCenter is null || _previousRadius is null)
            return CadDocumentChangeSet.Empty;

        GetCircle(document).SetGeometry(_previousCenter.Value, _previousRadius.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadCircle GetCircle(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadCircle circle
            ? circle
            : throw new InvalidOperationException($"Entity is not circle: {_entityId}");
    }
}
