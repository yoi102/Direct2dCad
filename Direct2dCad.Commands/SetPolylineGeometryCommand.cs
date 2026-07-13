using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetPolylineGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD[] _points;
    private readonly bool _closed;
    private CadPointD[]? _previousPoints;
    private bool? _previousClosed;

    public string Name => "Set Polyline Geometry";

    public SetPolylineGeometryCommand(
        EntityId entityId,
        IEnumerable<CadPointD> points,
        bool closed)
    {
        ArgumentNullException.ThrowIfNull(points);

        _entityId = entityId;
        _points = points.ToArray();
        if (_points.Length < 2)
            throw new ArgumentException("Polyline requires at least two points.", nameof(points));
        if (closed && _points.Length < 3)
            throw new ArgumentException("Closed polyline requires at least three points.", nameof(points));

        _closed = closed;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var polyline = GetPolyline(document);
        _previousPoints = polyline.Points.ToArray();
        _previousClosed = polyline.Closed;

        polyline.ReplacePoints(_points);
        polyline.SetClosed(_closed);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousPoints is null || _previousClosed is null)
            return CadDocumentChangeSet.Empty;

        var polyline = GetPolyline(document);
        polyline.ReplacePoints(_previousPoints);
        polyline.SetClosed(_previousClosed.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadPolyline GetPolyline(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadPolyline polyline
            ? polyline
            : throw new InvalidOperationException($"Entity is not polyline: {_entityId}");
    }
}
