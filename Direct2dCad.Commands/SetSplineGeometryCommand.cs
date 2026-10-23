using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetSplineGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadSplineGeometry _geometry;
    private CadSplineGeometry? _previousGeometry;

    public string Name => "Set Spline Geometry";

    public SetSplineGeometryCommand(
        EntityId entityId,
        IEnumerable<CadPointD> fitPoints,
        bool closed)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        _entityId = entityId;
        _geometry = CadSplineGeometry.FromFitPoints(fitPoints, closed);
    }

    public SetSplineGeometryCommand(EntityId entityId, CadSplineGeometry geometry)
    { _entityId = entityId; _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry)); }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var spline = GetSpline(document);
        _previousGeometry = spline.Geometry;
        spline.SetGeometry(_geometry);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousGeometry is null)
            return CadDocumentChangeSet.Empty;

        var spline = GetSpline(document);
        spline.SetGeometry(_previousGeometry);
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
