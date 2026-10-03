namespace Direct2dCad.Db.Cad.Settings;

[Flags]
public enum CadObjectSnapModes
{
    None = 0, Endpoint = 1, Midpoint = 2, Center = 4, Intersection = 8,
    Perpendicular = 16, Tangent = 32,
    Default = Endpoint | Midpoint | Center | Intersection
}

/// <summary>Pointer constraints only. Explicit coordinates always bypass these settings.</summary>
public sealed record CadSnapSettings
{
    public bool GridEnabled { get; init; }
    public bool ObjectsEnabled { get; init; } = true;
    public bool OrthoEnabled { get; init; }
    public bool PolarEnabled { get; init; }
    public double PolarIncrementDegrees { get; init; } = 45;
    public double ScreenTolerance { get; init; } = 10;
    public CadObjectSnapModes Modes { get; init; } = CadObjectSnapModes.Default;

    public void Validate()
    {
        if (!double.IsFinite(ScreenTolerance) || ScreenTolerance is < 2 or > 30 ||
            !double.IsFinite(PolarIncrementDegrees) || PolarIncrementDegrees is <= 0 or > 180 ||
            (Modes & ~(CadObjectSnapModes.Default | CadObjectSnapModes.Perpendicular | CadObjectSnapModes.Tangent)) != 0 ||
            OrthoEnabled && PolarEnabled)
            throw new ArgumentOutOfRangeException(nameof(CadSnapSettings));
    }
}
