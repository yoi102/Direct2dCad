using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetRectangleCornerRadiusCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly double _radiusX;
    private readonly double _radiusY;
    private double? _previousRadiusX;
    private double? _previousRadiusY;

    public string Name => "Set Rectangle Corner Radius";

    public SetRectangleCornerRadiusCommand(EntityId entityId, double radiusX, double radiusY)
    {
        _entityId = entityId;
        _radiusX = radiusX;
        _radiusY = radiusY;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var rectangle = GetRectangle(document);
        _previousRadiusX = rectangle.CornerRadiusX;
        _previousRadiusY = rectangle.CornerRadiusY;
        rectangle.SetCornerRadius(_radiusX, _radiusY);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousRadiusX is null || _previousRadiusY is null)
            return CadDocumentChangeSet.Empty;

        GetRectangle(document).SetCornerRadius(_previousRadiusX.Value, _previousRadiusY.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    private CadRectangle GetRectangle(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadRectangle rectangle
            ? rectangle
            : throw new InvalidOperationException($"Entity is not rectangle: {_entityId}");
    }
}
