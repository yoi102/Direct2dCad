using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetArcGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _center;
    private readonly double _radius;
    private readonly double _startAngleRadians;
    private readonly double _sweepAngleRadians;
    private CadPointD? _previousCenter;
    private double? _previousRadius;
    private double? _previousStartAngleRadians;
    private double? _previousSweepAngleRadians;

    public string Name => "Set Arc Geometry";

    public SetArcGeometryCommand(
        EntityId entityId,
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        _entityId = entityId;
        _center = center;
        _radius = radius;
        _startAngleRadians = startAngleRadians;
        _sweepAngleRadians = sweepAngleRadians;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var arc = GetArc(document);
        _previousCenter = arc.Center;
        _previousRadius = arc.Radius;
        _previousStartAngleRadians = arc.StartAngleRadians;
        _previousSweepAngleRadians = arc.SweepAngleRadians;
        arc.SetGeometry(_center, _radius, _startAngleRadians, _sweepAngleRadians);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousCenter is null ||
            _previousRadius is null ||
            _previousStartAngleRadians is null ||
            _previousSweepAngleRadians is null)
        {
            return CadDocumentChangeSet.Empty;
        }

        GetArc(document).SetGeometry(
            _previousCenter.Value,
            _previousRadius.Value,
            _previousStartAngleRadians.Value,
            _previousSweepAngleRadians.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadArc GetArc(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadArc arc
            ? arc
            : throw new InvalidOperationException($"Entity is not arc: {_entityId}");
    }
}
