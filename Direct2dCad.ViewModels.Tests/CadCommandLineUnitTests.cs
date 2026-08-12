using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadCommandLineUnitTests
{
    [Fact]
    public void PointParser_ConvertsSelectedUnitToCanonicalMillimeters()
    {
        Assert.True(CadCommandLinePointParser.TryParse(
            "1,2",
            relativeBase: null,
            unit: CadUnit.Inch,
            out var point,
            out var error));

        Assert.Null(error);
        Assert.Equal(25.4, point.X, precision: 10);
        Assert.Equal(50.8, point.Y, precision: 10);
    }

    [Fact]
    public void PolarPointParser_ConvertsSelectedUnitToCanonicalMillimeters()
    {
        Assert.True(CadCommandLinePointParser.TryParse(
            "@1<0",
            relativeBase: new CadCommandLinePoint(10, 20),
            unit: CadUnit.Foot,
            out var point,
            out var error));

        Assert.Null(error);
        Assert.Equal(314.8, point.X, precision: 10);
        Assert.Equal(20, point.Y, precision: 10);
    }
}
