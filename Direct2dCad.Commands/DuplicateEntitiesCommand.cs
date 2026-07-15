using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class DuplicateEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _sourceEntityIds;
    private readonly CadVectorD _delta;
    private readonly BlockId _ownerBlockId;
    private PasteEntitiesCommand? _pasteCommand;

    public DuplicateEntitiesCommand(
        IEnumerable<EntityId> sourceEntityIds,
        CadVectorD delta,
        BlockId? ownerBlockId = null)
    {
        _sourceEntityIds = sourceEntityIds?.Distinct().ToArray() ??
                           throw new ArgumentNullException(nameof(sourceEntityIds));
        _delta = delta;
        _ownerBlockId = ownerBlockId ?? BlockId.ModelSpace;

        if (_sourceEntityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(sourceEntityIds));
    }

    public string Name => "Duplicate Entities";

    public IReadOnlyList<EntityId> CreatedEntityIds => _pasteCommand?.CreatedEntityIds ?? [];

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var sourceEntityId in _sourceEntityIds)
        {
            if (document.TryGetEntity(sourceEntityId, out var source) &&
                source is { IsErased: false })
            {
                CadEntityAccessPolicy.EnsureCanAddToLayer(document, source.LayerId);
            }
        }

        if (_pasteCommand is not null)
            return _pasteCommand.Execute(document);

        var snapshot = CadClipboardSnapshotFactory.Create(document, _sourceEntityIds);
        if (snapshot is null)
            return CadDocumentChangeSet.Empty;

        _pasteCommand = new PasteEntitiesCommand(
            snapshot,
            _delta,
            targetLayerId: null,
            ownerBlockId: _ownerBlockId);
        return _pasteCommand.Execute(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _pasteCommand?.Undo(document) ?? CadDocumentChangeSet.Empty;
    }
}
