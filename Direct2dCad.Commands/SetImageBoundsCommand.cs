using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetImageBoundsCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadRectD _bounds;
    private CadRectD? _previousBounds;

    public string Name => "Set Image Bounds";

    public SetImageBoundsCommand(EntityId entityId, CadRectD bounds)
    {
        _entityId = entityId;
        _bounds = bounds;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var image = GetImage(document);
        _previousBounds = image.FrameBounds;
        image.SetBounds(_bounds);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousBounds is null)
            return CadDocumentChangeSet.Empty;

        GetImage(document).SetBounds(_previousBounds.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadImage GetImage(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadImage image
            ? image
            : throw new InvalidOperationException($"Entity is not image: {_entityId}");
    }
}
