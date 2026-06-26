using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public readonly record struct CadOriginSettingsSnapshot(
    CadPointD Position,
    CadOriginDisplayType DisplayType,
    CadOriginMarkerType MarkerType,
    CadOriginLinePattern LinePattern,
    CadColor Color,
    double Size,
    double StrokeWidth)
{
    public static CadOriginSettingsSnapshot From(CadOriginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new CadOriginSettingsSnapshot(
            settings.Position,
            settings.DisplayType,
            settings.MarkerType,
            settings.LinePattern,
            settings.Color,
            settings.Size,
            settings.StrokeWidth);
    }

    public void ApplyTo(CadOriginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Position = Position;
        settings.DisplayType = DisplayType;
        settings.MarkerType = MarkerType;
        settings.LinePattern = LinePattern;
        settings.Color = Color;
        settings.Size = Size;
        settings.StrokeWidth = StrokeWidth;
    }
}

public sealed class SetOriginSettingsCommand : ICadCommand
{
    private readonly CadOriginSettingsSnapshot _target;
    private CadOriginSettingsSnapshot? _previous;

    public string Name => "Set Origin Settings";

    public SetOriginSettingsCommand(CadOriginSettingsSnapshot target)
    {
        if (!IsPositiveFinite(target.Size))
            throw new ArgumentOutOfRangeException(nameof(target), "Origin size must be greater than zero.");

        if (!IsPositiveFinite(target.StrokeWidth))
            throw new ArgumentOutOfRangeException(nameof(target), "Origin stroke width must be greater than zero.");

        _target = target;
    }

    public SetOriginSettingsCommand(CadOriginSettings settings)
        : this(CadOriginSettingsSnapshot.From(settings))
    {
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _previous = CadOriginSettingsSnapshot.From(document.ViewSettings.Origin);
        _target.ApplyTo(document.ViewSettings.Origin);
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previous is null)
            return CadDocumentChangeSet.Empty;

        _previous.Value.ApplyTo(document.ViewSettings.Origin);
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
