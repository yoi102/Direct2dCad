using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public interface ICadCommand
{
    string Name { get; }
    CadDocumentChangeSet Execute(CadDocument document);
    CadDocumentChangeSet Undo(CadDocument document);
}
