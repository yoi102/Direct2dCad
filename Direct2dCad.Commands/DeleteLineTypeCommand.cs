using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class DeleteLineTypeCommand(LineTypeId lineTypeId) : ICadCommand
{
    private CadLineTypeDefinition? _snapshot;
    public string Name => "Delete Line Type";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _snapshot ??= document.GetLineType(lineTypeId);
        if (document.GetLineTypeReferenceCount(lineTypeId) > 0)
            throw new InvalidOperationException($"Line type is still referenced: {_snapshot.Name}");
        document.RemoveLineType(lineTypeId);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_snapshot is not null && !document.LineTypes.ContainsKey(_snapshot.Id))
            document.AddLineTypeCore(_snapshot);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
