using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetOriginPositionCommand : ICadCommand
{
    private readonly CadPointD _position;
    private CadPointD? _previousPosition;

    public string Name => "Set Origin Position";

    public SetOriginPositionCommand(CadPointD position)
    {
        if (!IsFinite(position.X) || !IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position), "Origin position must use finite coordinates.");

        _position = position;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _previousPosition = document.ViewSettings.Origin.Position;
        document.ViewSettings.Origin.Position = _position;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousPosition is null)
            return CadDocumentChangeSet.Empty;

        document.ViewSettings.Origin.Position = _previousPosition.Value;
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
