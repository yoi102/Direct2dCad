using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Commands;

public readonly record struct CadDocumentSettingsSnapshot(
    CadUnit Unit,
    int LengthPrecision,
    int AnglePrecision)
{
    public static CadDocumentSettingsSnapshot From(CadDocumentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.Unit, settings.LengthPrecision, settings.AnglePrecision);
    }

    public void ApplyTo(CadDocumentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SetUnit(Unit);
        settings.SetLengthPrecision(LengthPrecision);
        settings.SetAnglePrecision(AnglePrecision);
    }
}

public sealed class SetDocumentSettingsCommand : ICadCommand
{
    private readonly CadDocumentSettingsSnapshot _target;
    private CadDocumentSettingsSnapshot? _previous;

    public SetDocumentSettingsCommand(
        CadUnit unit,
        int lengthPrecision,
        int anglePrecision)
        : this(new CadDocumentSettingsSnapshot(unit, lengthPrecision, anglePrecision))
    {
    }

    public SetDocumentSettingsCommand(CadDocumentSettingsSnapshot target)
    {
        Validate(target);
        _target = target;
    }

    public string Name => "Set Document Settings";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previous = CadDocumentSettingsSnapshot.From(document.DocumentSettings);
        _target.ApplyTo(document.DocumentSettings);
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previous is null)
            return CadDocumentChangeSet.Empty;

        _previous.Value.ApplyTo(document.DocumentSettings);
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    private static void Validate(CadDocumentSettingsSnapshot value)
    {
        if (!Enum.IsDefined(value.Unit) ||
            value.LengthPrecision is < 0 or > 12 ||
            value.AnglePrecision is < 0 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Document settings contain an invalid unit or precision.");
        }
    }
}
