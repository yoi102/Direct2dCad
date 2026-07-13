using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetOleObjectBoundsCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadRectD _bounds;
    private CadRectD? _previousBounds;

    public string Name => "Set OLE Object Bounds";

    public SetOleObjectBoundsCommand(EntityId entityId, CadRectD bounds)
    {
        _entityId = entityId;
        _bounds = bounds;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var oleObject = GetOleObject(document);
        _previousBounds = oleObject.Bounds;
        oleObject.SetBounds(_bounds);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousBounds is null)
            return CadDocumentChangeSet.Empty;

        GetOleObject(document).SetBounds(_previousBounds.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadOleObject GetOleObject(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadOleObject oleObject
            ? oleObject
            : throw new InvalidOperationException($"Entity is not OLE object: {_entityId}");
    }
}
