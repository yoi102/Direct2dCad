using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Commands;

public sealed class SetSnapMarkerTypeCommand : ICadCommand
{
    private readonly CadSnapMarkerType _markerType;
    private CadSnapMarkerType? _previousMarkerType;

    public SetSnapMarkerTypeCommand(CadSnapMarkerType markerType)
    {
        _markerType = markerType;
    }

    public string Name => "Set Snap Marker Type";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousMarkerType = document.ViewSettings.Grid.SnapMarkerType;
        document.ViewSettings.Grid.SnapMarkerType = _markerType;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previousMarkerType is null)
            return CadDocumentChangeSet.Empty;

        document.ViewSettings.Grid.SnapMarkerType = _previousMarkerType.Value;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }
}
