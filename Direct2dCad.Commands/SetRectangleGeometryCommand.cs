using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetRectangleGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadRectD _bounds;
    private CadRectD? _previousBounds;
    private double? _previousCornerRadiusX;
    private double? _previousCornerRadiusY;

    public string Name => "Set Rectangle Geometry";

    public SetRectangleGeometryCommand(EntityId entityId, CadRectD bounds)
    {
        _entityId = entityId;
        _bounds = bounds;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var rectangle = GetRectangle(document);
        _previousBounds = rectangle.Bounds;
        _previousCornerRadiusX = rectangle.CornerRadiusX;
        _previousCornerRadiusY = rectangle.CornerRadiusY;
        rectangle.SetBounds(_bounds);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousBounds is null || _previousCornerRadiusX is null || _previousCornerRadiusY is null)
            return CadDocumentChangeSet.Empty;

        var rectangle = GetRectangle(document);
        rectangle.SetBounds(_previousBounds.Value);
        rectangle.SetCornerRadius(_previousCornerRadiusX.Value, _previousCornerRadiusY.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadRectangle GetRectangle(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadRectangle rectangle
            ? rectangle
            : throw new InvalidOperationException($"Entity is not rectangle: {_entityId}");
    }
}
