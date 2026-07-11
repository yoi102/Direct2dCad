using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public readonly record struct CadViewSettingsSnapshot(
    CadColor BackgroundColor,
    CadGridType GridType,
    double GridSpacingX,
    double GridSpacingY,
    int GridSubdivision,
    double GridSnapSpacingX,
    double GridSnapSpacingY,
    double GridMinimumScreenSpacing,
    double GridMinimumWorldSpacing,
    CadColor GridMinorLineColor,
    CadColor GridMajorLineColor,
    double GridMinorLineWidth,
    double GridMajorLineWidth,
    CadColor SnapMarkerColor,
    double SnapMarkerLength,
    double SnapMarkerStrokeWidth,
    CadSnapMarkerType SnapMarkerType,
    CadPointD OriginPosition,
    CadOriginDisplayType OriginDisplayType,
    CadOriginMarkerType OriginMarkerType,
    CadOriginLinePattern OriginLinePattern,
    CadColor OriginColor,
    double OriginSize,
    double OriginStrokeWidth)
{
    public static CadViewSettingsSnapshot From(CadViewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var grid = settings.Grid;
        var origin = settings.Origin;
        return new CadViewSettingsSnapshot(
            settings.BackgroundColor, grid.Type, grid.SpacingX, grid.SpacingY, grid.Subdivision,
            grid.SnapSpacingX, grid.SnapSpacingY, grid.MinimumScreenSpacing, grid.MinimumWorldSpacing,
            grid.MinorLineColor, grid.MajorLineColor, grid.MinorLineWidth, grid.MajorLineWidth,
            grid.SnapMarkerColor, grid.SnapMarkerLength, grid.SnapMarkerStrokeWidth, grid.SnapMarkerType,
            origin.Position, origin.DisplayType, origin.MarkerType, origin.LinePattern,
            origin.Color, origin.Size, origin.StrokeWidth);
    }

    public void ApplyTo(CadViewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.BackgroundColor = BackgroundColor;

        var grid = settings.Grid;
        grid.Type = GridType;
        grid.SpacingX = GridSpacingX;
        grid.SpacingY = GridSpacingY;
        grid.Subdivision = GridSubdivision;
        grid.SnapSpacingX = GridSnapSpacingX;
        grid.SnapSpacingY = GridSnapSpacingY;
        grid.MinimumScreenSpacing = GridMinimumScreenSpacing;
        grid.MinimumWorldSpacing = GridMinimumWorldSpacing;
        grid.MinorLineColor = GridMinorLineColor;
        grid.MajorLineColor = GridMajorLineColor;
        grid.MinorLineWidth = GridMinorLineWidth;
        grid.MajorLineWidth = GridMajorLineWidth;
        grid.SnapMarkerColor = SnapMarkerColor;
        grid.SnapMarkerLength = SnapMarkerLength;
        grid.SnapMarkerStrokeWidth = SnapMarkerStrokeWidth;
        grid.SnapMarkerType = SnapMarkerType;

        var origin = settings.Origin;
        origin.Position = OriginPosition;
        origin.DisplayType = OriginDisplayType;
        origin.MarkerType = OriginMarkerType;
        origin.LinePattern = OriginLinePattern;
        origin.Color = OriginColor;
        origin.Size = OriginSize;
        origin.StrokeWidth = OriginStrokeWidth;
    }
}

public sealed class SetViewSettingsCommand : ICadCommand
{
    private readonly CadViewSettingsSnapshot _target;
    private CadViewSettingsSnapshot? _previous;

    public SetViewSettingsCommand(CadViewSettings settings) : this(CadViewSettingsSnapshot.From(settings)) { }

    public SetViewSettingsCommand(CadViewSettingsSnapshot target)
    {
        Validate(target);
        _target = target;
    }

    public string Name => "Set Document View Settings";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previous = CadViewSettingsSnapshot.From(document.ViewSettings);
        _target.ApplyTo(document.ViewSettings);
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previous is null)
            return CadDocumentChangeSet.Empty;

        _previous.Value.ApplyTo(document.ViewSettings);
        return CadDocumentChangeSet.Empty.WithViewSettingsChanged();
    }

    private static void Validate(CadViewSettingsSnapshot value)
    {
        if (!IsPositiveFinite(value.GridSpacingX) || !IsPositiveFinite(value.GridSpacingY) ||
            value.GridSubdivision < 1 ||
            !IsNonNegativeFinite(value.GridSnapSpacingX) || !IsNonNegativeFinite(value.GridSnapSpacingY) ||
            !IsPositiveFinite(value.GridMinimumScreenSpacing) || !IsPositiveFinite(value.GridMinimumWorldSpacing) ||
            !IsPositiveFinite(value.GridMinorLineWidth) || !IsPositiveFinite(value.GridMajorLineWidth) ||
            !IsPositiveFinite(value.SnapMarkerLength) || !IsPositiveFinite(value.SnapMarkerStrokeWidth) ||
            !IsFinite(value.OriginPosition.X) || !IsFinite(value.OriginPosition.Y) ||
            !IsPositiveFinite(value.OriginSize) || !IsPositiveFinite(value.OriginStrokeWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Document view settings contain invalid numeric values.");
        }
    }

    private static bool IsPositiveFinite(double value) => value > 0 && IsFinite(value);
    private static bool IsNonNegativeFinite(double value) => value >= 0 && IsFinite(value);
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
