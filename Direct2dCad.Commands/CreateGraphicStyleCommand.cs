using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

public sealed class CreateGraphicStyleCommand : ICadCommand
{
    private readonly string _name;
    private readonly CadColor _color;
    private readonly CadLineWeight _lineWeight;
    private readonly LineTypeId _lineTypeId;
    private CadGraphicStyle? _createdStyle;

    public CreateGraphicStyleCommand(
        string name,
        CadColor color,
        CadLineWeight lineWeight,
        LineTypeId lineTypeId)
    {
        _name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Style name cannot be empty.", nameof(name))
            : name.Trim();
        _color = color;
        _lineWeight = lineWeight;
        _lineTypeId = lineTypeId;
    }

    public string Name => "Create Graphic Style";
    public StyleId? CreatedStyleId => _createdStyle?.Id;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if (_createdStyle is null)
        {
            var id = document.CreateGraphicStyle(_name, _color, _lineWeight, _lineTypeId);
            _createdStyle = (CadGraphicStyle)document.Styles[id];
        }
        else if (!document.TryGetStyle(_createdStyle.Id, out _))
        {
            document.AddStyleCore(_createdStyle);
        }

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_createdStyle is not null &&
            document.Styles.ContainsKey(_createdStyle.Id) &&
            document.GetStyleReferenceCount(_createdStyle.Id) == 0)
        {
            document.RemoveStyleCore(_createdStyle.Id);
        }

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
