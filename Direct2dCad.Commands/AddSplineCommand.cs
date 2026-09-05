using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddSplineCommand : ICadCommand
{
    private readonly CadPointD[] _fitPoints;
    private readonly bool _closed;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly StyleId? _fillStyleId;
    private readonly string _name;
    private readonly CadLineWeight? _lineWeight;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private readonly CadStrokeStyle _strokeStyle;
    private EntityId? _createdEntityId;

    public string Name => "Add Spline";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddSplineCommand(
        IEnumerable<CadPointD> fitPoints,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true,
        CadStrokeStyle? strokeStyle = null)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        _fitPoints = fitPoints.ToArray();
        if (_fitPoints.Length < 2)
            throw new ArgumentException("Spline requires at least two fit points.", nameof(fitPoints));
        if (closed && _fitPoints.Length < 3)
            throw new ArgumentException("Closed spline requires at least three fit points.", nameof(fitPoints));

        _closed = closed;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _fillStyleId = fillStyleId;
        _name = name;
        _lineWeight = lineWeight;
        _zIndex = zIndex;
        _isVisible = isVisible;
        _strokeStyle = strokeStyle ?? CadStrokeStyle.Default;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadEntityAccessPolicy.EnsureCanAddToLayer(document, _layerId ?? LayerId.Default);

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

        var spline = document.AddSpline(
            _fitPoints,
            _closed,
            _layerId,
            _graphicStyleId,
            _fillStyleId,
            _name);
        spline.SetLineWeight(_lineWeight);
        spline.SetZIndex(_zIndex);
        spline.SetVisible(_isVisible);
        spline.SetStrokeStyle(_strokeStyle);

        _createdEntityId = spline.Id;

        return CadDocumentChangeSet.ForEntity(
            spline.Id,
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
