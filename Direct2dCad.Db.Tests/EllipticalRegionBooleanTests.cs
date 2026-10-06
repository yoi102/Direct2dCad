using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class EllipticalRegionBooleanTests
{
    [Theory]
    [InlineData(0d)] [InlineData(.61)] [InlineData(2.7)]
    public void AffineCircleLensHasAnalyticAreaAndPreservesEllipticalArcs(double angle)
    {
        var d = CadDocument.Create("ellipse lens");
        var a = d.AddEllipse(default, 10, 5); a.SetRotation(angle);
        var b = d.AddEllipse(new(10 * Math.Cos(angle), 10 * Math.Sin(angle)), 10, 5); b.SetRotation(angle);
        var region = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
        Assert.Equal((200 * Math.PI / 3 - 50 * Math.Sqrt(3)) / 2, region.Area, 7);
        Assert.All(region.Contours.SelectMany(c => c.Edges), edge => Assert.True(edge.IsEllipse));
        Assert.Single(region.Contours);
    }

    [Fact]
    public void OrthogonalEllipsesHaveFourIntersectionsIncludingHalfAngleChartBoundaries()
    {
        var a = CadPlanarPrimitive.EllipseArc(default, 10, 5, 0, 0, Math.PI * 2);
        var b = CadPlanarPrimitive.EllipseArc(default, 10, 5, Math.PI / 2, 0, Math.PI * 2);
        var intersections = CadPlanarGeometry.Intersections(a, b);
        Assert.Equal(4, intersections.Count);
        foreach (var p in intersections)
        {
            Assert.Equal(20, p.X * p.X, 8); Assert.Equal(20, p.Y * p.Y, 8);
            Assert.InRange(CadRegionGeometry.Distance(a, p), 0, 1e-8);
            Assert.InRange(CadRegionGeometry.Distance(b, p), 0, 1e-8);
        }
    }

    [Theory]
    [InlineData(CadBooleanOperation.Union)]
    [InlineData(CadBooleanOperation.Intersection)]
    [InlineData(CadBooleanOperation.Difference)]
    public void RandomRotatedEllipsePairsMatchIndependentImplicitPredicates(CadBooleanOperation operation)
    {
        var random = new Random(671782);
        for (var sample = 0; sample < 12; sample++)
        {
            var d = CadDocument.Create("implicit membership");
            var a = d.AddEllipse(new(random.NextDouble() * 8, random.NextDouble() * 8), 6 + random.NextDouble() * 8, 2 + random.NextDouble() * 4);
            var b = d.AddEllipse(new(random.NextDouble() * 8, random.NextDouble() * 8), 6 + random.NextDouble() * 8, 2 + random.NextDouble() * 4);
            a.SetRotation(random.NextDouble() * Math.PI); b.SetRotation(random.NextDouble() * Math.PI);
            var result = CadRegionBoolean.Compute([a, b], operation);
            for (var i = 0; i < 300; i++)
            {
                var point = new CadPointD(random.NextDouble() * 40 - 15, random.NextDouble() * 40 - 15);
                var qa = Implicit(a, point); var qb = Implicit(b, point);
                if (Math.Abs(qa - 1) < 1e-6 || Math.Abs(qb - 1) < 1e-6) continue;
                Assert.Equal(operation switch
                {
                    CadBooleanOperation.Union => qa < 1 || qb < 1,
                    CadBooleanOperation.Intersection => qa < 1 && qb < 1,
                    _ => qa < 1 && qb > 1
                }, CadRegionGeometry.Contains(result, point));
            }
        }
    }

    [Fact]
    public void EllipseCircleAndRectangleProduceReusableRegionWithHole()
    {
        var d = CadDocument.Create("reuse");
        var outer = d.AddEllipse(default, 20, 12); outer.SetRotation(.42);
        var hole = d.AddCircle(default, 3);
        var ring = d.AddRegion(CadRegionBoolean.Compute([outer, hole], CadBooleanOperation.Difference));
        Assert.Equal(Math.PI * (240 - 9), ring.Area, 7);
        Assert.Equal(2, ring.Contours.Count); Assert.False(ring.Contains(default));
        var cutter = d.AddRectangle(CadRectD.FromLTRB(-25, -2, 25, 2));
        var split = d.AddRegion(CadRegionBoolean.Compute([ring, cutter], CadBooleanOperation.Difference));
        Assert.Equal(2, split.Contours.Count);
        Assert.Contains(split.Contours.SelectMany(c => c.Edges), p => p.IsEllipse);
        Assert.Contains(split.Contours.SelectMany(c => c.Edges), p => p.IsLine);
        Assert.False(split.Contains(default));
        var refill = d.AddRegion(CadRegionBoolean.Compute([ring, hole], CadBooleanOperation.Union));
        Assert.Single(refill.Contours); Assert.Equal(Math.PI * 240, refill.Area, 7);
    }

    [Theory]
    [InlineData(0d)] [InlineData(.37)] [InlineData(Math.PI)]
    public void ExternalTangencyAndDisjointEllipsesDoNotCreateSpuriousMaterial(double rotation)
    {
        var d = CadDocument.Create("tangent");
        var a = d.AddEllipse(default, 10, 5); a.SetRotation(rotation);
        var b = d.AddEllipse(new(20 * Math.Cos(rotation), 20 * Math.Sin(rotation)), 10, 5); b.SetRotation(rotation);
        Assert.Single(CadPlanarGeometry.Intersections(CadPlanarCurves.Get(a)[0], CadPlanarCurves.Get(b)[0]));
        Assert.Empty(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
        var union = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Union));
        Assert.Equal(2, union.Contours.Count); Assert.Equal(100 * Math.PI, union.Area, 7);
    }

    [Theory]
    [InlineData(CadBooleanOperation.Union)] [InlineData(CadBooleanOperation.Intersection)] [InlineData(CadBooleanOperation.Difference)]
    public void CoincidentEllipsesWithSwappedAxesAndParameterDirectionAreNoded(CadBooleanOperation operation)
    {
        var d = CadDocument.Create("same support");
        var a = d.AddEllipse(new(4, -5), 10, 5); a.SetRotation(.6);
        var b = d.AddEllipse(new(4, -5), 5, 10); b.SetRotation(.6 + Math.PI / 2);
        var result = CadRegionBoolean.Compute([a, b], operation);
        if (operation == CadBooleanOperation.Difference) Assert.Empty(result);
        else Assert.Equal(Math.PI * 50, d.AddRegion(result).Area, 7);
    }

    [Theory]
    [InlineData(.0001, 0d)] [InlineData(1d, 1000000d)] [InlineData(100000d, 1000000000d)]
    public void EllipseLensIsScaleAndTranslationIndependent(double scale, double offset)
    {
        var d = CadDocument.Create("scale");
        var a = d.AddEllipse(new(offset, offset), 10 * scale, 5 * scale);
        var b = d.AddEllipse(new(offset + 10 * scale, offset), 10 * scale, 5 * scale);
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
        Assert.Equal((200 * Math.PI / 3 - 50 * Math.Sqrt(3)) / 2, r.Area / (scale * scale), 5);
    }

    [Fact]
    public void LineEllipseTangencyAndArcFilteringAreAnalytic()
    {
        var ellipse = CadPlanarPrimitive.EllipseArc(default, 10, 5, 0, 0, Math.PI);
        Assert.Single(CadPlanarGeometry.Intersections(ellipse, CadPlanarPrimitive.Line(new(-20, 5), new(20, 5))));
        Assert.Empty(CadPlanarGeometry.Intersections(ellipse, CadPlanarPrimitive.Line(new(-20, -2), new(20, -2))));
        Assert.Equal(2, CadPlanarGeometry.Intersections(ellipse, CadPlanarPrimitive.Line(new(-20, 2), new(20, 2))).Count);
    }

    [Theory]
    [InlineData(.19)] [InlineData(.7)] [InlineData(1.9)] [InlineData(3.4)]
    public void CircleTangentToRotatedEllipseAtNonCardinalPointHasOneIntersection(double angle)
    {
        var d = CadDocument.Create("non cardinal tangent");
        var ellipse = d.AddEllipse(new(3, -4), 10, 5); ellipse.SetRotation(.37);
        var point = ellipse.GetPointAtAngle(angle);
        var normal = CadMatrixD.CreateRotation(.37).TransformVector(new CadVectorD(Math.Cos(angle) / 10, Math.Sin(angle) / 5)).Normalize();
        var circle = d.AddCircle(point + normal * 3, 3);
        var intersections = CadPlanarGeometry.Intersections(CadPlanarCurves.Get(ellipse)[0], CadPlanarCurves.Get(circle)[0]);
        var actual = Assert.Single(intersections); Assert.InRange(actual.DistanceTo(point), 0, 1e-7);
        Assert.Empty(CadRegionBoolean.Compute([ellipse, circle], CadBooleanOperation.Intersection));
        Assert.Equal(Math.PI * 59, d.AddRegion(CadRegionBoolean.Compute([ellipse, circle], CadBooleanOperation.Union)).Area, 6);
    }

    [Theory]
    [InlineData(1e-5)] [InlineData(1e-7)] [InlineData(1e-10)]
    public void NearTangencyNeverSilentlyDropsAnOverlappingSliver(double overlap)
    {
        var d = CadDocument.Create("near tangent");
        var a = d.AddEllipse(default, 10, 5); var b = d.AddEllipse(new(20 - overlap, 0), 10, 5);
        IReadOnlyList<CadRegionContour> result;
        try { result = CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection); }
        catch (InvalidOperationException) { return; } // Explicit refusal preserves the unmodified operands.
        Assert.NotEmpty(result);
        Assert.True(CadRegionGeometry.Contains(result, new(10 - overlap / 2, 0)));
    }

    [Fact]
    public void InternalTangencyRetainsTheEllipticalHole()
    {
        var d = CadDocument.Create("internal tangent");
        var outer = d.AddEllipse(default, 10, 5); var inner = d.AddEllipse(new(5, 0), 5, 2.5);
        var region = d.AddRegion(CadRegionBoolean.Compute([outer, inner], CadBooleanOperation.Difference));
        Assert.Equal(Math.PI * 37.5, region.Area, 7);
        Assert.True(region.Contains(new(-5, 0))); Assert.False(region.Contains(new(5, 0)));
    }

    [Fact]
    public void NearestPointRemainsUsableNearAnIrrelevantRepeatedStationaryRoot()
    {
        var ellipse = CadPlanarPrimitive.EllipseArc(default, 10, 5, 0, 0, 2 * Math.PI);
        // A point just off the ellipse's evolute: one non-minimal stationary pair is
        // numerically inseparable, while the global nearest point is well separated.
        var point = new CadPointD(.18488520053641336, -13.135148687211046);
        var nearest = ellipse.NearestPoint(point);
        var independentUpperBound = Enumerable.Range(0, 20001).Select(i => ellipse.At(i / 20000d).DistanceTo(point)).Min();
        Assert.InRange(nearest.DistanceTo(point), 8, independentUpperBound + 1e-8);
        Assert.Equal(1, nearest.X * nearest.X / 100 + nearest.Y * nearest.Y / 25, 10);
    }

    [Fact]
    public void IllConditionedEllipsesFailInsteadOfPolygonizingOrMutatingInput()
    {
        var d = CadDocument.Create("condition");
        var ellipse = d.AddEllipse(default, 1e10, 1); var circle = d.AddCircle(default, 10);
        Assert.Throws<InvalidOperationException>(() => CadRegionBoolean.Compute([ellipse, circle], CadBooleanOperation.Union));
        Assert.Equal(1e10, ellipse.RadiusX); Assert.Equal(1, ellipse.RadiusY);
    }

    private static double Implicit(CadEllipse e, CadPointD p)
    {
        var dx = p.X - e.Center.X; var dy = p.Y - e.Center.Y;
        var c = Math.Cos(e.RotationRadians); var s = Math.Sin(e.RotationRadians);
        var x = (dx * c + dy * s) / e.RadiusX; var y = (-dx * s + dy * c) / e.RadiusY;
        return x * x + y * y;
    }
}
