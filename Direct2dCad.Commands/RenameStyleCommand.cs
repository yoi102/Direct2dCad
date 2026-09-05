using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class RenameStyleCommand(StyleId styleId, string name) : ICadCommand
{
    private string? _oldName;
    public string Name => "Rename Style";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _oldName ??= document.GetStyle<Direct2dCad.Db.Data.Styles.CadStyle>(styleId).Name;
        document.RenameStyle(styleId, name);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_oldName is null)
            return CadDocumentChangeSet.Empty;
        document.RenameStyle(styleId, _oldName);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
