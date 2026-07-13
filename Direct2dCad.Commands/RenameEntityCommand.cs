using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class RenameEntityCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly string _name;
    private string? _previousName;

    public string Name => "Rename Entity";

    public RenameEntityCommand(EntityId entityId, string name)
    {
        _entityId = entityId;
        _name = name ?? string.Empty;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityId);

        var entity = document.GetEntity(_entityId);
        _previousName ??= entity.Name;
        entity.Rename(_name);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Metadata);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousName is null)
            return CadDocumentChangeSet.Empty;

        document.GetEntity(_entityId).Rename(_previousName);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Metadata);
    }
}
