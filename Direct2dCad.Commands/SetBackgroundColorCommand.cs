using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class SetBackgroundColorCommand : ICadCommand
{
    private readonly CadColor _color;
    private CadColor? _previousColor;

    public SetBackgroundColorCommand(CadColor color)
    {
        _color = color;
    }

    public string Name => "Set Background Color";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousColor = document.ViewSettings.BackgroundColor;
        document.ViewSettings.BackgroundColor = _color;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previousColor is null)
            return CadDocumentChangeSet.Empty;

        document.ViewSettings.BackgroundColor = _previousColor.Value;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }
}
