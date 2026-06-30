using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;

namespace Direct2dCad.Commands;

public sealed class SetShapeTextFontCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadShapeFontId _shapeFontId;
    private readonly Dictionary<EntityId, CadShapeFontId> _previousShapeFontIds = [];

    public string Name => "Set Shape Text Font";

    public SetShapeTextFontCommand(EntityId entityId, CadShapeFontId shapeFontId)
        : this([entityId], shapeFontId)
    {
    }

    public SetShapeTextFontCommand(IEnumerable<EntityId> entityIds, CadShapeFontId shapeFontId)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _shapeFontId = CadShapeFontRegistry.GetOrDefault(shapeFontId).Id;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousShapeFontIds.Clear();

        var texts = _entityIds
            .Select(id => GetShapeText(document, id))
            .ToArray();

        foreach (var text in texts)
            _previousShapeFontIds[text.Id] = text.ShapeFontId;

        foreach (var text in texts)
            text.SetShapeFont(_shapeFontId);

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, shapeFontId) in _previousShapeFontIds)
            GetShapeText(document, entityId).SetShapeFont(shapeFontId);

        return CadDocumentChangeSet.ForEntities(_previousShapeFontIds.Keys, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    private static CadShapeText GetShapeText(CadDocument document, EntityId entityId)
    {
        return document.GetEntity(entityId) is CadShapeText text
            ? text
            : throw new InvalidOperationException($"Entity is not shape text: {entityId}");
    }
}
