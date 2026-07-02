using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddCircleCommand : ICadCommand
{
    private readonly CadPointD _center;
    private readonly double _radius;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly StyleId? _fillStyleId;
    private readonly string _name;
    private readonly CadLineWeight? _lineWeight;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private EntityId? _createdEntityId;

    public string Name => "Add Circle";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddCircleCommand(
        CadPointD center,
        double radius,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        _center = center;
        _radius = radius;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _fillStyleId = fillStyleId;
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

        var circle = document.AddCircle(_center, _radius, _layerId, _graphicStyleId, _fillStyleId, _name);
        circle.SetLineWeight(_lineWeight);
        circle.SetZIndex(_zIndex);
        circle.SetVisible(_isVisible);

        _createdEntityId = circle.Id;
        return CadDocumentChangeSet.ForEntity(
            circle.Id,
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
