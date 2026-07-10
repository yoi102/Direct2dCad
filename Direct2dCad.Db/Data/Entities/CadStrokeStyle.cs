namespace Direct2dCad.Db.Data.Entities;

public enum CadStrokeCap
{
    Flat = 0,
    Square = 1,
    Round = 2,
    Triangle = 3
}

public enum CadStrokeDashStyle
{
    Solid = 0,
    Dash = 1,
    Dot = 2,
    DashDot = 3,
    DashDotDot = 4
}

public enum CadStrokeLineJoin
{
    Miter = 0,
    Bevel = 1,
    Round = 2,
    MiterOrBevel = 3
}

public readonly record struct CadStrokeStyle(
    CadStrokeCap StartCap,
    CadStrokeCap EndCap,
    CadStrokeCap DashCap,
    CadStrokeDashStyle DashStyle,
    CadStrokeLineJoin LineJoin)
{
    public static CadStrokeStyle Default { get; } = new(
        CadStrokeCap.Flat,
        CadStrokeCap.Flat,
        CadStrokeCap.Flat,
        CadStrokeDashStyle.Solid,
        CadStrokeLineJoin.Miter);
}
