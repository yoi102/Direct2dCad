using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class SplineLengthTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LengthMatchesFlatteningAndInvalidatesOnGeometryChanges(bool closed)
    {
        var document = CadDocument.Create("Length");
        var spline = document.AddSpline([new(0, 0), new(10, 20), new(30, 0)], closed);
        var first = spline.Length;
        Assert.Equal(ReferenceLength(spline), first, 10);
        Assert.Equal(first, spline.Length);

        spline.ReplaceFitPoints([new(0, 0), new(20, 40), new(60, 0)]);
        Assert.Equal(first * 2, spline.Length, 10);
        spline.SetClosed(!closed);
        Assert.Equal(ReferenceLength(spline), spline.Length, 10);
        spline.ReplaceFitPoints([new(0, 0), new(3, 4)]);
        Assert.False(spline.Closed);
        Assert.Equal(5, spline.Length, 10);
    }

    private static double ReferenceLength(CadSpline spline)
    {
        var points = spline.EnumerateFlattenedPoints(20).ToArray();
        return points.Zip(points.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
    }
}
