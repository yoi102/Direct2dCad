namespace Direct2dCad.Db.Cad;

public readonly record struct CadColor(byte A, byte R, byte G, byte B)
{
    public static readonly CadColor Transparent = FromArgb(0, 0, 0, 0);
    public static readonly CadColor Black = FromRgb(0, 0, 0);
    public static readonly CadColor White = FromRgb(255, 255, 255);
    public static readonly CadColor Red = FromRgb(255, 0, 0);
    public static readonly CadColor Green = FromRgb(0, 255, 0);
    public static readonly CadColor Blue = FromRgb(0, 0, 255);

    public bool IsTransparent => A == 0;

    public static CadColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
    public static CadColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
}
