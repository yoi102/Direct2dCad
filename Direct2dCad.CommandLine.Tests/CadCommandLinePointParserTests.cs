using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.CommandLine.Tests;

public sealed class CadCommandLinePointParserTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData("10,20", true)]
    [InlineData("-1.5,2", true)]
    [InlineData("@5,5", true)]
    [InlineData("@10<90", true)]
    [InlineData("10<45", true)]
    [InlineData("LINE", false)]
    [InlineData("10", false)]
    [InlineData("", false)]
    public void LooksLikePointRecognisesCoordinateSyntax(string value, bool expected)
        => Assert.Equal(expected, CadCommandLinePointParser.LooksLikePoint(value));

    [Fact]
    public void ParsesAbsoluteAndRelativeCartesianPoints()
    {
        Assert.True(CadCommandLinePointParser.TryParse(" 10 , -20.5 ", null, out var absolute, out _));
        Assert.Equal(new CadCommandLinePoint(10, -20.5), absolute);

        Assert.True(CadCommandLinePointParser.TryParse("@1,2", new CadCommandLinePoint(10, 10), out var relative, out _));
        Assert.Equal(new CadCommandLinePoint(11, 12), relative);
    }

    [Fact]
    public void ParsesPolarPointsInDegrees()
    {
        Assert.True(CadCommandLinePointParser.TryParse("@10<90", new CadCommandLinePoint(1, 1), out var point, out _));
        Assert.Equal(1, point.X, Tolerance);
        Assert.Equal(11, point.Y, Tolerance);
    }

    [Fact]
    public void ConvertsDocumentUnitsToMillimeters()
    {
        Assert.True(CadCommandLinePointParser.TryParse("1,2", null, CadUnit.Centimeter, out var point, out _));
        Assert.Equal(new CadCommandLinePoint(
            CadUnitConversion.ToMillimeters(1, CadUnit.Centimeter),
            CadUnitConversion.ToMillimeters(2, CadUnit.Centimeter)), point);
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("a,b")]
    [InlineData("NaN,1")]
    [InlineData("Infinity,1")]
    [InlineData("1e400,1")]
    [InlineData("10<")]
    public void RejectsMalformedOrNonFiniteInput(string value)
    {
        Assert.False(CadCommandLinePointParser.TryParse(value, null, out var point, out var error));
        Assert.Equal(default, point);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void RelativePointWithoutBaseIsRejected()
    {
        Assert.False(CadCommandLinePointParser.TryParse("@1,1", null, out _, out var error));
        Assert.Contains("previous", error);
    }

    [Fact]
    public void UsesInvariantCultureRegardlessOfCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.True(CadCommandLinePointParser.TryParse("1.5,2.25", null, out var point, out _));
            Assert.Equal(new CadCommandLinePoint(1.5, 2.25), point);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
