using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class CreateLayerCommand : ICadCommand
{
    private readonly string _name;
    private readonly CadColor _color;
    private readonly CadLineWeight _lineWeight;
    private readonly StyleId? _defaultGraphicStyleId;
    private LayerId? _layerId;

    public string Name => "Create Layer";
    public LayerId? LayerId => _layerId;

    public CreateLayerCommand(
        string name,
        CadColor color,
        CadLineWeight lineWeight,
        StyleId? defaultGraphicStyleId = null)
    {
        _name = name;
        _color = color;
        _lineWeight = lineWeight;
        _defaultGraphicStyleId = defaultGraphicStyleId;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_layerId is not null && document.TryGetLayer(_layerId.Value, out _))
            return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();

        _layerId = document.CreateLayer(_name, _color, _lineWeight, _defaultGraphicStyleId);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_layerId is not null)
            document.RemoveLayer(_layerId.Value);

        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }
}
