using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetEllipseArcGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _center;
    private readonly double _radiusX;
    private readonly double _radiusY;
    private readonly double _startAngleRadians;
    private readonly double _sweepAngleRadians;
    private GeometrySnapshot? _previous;

    public SetEllipseArcGeometryCommand(
        EntityId entityId,
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        _entityId = entityId;
        _center = center;
        _radiusX = radiusX;
        _radiusY = radiusY;
        _startAngleRadians = startAngleRadians;
        _sweepAngleRadians = sweepAngleRadians;
    }

    public string Name => "Set Ellipse Arc Geometry";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadEntityAccessPolicy.EnsureEditable(document, document.GetEntity(_entityId));
        var ellipseArc = GetEllipseArc(document);
        _previous = new GeometrySnapshot(
            ellipseArc.Center,
            ellipseArc.RadiusX,
            ellipseArc.RadiusY,
            ellipseArc.StartAngleRadians,
            ellipseArc.SweepAngleRadians);
        ellipseArc.SetGeometry(
            _center,
            _radiusX,
            _radiusY,
            _startAngleRadians,
            _sweepAngleRadians);
        return ChangeSet();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is not { } previous)
            return CadDocumentChangeSet.Empty;

        GetEllipseArc(document).SetGeometry(
            previous.Center,
            previous.RadiusX,
            previous.RadiusY,
            previous.StartAngleRadians,
            previous.SweepAngleRadians);
        return ChangeSet();
    }

    private CadEllipseArc GetEllipseArc(CadDocument document) =>
        document.GetEntity(_entityId) as CadEllipseArc ??
        throw new InvalidOperationException($"Entity is not an ellipse arc: {_entityId}");

    private CadDocumentChangeSet ChangeSet() =>
        CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);

    private readonly record struct GeometrySnapshot(
        CadPointD Center,
        double RadiusX,
        double RadiusY,
        double StartAngleRadians,
        double SweepAngleRadians);
}
