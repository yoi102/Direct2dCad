using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Commands;

public sealed class SetGridTypeCommand : ICadCommand
{
    private readonly CadGridType _gridType;
    private CadGridType? _previousGridType;

    public SetGridTypeCommand(CadGridType gridType)
    {
        _gridType = gridType;
    }

    public string Name => "Set Grid Type";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousGridType = document.ViewSettings.Grid.Type;
        document.ViewSettings.Grid.Type = _gridType;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previousGridType is null)
            return CadDocumentChangeSet.Empty;

        document.ViewSettings.Grid.Type = _previousGridType.Value;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }
}
