using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Db.Tests;

public sealed class CadUnitConversionTests
{
    [Theory]
    [InlineData(CadUnit.Unitless, 25.4)]
    [InlineData(CadUnit.Millimeter, 25.4)]
    [InlineData(CadUnit.Centimeter, 2.54)]
    [InlineData(CadUnit.Meter, 0.0254)]
    [InlineData(CadUnit.Inch, 1.0)]
    [InlineData(CadUnit.Foot, 1.0 / 12.0)]
    [InlineData(CadUnit.Mil, 1000.0)]
    public void FromMillimeters_UsesExpectedDisplayScale(CadUnit unit, double expected)
    {
        Assert.Equal(expected, CadUnitConversion.FromMillimeters(25.4, unit), precision: 10);
    }

    [Theory]
    [InlineData(CadUnit.Unitless, 25.4)]
    [InlineData(CadUnit.Millimeter, 25.4)]
    [InlineData(CadUnit.Centimeter, 25.4)]
    [InlineData(CadUnit.Meter, 25.4)]
    [InlineData(CadUnit.Inch, 25.4)]
    [InlineData(CadUnit.Foot, 25.4)]
    [InlineData(CadUnit.Mil, 25.4)]
    public void ToMillimeters_RoundTripsDisplayValue(CadUnit unit, double millimeters)
    {
        var displayValue = CadUnitConversion.FromMillimeters(millimeters, unit);

        Assert.Equal(
            millimeters,
            CadUnitConversion.ToMillimeters(displayValue, unit),
            precision: 10);
    }

    [Theory]
    [InlineData(CadUnit.Unitless, "")]
    [InlineData(CadUnit.Millimeter, "mm")]
    [InlineData(CadUnit.Centimeter, "cm")]
    [InlineData(CadUnit.Meter, "m")]
    [InlineData(CadUnit.Inch, "in")]
    [InlineData(CadUnit.Foot, "ft")]
    [InlineData(CadUnit.Mil, "mil")]
    public void GetSymbol_ReturnsExpectedSymbol(CadUnit unit, string expected)
    {
        Assert.Equal(expected, CadUnitConversion.GetSymbol(unit));
    }
}
