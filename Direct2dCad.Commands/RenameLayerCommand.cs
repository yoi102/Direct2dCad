using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class RenameLayerCommand : ICadCommand
{
    private readonly LayerId _layerId;
    private readonly string _name;
    private string? _previousName;

    public string Name => "Rename Layer";

    public RenameLayerCommand(LayerId layerId, string name)
    {
        _layerId = layerId;
        _name = name;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layer = document.GetLayer(_layerId);
        _previousName ??= layer.Name;
        layer.Rename(_name);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousName is null)
            return CadDocumentChangeSet.Empty;

        document.GetLayer(_layerId).Rename(_previousName);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }
}
