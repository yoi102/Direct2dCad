namespace Direct2dCad.Db.Cad.Settings;

/// <summary>
/// Converts the application's canonical drawing distance (millimetres) to and
/// from the unit selected for display and numeric input.
/// </summary>
public static class CadUnitConversion
{
    public static double FromMillimeters(double millimeters, CadUnit unit) =>
        unit switch
        {
            CadUnit.Centimeter => millimeters / 10.0,
            CadUnit.Meter => millimeters / 1000.0,
            CadUnit.Inch => millimeters / 25.4,
            CadUnit.Foot => millimeters / 304.8,
            CadUnit.Mil => millimeters / 0.0254,
            _ => millimeters
        };

    public static double ToMillimeters(double value, CadUnit unit) =>
        unit switch
        {
            CadUnit.Centimeter => value * 10.0,
            CadUnit.Meter => value * 1000.0,
            CadUnit.Inch => value * 25.4,
            CadUnit.Foot => value * 304.8,
            CadUnit.Mil => value * 0.0254,
            _ => value
        };

    public static string GetSymbol(CadUnit unit) =>
        unit switch
        {
            CadUnit.Millimeter => "mm",
            CadUnit.Centimeter => "cm",
            CadUnit.Meter => "m",
            CadUnit.Inch => "in",
            CadUnit.Foot => "ft",
            CadUnit.Mil => "mil",
            _ => string.Empty
        };
}
