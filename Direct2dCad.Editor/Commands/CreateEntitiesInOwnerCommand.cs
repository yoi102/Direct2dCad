using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Editor.Commands;

// Pins the destination for both initial execution and redo, before change publication.
internal sealed class CreateEntitiesInOwnerCommand(ICadCommand inner, BlockId ownerId) : ICadCommand
{
    public string Name => inner.Name;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if (!document.TryGetBlock(ownerId, out var owner) || owner is null)
            throw new InvalidOperationException($"Destination block does not exist: {ownerId}");
        var result = inner.Execute(document);
        try
        {
            foreach (var change in result.EntityChanges)
            {
                if ((change.Kind & CadEntityChangeKind.Created) != 0)
                    document.MoveEntityToBlock(change.EntityId, ownerId);
            }
            return result;
        }
        catch
        {
            inner.Undo(document);
            throw;
        }
    }

    public CadDocumentChangeSet Undo(CadDocument document) => inner.Undo(document);
}
