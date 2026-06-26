using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Cad.Settings;

public enum CadOriginDisplayType
{
    None,
    Axes,
    Marker,
    AxesAndMarker
}

public enum CadOriginMarkerType
{
    Cross,
    X,
    Circle,
    Square
}

public enum CadOriginLinePattern
{
    Solid,
    Dash,
    Dot,
    DashDot
}

public sealed class CadOriginSettings
{
    public CadPointD Position { get; set; } = CadPointD.Origin;
    public CadOriginDisplayType DisplayType { get; set; } = CadOriginDisplayType.AxesAndMarker;
    public CadOriginMarkerType MarkerType { get; set; } = CadOriginMarkerType.Circle;
    public CadOriginLinePattern LinePattern { get; set; } = CadOriginLinePattern.Solid;
    public CadColor Color { get; set; } = CadColor.FromArgb(255, 80, 190, 255);
    public double Size { get; set; } = 18.0;
    public double StrokeWidth { get; set; } = 0.62;
}
