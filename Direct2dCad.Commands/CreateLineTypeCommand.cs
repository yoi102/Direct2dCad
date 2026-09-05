using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class CreateLineTypeCommand : ICadCommand
{
    private readonly string _name;
    private readonly double[] _dashPattern;
    private readonly string _description;
    private CadLineTypeDefinition? _created;

    public CreateLineTypeCommand(string name, IEnumerable<double>? dashPattern = null, string description = "")
    {
        _name = name;
        _dashPattern = dashPattern?.ToArray() ?? [];
        _description = description;
    }

    public string Name => "Create Line Type";
    public LineTypeId? CreatedLineTypeId => _created?.Id;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if (_created is null)
        {
            var id = document.CreateLineType(_name, _dashPattern, _description);
            _created = document.GetLineType(id);
        }
        else if (!document.LineTypes.ContainsKey(_created.Id))
        {
            document.AddLineTypeCore(_created);
        }

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_created is not null && document.GetLineTypeReferenceCount(_created.Id) == 0)
            document.RemoveLineType(_created.Id);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
