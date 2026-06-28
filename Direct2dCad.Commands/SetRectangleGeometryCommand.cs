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
        rectangle.SetBounds(_bounds);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousBounds is null)
            return CadDocumentChangeSet.Empty;

        GetRectangle(document).SetBounds(_previousBounds.Value);
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
