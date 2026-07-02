using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddLineCommand : ICadCommand
{
    private readonly CadPointD _start;
    private readonly CadPointD _end;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly string _name;
    private readonly CadLineWeight? _lineWeight;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private EntityId? _createdEntityId;

    public string Name => "Add Line";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddLineCommand(
        CadPointD start,
        CadPointD end,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        _start = start;
        _end = end;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _name = name;
        _lineWeight = lineWeight;
        _zIndex = zIndex;
        _isVisible = isVisible;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is not null && document.TryGetEntity(_createdEntityId.Value, out var existing) && existing is not null)
        {
            existing.Restore();
            return CadDocumentChangeSet.ForEntity(
                existing.Id,
                CadEntityChangeKind.Created |
                CadEntityChangeKind.Geometry |
                CadEntityChangeKind.Appearance |
                CadEntityChangeKind.Visibility |
                CadEntityChangeKind.DrawOrder);
        }

        var line = document.AddLine(_start, _end, _layerId, _graphicStyleId, _name);
        line.SetLineWeight(_lineWeight);
        line.SetZIndex(_zIndex);
        line.SetVisible(_isVisible);

        _createdEntityId = line.Id;
        return CadDocumentChangeSet.ForEntity(
            line.Id,
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.DrawOrder);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is null || !document.TryGetEntity(_createdEntityId.Value, out var entity) || entity is null)
            return CadDocumentChangeSet.Empty;

        entity.Erase();
        return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }
}
