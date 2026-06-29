using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddSplineCommand : ICadCommand
{
    private readonly CadPointD[] _fitPoints;
    private readonly bool _closed;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly string _name;
    private EntityId? _createdEntityId;

    public string Name => "Add Spline";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddSplineCommand(
        IEnumerable<CadPointD> fitPoints,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "")
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

        var spline = document.AddSpline(
            _fitPoints,
            _closed,
            _layerId,
            _graphicStyleId,
            _name);
        _createdEntityId = spline.Id;

        return CadDocumentChangeSet.ForEntity(
            spline.Id,
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
