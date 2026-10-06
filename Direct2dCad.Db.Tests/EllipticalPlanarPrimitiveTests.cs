using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class EllipticalPlanarPrimitiveTests
{
    [Theory]
    [InlineData(.4, 4.8)]
    [InlineData(5.8, -4.8)]
    public void ParameterSliceReverseAndTangentPreserveTheEllipse(double start, double sweep)
    {
        var p = CadPlanarPrimitive.EllipseArc(new(17, -13), 19, 4, .63, start, sweep);
        foreach (var t in new[] { .01, .2, .5, .87, .99 })
        {
            Assert.Equal(t, p.Parameter(p.At(t)), 10);
            var delta = (p.At(t + 1e-6) - p.At(t - 1e-6)) / 2e-6;
            Assert.True(delta.NearEquals(p.TangentAt(t), 1e-7));
        }
        var slice = p.Slice(.2, .87);
        Assert.True(slice.IsEllipse);
        Assert.Equal(p.RadiusY, slice.RadiusY);
        Assert.Equal(p.Rotation, slice.Rotation);
        Near(p.At(.2), slice.Start); Near(p.At(.87), slice.End);
        Near(p.At(.535), slice.At(.5));
        var reverse = p.Reversed();
        foreach (var t in new[] { 0d, .2, .75, 1d }) Near(p.At(1 - t), reverse.At(t));
        Assert.Equal(-sweep, reverse.Sweep);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(.73)]
    [InlineData(-1.2)]
    public void RotatedExtremaAndBoundsMatchAnalyticFullEllipse(double rotation)
    {
        var p = CadPlanarPrimitive.EllipseArc(new(9, -5), 13, 4, rotation, .23, Math.Tau);
        var extentsX = Math.Sqrt(Math.Pow(13 * Math.Cos(rotation), 2) + Math.Pow(4 * Math.Sin(rotation), 2));
        var extentsY = Math.Sqrt(Math.Pow(13 * Math.Sin(rotation), 2) + Math.Pow(4 * Math.Cos(rotation), 2));
        Assert.Equal(9 - extentsX, p.Bounds.MinX, 10); Assert.Equal(9 + extentsX, p.Bounds.MaxX, 10);
        Assert.Equal(-5 - extentsY, p.Bounds.MinY, 10); Assert.Equal(-5 + extentsY, p.Bounds.MaxY, 10);
        Assert.Equal(4, p.ExtremaParameters().Count());
        foreach (var t in p.ExtremaParameters())
        {
            var tangent = p.TangentAt(t);
            Assert.InRange(Math.Min(Math.Abs(tangent.X), Math.Abs(tangent.Y)), 0, 1e-10);
        }
    }

    [Theory]
    [InlineData(0d, Math.Tau, 20d, 8d)]
    [InlineData(.2, 1.7, -20d, -8d)]
    [InlineData(5.8, -4.8, 1d, 2d)]
    [InlineData(0d, Math.Tau, 0d, 0d)]
    public void NearestPointIsOnTheTrueEllipseAndMinimizesDistance(double start, double sweep, double x, double y)
    {
        var transform = CadMatrixD.CreateRotation(.61) * CadMatrixD.CreateTranslation(12, -7);
        var p = CadPlanarPrimitive.EllipseArc(new(12, -7), 10, 3, .61, start, sweep);
        var target = transform.TransformPoint(new(x, y));
        var nearest = p.NearestPoint(target);
        var parameter = p.Parameter(nearest);
        Near(p.At(parameter), nearest);
        Assert.InRange(parameter, 0, 1 + 1e-10);
        var sampledMinimum = Enumerable.Range(0, 20001).Min(i => p.At(i / 20000d).DistanceTo(target));
        Assert.True(nearest.DistanceTo(target) <= sampledMinimum + 1e-9);
        if (parameter is > 1e-7 and < .9999999)
            Assert.InRange(Math.Abs((nearest - target).Dot(p.TangentAt(parameter).Normalize())), 0, 1e-7);
    }

    [Theory]
    [InlineData(false, 0d)]
    [InlineData(true, 0d)]
    [InlineData(false, 1e9)]
    [InlineData(true, 1e9)]
    public void SimilarityTransformsPreserveEllipsesIncludingReflectionAndLargeCoordinates(bool mirrored, double origin)
    {
        var p = CadPlanarPrimitive.EllipseArc(new(origin + 12, origin - 7), 10, 3, .61, .2, 4.3);
        var contour = new CadRegionContour([p, CadPlanarPrimitive.Line(p.End, p.Start)]);
        var transform = CadMatrixD.CreateTranslation(-origin, -origin) *
            CadMatrixD.CreateScale(mirrored ? -2 : 2, 2) * CadMatrixD.CreateRotation(.37) *
            CadMatrixD.CreateTranslation(origin + 40, origin - 20);
        var changed = contour.Transform(transform.TransformPoint, mirrored);
        var edge = changed.Edges[0];
        Assert.True(edge.IsEllipse);
        foreach (var t in new[] { 0d, .2, .6, 1d }) Near(transform.TransformPoint(p.At(t)), edge.At(t), 2e-6);
        Assert.InRange(Math.Abs(changed.SignedArea / contour.SignedArea - (mirrored ? -4 : 4)), 0, 2e-6);
        Assert.InRange(Math.Abs(changed.Length / contour.Length - 2), 0, 2e-7);
    }

    [Fact]
    public void EllipticalRegionReportsAnalyticAreaAndApproximatePerimeter()
    {
        var edge = CadPlanarPrimitive.EllipseArc(new(12, -7), 5, 3, .4, .2, Math.Tau);
        var contour = new CadRegionContour([edge]);
        var region = CadDocument.Create("ellipse region").AddRegion([contour]);
        var measured = CadCurveMeasurements.Measure(region, 1e-9);
        Assert.Equal(15 * Math.PI, region.Area, 10);
        Assert.Equal(25.526998863398, measured.Length, 9);
        Assert.True(measured.Approximate);
        Assert.InRange(measured.LengthErrorEstimate, 0, 1e-9);
        Assert.False(measured.ReachedBudget);
        var flattened = CadCurveTessellation.Create(region, 1e-4);
        Assert.False(flattened.ReachedBudget);
        Assert.All(flattened.Points, point => Assert.InRange(point.DistanceTo(edge.NearestPoint(point)), 0, 1e-8));
        Assert.Throws<NotSupportedException>(() => contour.Transform(CadMatrixD.CreateScale(2, 3).TransformPoint));
    }

    [Theory]
    [InlineData(1e9, 0d)]
    [InlineData(1e9, .37)]
    [InlineData(1e12, 0d)]
    [InlineData(1e12, .37)]
    public void MovingAnEllipseFromTheOriginToLargeCoordinatesRemainsASimilarity(double destination, double rotation)
    {
        var original = CadPlanarPrimitive.EllipseArc(new(12, -7), 10, 3, .61, .2, Math.Tau);
        var contour = new CadRegionContour([original]);
        var transform = CadMatrixD.CreateRotation(rotation) * CadMatrixD.CreateTranslation(destination, -.7 * destination);
        var changed = contour.Transform(transform.TransformPoint);
        var edge = Assert.Single(changed.Edges);
        Assert.True(edge.IsEllipse);
        Assert.Equal(original.RadiusX, edge.RadiusX, 8);
        Assert.Equal(original.RadiusY, edge.RadiusY, 8);
        Assert.Equal(original.Rotation + rotation, edge.Rotation, 9);
        Assert.Equal(contour.SignedArea, changed.SignedArea, 7);
        // At large offsets the output coordinates themselves are quantized;
        // allow eight double-precision ULPs, not a drawing-unit tolerance.
        var tolerance = Math.Max(1e-9, destination * 8 * 2.2204460492503131e-16);
        foreach (var t in new[] { 0d, .2, .6, 1d }) Near(transform.TransformPoint(original.At(t)), edge.At(t), tolerance);
    }

    [Fact]
    public void LegacySixParameterCircularPrimitiveAndLinesKeepTheirMeaning()
    {
        var p = new CadPlanarPrimitive(new(5, 0), new(-5, 0), default, 5, 0, Math.PI);
        Assert.False(p.IsEllipse); Assert.Equal(5, p.RadiusX); Assert.Equal(5, p.RadiusY); Assert.Equal(0, p.Rotation);
        Assert.Equal(5 * Math.PI, p.Length); Near(new(0, 5), p.NearestPoint(new(0, 9)));
        var line = CadPlanarPrimitive.Line(new(2, 3), new(8, 3));
        Near(new(8, 3), line.NearestPoint(new(20, 5))); Near(new(5, 3), line.NearestPoint(new(5, 8)));
    }

    private static void Near(CadPointD expected, CadPointD actual, double tolerance = 1e-9) =>
        Assert.InRange(expected.DistanceTo(actual), 0, tolerance);
}
