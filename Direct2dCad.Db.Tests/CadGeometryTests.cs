using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class CadGeometryTests
{
    [Fact]
    public void ComposedMatrix_InverseRoundTripsPoint()
    {
        var matrix =
            CadMatrixD.CreateScale(2.5, -0.75) *
            CadMatrixD.CreateRotation(Math.PI / 6) *
            CadMatrixD.CreateTranslation(17, -9);
        var point = new CadPointD(3.25, -4.5);

        var roundTripped = matrix.Invert().TransformPoint(matrix.TransformPoint(point));

        Assert.True(point.NearEquals(roundTripped, 1e-9));
    }

    [Fact]
    public void RectangleOperations_PreserveExpectedExtents()
    {
        var first = CadRectD.FromLTRB(0, 0, 10, 10);
        var second = CadRectD.FromLTRB(5, -5, 15, 4);

        Assert.True(first.Intersection(second).NearEquals(CadRectD.FromLTRB(5, 0, 10, 4)));
        Assert.True(first.Union(second).NearEquals(CadRectD.FromLTRB(0, -5, 15, 10)));
        Assert.True(first.Inflate(2, 3).NearEquals(CadRectD.FromLTRB(-2, -3, 12, 13)));
    }
}
