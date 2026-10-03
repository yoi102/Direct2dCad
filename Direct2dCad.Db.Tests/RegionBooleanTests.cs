using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class RegionBooleanTests
{
    [Fact]
    public void AnalyticMembershipMatchesIndependentCirclePredicates()
    {
        var random = new Random(38191); var d = CadDocument.Create("membership");
        for (var sample = 0; sample < 12; sample++)
        {
            var a = d.AddCircle(new(random.NextDouble() * 20, random.NextDouble() * 20), 3 + random.NextDouble() * 12);
            var b = d.AddCircle(new(random.NextDouble() * 20, random.NextDouble() * 20), 3 + random.NextDouble() * 12);
            foreach (var op in Enum.GetValues<CadBooleanOperation>())
            {
                var contours = CadRegionBoolean.Compute([a, b], op);
                for (var i = 0; i < 400; i++)
                {
                    var p = new CadPointD(random.NextDouble() * 50 - 15, random.NextDouble() * 50 - 15);
                    if (Math.Abs(p.DistanceTo(a.Center) - a.Radius) < 1e-5 || Math.Abs(p.DistanceTo(b.Center) - b.Radius) < 1e-5) continue;
                    var inA = p.DistanceTo(a.Center) < a.Radius; var inB = p.DistanceTo(b.Center) < b.Radius;
                    var expected = op switch { CadBooleanOperation.Union => inA || inB, CadBooleanOperation.Intersection => inA && inB, _ => inA && !inB };
                    Assert.Equal(expected, CadRegionGeometry.Contains(contours, p));
                }
            }
        }
    }
    [Fact]
    public void SplitSubjectAndNestedIslandKeepEveryContour()
    {
        var d = CadDocument.Create("islands"); var a = d.AddRectangle(CadRectD.FromLTRB(0, 0, 20, 10)); var b = d.AddRectangle(CadRectD.FromLTRB(8, -5, 12, 15));
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference));
        Assert.Equal(2, r.Contours.Count); Assert.Equal(160, r.Area, 7); Assert.False(r.Contains(new(10, 5)));
        var outer = d.AddCircle(default, 20); var hole = d.AddCircle(default, 10); var island = d.AddCircle(default, 3);
        var ring = d.AddRegion(CadRegionBoolean.Compute([outer, hole], CadBooleanOperation.Difference));
        var nested = d.AddRegion(CadRegionBoolean.Compute([ring, island], CadBooleanOperation.Union));
        Assert.Equal(3, nested.Contours.Count); Assert.Equal(Math.PI * 309, nested.Area, 7);
        Assert.True(nested.Contains(default)); Assert.False(nested.Contains(new(5, 0))); Assert.True(nested.Contains(new(15, 0)));
    }
    [Theory][InlineData(.00001)][InlineData(1)][InlineData(100000)]
    public void ScaleIndependentCircularResults(double scale)
    {
        var d = CadDocument.Create("scale"); var a = d.AddCircle(default, 10 * scale); var b = d.AddCircle(new(10 * scale, 0), 10 * scale);
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
        Assert.Equal(200 * Math.PI / 3 - 50 * Math.Sqrt(3), r.Area / (scale * scale), 5);
    }
    [Fact]
    public void ClosedRepeatedEndpointAndRotatedRectangleAreSupported()
    {
        var d = CadDocument.Create("path"); var p = d.AddPolyline([new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)], true);
        var rectangle = d.AddRectangle(CadRectD.FromLTRB(2, 2, 8, 8)); rectangle.SetRotation(.5);
        var r = d.AddRegion(CadRegionBoolean.Compute([p, rectangle], CadBooleanOperation.Difference));
        Assert.Equal(64, r.Area, 7); Assert.Equal(2, r.Contours.Count);
    }
    [Fact]
    public void WorkerRespectsCancellationAndInputBudget()
    {
        var circle = new CadRegionContour([CadPlanarPrimitive.Arc(default, 10, 0, Math.PI * 2)]);
        IReadOnlyList<CadRegionContour>[] operands = [[circle], [circle]];
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => CadRegionBoolean.Compute(operands, CadBooleanOperation.Union, token: cancel.Token));
        var tooMany = Enumerable.Repeat<IReadOnlyList<CadRegionContour>>([circle], 2049).ToArray();
        Assert.Throws<NotSupportedException>(() => CadRegionBoolean.Compute(tooMany, CadBooleanOperation.Union));
    }
    [Theory]
    [InlineData(CadBooleanOperation.Union, 150)]
    [InlineData(CadBooleanOperation.Intersection, 50)]
    [InlineData(CadBooleanOperation.Difference, 50)]
    public void OverlappingRectangles(CadBooleanOperation operation, double area)
    {
        var d = CadDocument.Create("boolean");
        var a = d.AddRectangle(CadRectD.FromLTRB(0, 0, 10, 10)); var b = d.AddRectangle(CadRectD.FromLTRB(5, 0, 15, 10));
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], operation));
        Assert.Equal(area, r.Area, 7); Assert.Single(r.Contours);
        Assert.Equal(operation != CadBooleanOperation.Intersection, r.Contains(new(2, 5)));
    }
    [Fact]
    public void DifferenceCreatesExactCircularHoleAndRegionCanBeReused()
    {
        var d = CadDocument.Create("hole"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3);
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference));
        Assert.Equal(Math.PI * 91, r.Area, 7); Assert.Equal(Math.PI * 26, r.Length, 7);
        Assert.Equal(2, r.Contours.Count); Assert.False(r.Contains(default)); Assert.True(r.Contains(new(5, 0)));
        Assert.All(r.Contours.SelectMany(c => c.Edges), p => Assert.False(p.IsLine));
        var refill = d.AddRegion(CadRegionBoolean.Compute([r, b], CadBooleanOperation.Union));
        Assert.Equal(Math.PI * 100, refill.Area, 7); Assert.Single(refill.Contours);
    }
    [Theory][InlineData(CadBooleanOperation.Union)][InlineData(CadBooleanOperation.Intersection)][InlineData(CadBooleanOperation.Difference)]
    public void IdenticalOperands(CadBooleanOperation operation)
    {
        var d = CadDocument.Create("same"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 10);
        var contours = CadRegionBoolean.Compute([a, b], operation);
        if (operation == CadBooleanOperation.Difference) Assert.Empty(contours);
        else Assert.Equal(Math.PI * 100, d.AddRegion(contours).Area, 7);
    }
    [Fact]
    public void SharedEdgesAndDisconnectedIslands()
    {
        var d = CadDocument.Create("adjacent"); var a = d.AddRectangle(CadRectD.FromLTRB(0, 0, 10, 10));
        var b = d.AddRectangle(CadRectD.FromLTRB(10, 0, 20, 10)); var c = d.AddRectangle(CadRectD.FromLTRB(30, 0, 40, 10));
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b, c], CadBooleanOperation.Union));
        Assert.Equal(300, r.Area, 7); Assert.Equal(2, r.Contours.Count); Assert.Equal(100, r.Length, 7);
        Assert.Empty(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
    }
    [Fact]
    public void CircleLensKeepsAnalyticArcs()
    {
        var d = CadDocument.Create("lens"); var a = d.AddCircle(default, 10); var b = d.AddCircle(new(10, 0), 10);
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
        Assert.Equal(200 * Math.PI / 3 - 50 * Math.Sqrt(3), r.Area, 7);
        Assert.All(r.Contours.SelectMany(c => c.Edges), p => Assert.Equal(10, p.Radius));
        Assert.True(r.Contains(new(5, 0))); Assert.False(r.Contains(new(-5, 0)));
    }
    [Fact]
    public void TangenciesAndPointContactsDoNotProduceZeroAreaContours()
    {
        var d = CadDocument.Create("tangent"); var a = d.AddCircle(default, 10); var b = d.AddCircle(new(20, 0), 10);
        Assert.Empty(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection));
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Union));
        Assert.Equal(Math.PI * 200, r.Area, 7); Assert.Equal(2, r.Contours.Count);
        var x = d.AddRectangle(CadRectD.FromLTRB(0, 0, 10, 10)); var y = d.AddRectangle(CadRectD.FromLTRB(10, 10, 20, 20));
        Assert.Equal(2, CadRegionBoolean.Compute([x, y], CadBooleanOperation.Union).Count);
    }
    [Fact]
    public void RejectOpenUnsupportedAndSelfIntersectingInputs()
    {
        var d = CadDocument.Create("invalid"); var a = d.AddCircle(default, 10); var line = d.AddLine(default, new(10, 0));
        Assert.False(CadRegionBoolean.Supports(line));
        Assert.Throws<NotSupportedException>(() => CadRegionBoolean.Compute([a, line], CadBooleanOperation.Union));
        var bow = d.AddPolyline([new(0, 0), new(10, 10), new(0, 7), new(8, 0)], true);
        Assert.Throws<InvalidOperationException>(() => CadRegionBoolean.Compute([a, bow], CadBooleanOperation.Union));
    }
}
