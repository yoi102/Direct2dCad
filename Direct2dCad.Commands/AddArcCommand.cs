using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddArcCommand : ICadCommand
{
    private readonly CadPointD _center;
    private readonly double _radius;
    private readonly double _startAngleRadians;
    private readonly double _sweepAngleRadians;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly string _name;
    private EntityId? _createdEntityId;

    public string Name => "Add Arc";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddArcCommand(
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "")
    {
        _center = center;
        _radius = radius;
        _startAngleRadians = startAngleRadians;
        _sweepAngleRadians = sweepAngleRadians;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _name = name;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is not null && document.TryGetEntity(_createdEntityId.Value, out var existing) && existing is not null)
        {
            existing.Restore();
            return CadDocumentChangeSet.ForEntity(existing.Id, CadEntityChangeKind.Created | CadEntityChangeKind.Visibility);
        }

        var arc = document.AddArc(
            _center,
            _radius,
            _startAngleRadians,
            _sweepAngleRadians,
            _layerId,
            _graphicStyleId,
            _name);
        _createdEntityId = arc.Id;
        return CadDocumentChangeSet.ForEntity(arc.Id, CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
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
