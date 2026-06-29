using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetEllipseGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _center;
    private readonly double _radiusX;
    private readonly double _radiusY;
    private CadPointD? _previousCenter;
    private double? _previousRadiusX;
    private double? _previousRadiusY;

    public string Name => "Set Ellipse Geometry";

    public SetEllipseGeometryCommand(EntityId entityId, CadPointD center, double radiusX, double radiusY)
    {
        _entityId = entityId;
        _center = center;
        _radiusX = radiusX;
        _radiusY = radiusY;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var ellipse = GetEllipse(document);
        _previousCenter = ellipse.Center;
        _previousRadiusX = ellipse.RadiusX;
        _previousRadiusY = ellipse.RadiusY;
        ellipse.SetGeometry(_center, _radiusX, _radiusY);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousCenter is null || _previousRadiusX is null || _previousRadiusY is null)
            return CadDocumentChangeSet.Empty;

        GetEllipse(document).SetGeometry(_previousCenter.Value, _previousRadiusX.Value, _previousRadiusY.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadEllipse GetEllipse(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadEllipse ellipse
            ? ellipse
            : throw new InvalidOperationException($"Entity is not ellipse: {_entityId}");
    }
}
