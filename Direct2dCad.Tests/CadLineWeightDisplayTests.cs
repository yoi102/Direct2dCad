using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadLineWeightDisplayTests
{
    [Fact]
    public void DefaultLineWeightIsQuarterMillimeter()
    {
        Assert.Equal(0.25, CadLineWeight.Default.ExplicitMillimeters);
        Assert.Null(CadLineWeight.ByLayer.ExplicitMillimeters);
    }

    [Fact]
    public void MillimetersConvertToDeviceIndependentPixels()
    {
        Assert.Equal(96.0, CadLineWeightDisplay.ToDips(25.4), 8);
    }
}
