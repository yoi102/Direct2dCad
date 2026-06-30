using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddPolylineCommand : ICadCommand
{
    private readonly CadPointD[] _points;
    private readonly bool _closed;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly StyleId? _fillStyleId;
    private readonly string _name;
    private EntityId? _createdEntityId;

    public string Name => "Add Polyline";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddPolylineCommand(
        IEnumerable<CadPointD> points,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(points);

        _points = points.ToArray();
        if (_points.Length < 2)
            throw new ArgumentException("Polyline requires at least two points.", nameof(points));

        if (closed && _points.Length < 3)
            throw new ArgumentException("Closed polyline requires at least three points.", nameof(points));

        _closed = closed;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _fillStyleId = fillStyleId;
        _name = name;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is not null &&
            document.TryGetEntity(_createdEntityId.Value, out var existing) &&
            existing is not null)
        {
            existing.Restore();
            return CadDocumentChangeSet.ForEntity(
                existing.Id,
                CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance | CadEntityChangeKind.Visibility);
        }

        var polyline = document.AddPolyline(
            _points,
            _closed,
            _layerId,
            _graphicStyleId,
            _fillStyleId,
            _name);
        _createdEntityId = polyline.Id;

        return CadDocumentChangeSet.ForEntity(
            polyline.Id,
            CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is null ||
            !document.TryGetEntity(_createdEntityId.Value, out var entity) ||
            entity is null)
        {
            return CadDocumentChangeSet.Empty;
        }

        entity.Erase();
        return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }
}
