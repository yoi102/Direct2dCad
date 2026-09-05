using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddCompositePathCommand : ICadCommand
{
    private readonly CadPointD _startPoint;
    private readonly CadCompositePathSegment[] _segments;
    private readonly bool _closed;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly StyleId? _fillStyleId;
    private readonly string _name;
    private readonly CadLineWeight? _lineWeight;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private EntityId? _createdEntityId;

    public string Name => "Add Composite Path";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddCompositePathCommand(
        CadPointD startPoint,
        IEnumerable<CadCompositePathSegment> segments,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        _startPoint = startPoint;
        _segments = segments?.ToArray() ?? throw new ArgumentNullException(nameof(segments));
        if (_segments.Length == 0)
            throw new ArgumentException("A composite path requires at least one segment.", nameof(segments));
        _closed = closed;
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
        CadEntityAccessPolicy.EnsureCanAddToLayer(document, _layerId ?? LayerId.Default);

        if (_createdEntityId is { } existingId &&
            document.TryGetEntity(existingId, out var existing) &&
            existing is CadCompositePath)
        {
            existing.Restore();
            return Changed(existing.Id, CadEntityChangeKind.Created | CadEntityChangeKind.Visibility);
        }

        var path = document.AddCompositePath(
            _startPoint,
            _segments,
            _closed,
            _layerId,
            _graphicStyleId,
            _fillStyleId,
            _name);
        path.SetLineWeight(_lineWeight);
        path.SetZIndex(_zIndex);
        path.SetVisible(_isVisible);
        _createdEntityId = path.Id;
        return Changed(path.Id, CadEntityChangeKind.Created | CadEntityChangeKind.Visibility);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_createdEntityId is not { } id ||
            !document.TryGetEntity(id, out var entity) ||
            entity is null)
        {
            return CadDocumentChangeSet.Empty;
        }

        entity.Erase();
        return CadDocumentChangeSet.ForEntity(id, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }

    private static CadDocumentChangeSet Changed(EntityId id, CadEntityChangeKind extra) =>
        CadDocumentChangeSet.ForEntity(
            id,
            extra |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Fill |
            CadEntityChangeKind.DrawOrder);
}

public sealed class SetCompositePathGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _startPoint;
    private readonly CadCompositePathSegment[] _segments;
    private readonly bool _closed;
    private CadPointD _previousStartPoint;
    private CadCompositePathSegment[]? _previousSegments;
    private bool _previousClosed;

    public string Name => "Set Composite Path Geometry";

    public SetCompositePathGeometryCommand(
        EntityId entityId,
        CadPointD startPoint,
        IEnumerable<CadCompositePathSegment> segments,
        bool closed)
    {
        _entityId = entityId;
        _startPoint = startPoint;
        _segments = segments?.ToArray() ?? throw new ArgumentNullException(nameof(segments));
        if (_segments.Length == 0)
            throw new ArgumentException("A composite path requires at least one segment.", nameof(segments));
        _closed = closed;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var path = GetPath(document);
        _previousStartPoint = path.StartPoint;
        _previousSegments = path.Segments.ToArray();
        _previousClosed = path.Closed;
        path.ReplaceGeometry(_startPoint, _segments, _closed);
        return CadCommandGeometryChanges.Resolve(document, [_entityId], CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousSegments is null)
            return CadDocumentChangeSet.Empty;
        var path = GetPath(document);
        path.ReplaceGeometry(_previousStartPoint, _previousSegments, _previousClosed);
        return CadCommandGeometryChanges.Resolve(document, [_entityId], CadEntityChangeKind.Geometry);
    }

    private CadCompositePath GetPath(CadDocument document) =>
        document.GetEntity(_entityId) as CadCompositePath ??
        throw new InvalidOperationException($"Entity is not a composite path: {_entityId}");
}
