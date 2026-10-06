using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public interface ICadCommand
{
    string Name { get; }

    /// <summary>
    /// Applies a document edit. Validate all fallible inputs before mutation, or
    /// restore the original state before throwing. Managers can roll back only
    /// commands that have completed successfully.
    /// </summary>
    CadDocumentChangeSet Execute(CadDocument document);

    /// <summary>Reverses a successful edit; on failure the document must remain unchanged.</summary>
    CadDocumentChangeSet Undo(CadDocument document);
}
