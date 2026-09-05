using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class RenameLineTypeCommand(LineTypeId lineTypeId, string name) : ICadCommand
{
    private string? _oldName;
    public string Name => "Rename Line Type";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _oldName ??= document.GetLineType(lineTypeId).Name;
        document.RenameLineType(lineTypeId, name);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_oldName is not null)
            document.RenameLineType(lineTypeId, _oldName);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
