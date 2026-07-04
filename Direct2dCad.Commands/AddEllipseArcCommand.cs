using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddEllipseArcCommand : ICadCommand
{
    private readonly CadPointD _center;
    private readonly double _radiusX;
    private readonly double _radiusY;
    private readonly double _startAngleRadians;
    private readonly double _sweepAngleRadians;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly string _name;
    private readonly CadLineWeight? _lineWeight;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private EntityId? _createdEntityId;

    public string Name => "Add Ellipse Arc";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddEllipseArcCommand(
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngleRadians,
        double sweepAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        _center = center;
        _radiusX = radiusX;
        _radiusY = radiusY;
        _startAngleRadians = startAngleRadians;
        _sweepAngleRadians = sweepAngleRadians;
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

        if (_createdEntityId is not null &&
            document.TryGetEntity(_createdEntityId.Value, out var existing) &&
            existing is not null)
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

        var ellipseArc = document.AddEllipseArc(
            _center,
            _radiusX,
            _radiusY,
            _startAngleRadians,
            _sweepAngleRadians,
            _layerId,
            _graphicStyleId,
            _name);
        ellipseArc.SetLineWeight(_lineWeight);
        ellipseArc.SetZIndex(_zIndex);
        ellipseArc.SetVisible(_isVisible);

        _createdEntityId = ellipseArc.Id;
        return CadDocumentChangeSet.ForEntity(
            ellipseArc.Id,
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.DrawOrder);
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
