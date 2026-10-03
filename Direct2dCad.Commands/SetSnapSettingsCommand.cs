using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Commands;

public sealed class SetSnapSettingsCommand(CadSnapSettings settings) : ICadCommand
{
    private CadSnapSettings? _previous;
    public string Name => "Set Snap Settings";
    public CadDocumentChangeSet Execute(CadDocument document)
    {
        settings.Validate();
        _previous = document.ViewSettings.Snap;
        document.ViewSettings.Snap = settings;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is not null) document.ViewSettings.Snap = _previous;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }
}
