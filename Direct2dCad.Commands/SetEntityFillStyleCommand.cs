using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;

namespace Direct2dCad.Commands;

public sealed class SetEntityFillStyleCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly StyleId? _fillStyleId;
    private readonly Dictionary<EntityId, StyleId?> _previousFillStyles = [];

    public string Name => "Set Entity Fill Style";

    public SetEntityFillStyleCommand(IEnumerable<EntityId> entityIds, StyleId? fillStyleId)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _fillStyleId = fillStyleId;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousFillStyles.Clear();
        ValidateFillStyle(document);

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousFillStyles[entityId] = GetFillStyleId(entity);
            SetFillStyleId(entity, _fillStyleId);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, styleId) in _previousFillStyles)
            SetFillStyleId(document.GetEntity(entityId), styleId);

        return CadDocumentChangeSet.ForEntities(_previousFillStyles.Keys, CadEntityChangeKind.Appearance);
    }

    private static StyleId? GetFillStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadPolyline polyline => polyline.FillStyleId,
            _ => throw new NotSupportedException($"Entity type has no fill style: {entity.GetType().Name}")
        };
    }

    private static void SetFillStyleId(CadEntity entity, StyleId? styleId)
    {
        switch (entity)
        {
            case CadCircle circle:
                circle.SetFillStyleInternal(styleId);
                break;
            case CadPolyline polyline:
                polyline.SetFillStyleInternal(styleId);
                break;
            default:
                throw new NotSupportedException($"Entity type has no fill style: {entity.GetType().Name}");
        }
    }

    private void ValidateFillStyle(CadDocument document)
    {
        if (_fillStyleId is null)
            return;

        if (!document.TryGetStyle(_fillStyleId.Value, out var style))
            throw new InvalidOperationException($"Style does not exist: {_fillStyleId}");

        if (style is not CadFillStyle)
            throw new InvalidOperationException($"Style is not fill style: {_fillStyleId}");
    }
}
