using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetSplineGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD[] _fitPoints;
    private readonly bool _closed;
    private CadPointD[]? _previousFitPoints;
    private bool? _previousClosed;

    public string Name => "Set Spline Geometry";

    public SetSplineGeometryCommand(
        EntityId entityId,
        IEnumerable<CadPointD> fitPoints,
        bool closed)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        _entityId = entityId;
        _fitPoints = fitPoints.ToArray();
        if (_fitPoints.Length < 2)
            throw new ArgumentException("Spline requires at least two fit points.", nameof(fitPoints));
        if (closed && _fitPoints.Length < 3)
            throw new ArgumentException("Closed spline requires at least three fit points.", nameof(fitPoints));

        _closed = closed;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var spline = GetSpline(document);
        _previousFitPoints = spline.FitPoints.ToArray();
        _previousClosed = spline.Closed;

        spline.ReplaceFitPoints(_fitPoints);
        spline.SetClosed(_closed);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousFitPoints is null || _previousClosed is null)
            return CadDocumentChangeSet.Empty;

        var spline = GetSpline(document);
        spline.ReplaceFitPoints(_previousFitPoints);
        spline.SetClosed(_previousClosed.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadSpline GetSpline(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadSpline spline
            ? spline
            : throw new InvalidOperationException($"Entity is not spline: {_entityId}");
    }
}
