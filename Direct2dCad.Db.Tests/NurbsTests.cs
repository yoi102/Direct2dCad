using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class NurbsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FitMetadataSurvivesFarCoordinateAffineTransforms(bool closed)
    {
        var geometry=CadSplineGeometry.FromFitPoints([new(0,0),new(3,8),new(10,2)],closed);
        var far=geometry.Transform(p=>p+new CadVectorD(100_000_000,100_000_000));
        var rotation=CadMatrixD.CreateRotation(.37,new(100_000_000,100_000_000));
        var rotated=far.Transform(rotation.TransformPoint);
        Assert.Equal(geometry.FitPoints.Count,rotated.FitPoints.Count);
        for(var i=0;i<=20;i++)Assert.InRange(rotation.TransformPoint(far.Curve.EvaluateNormalized(i/20.0)).DistanceTo(rotated.Curve.EvaluateNormalized(i/20.0)),0,1e-7);
    }

    [Fact]
    public void RationalAndPeriodicTessellationBoundContainsActualSamples()
    {
        var curves = new[] { QuarterCircle(),
            CadNurbsCurve.CreateUniform([new(0,0), new(10,0), new(10,10), new(0,10)], 3, [1,2,1,2], true),
            CadNurbsCurve.CreateUniform([new(0,0), new(3,8), new(6,-5), new(12,9), new(20,0)], 4, [1,2,.5,4,1]),
            new CadNurbsCurve(3, [new(0,0), new(3,8), new(6,-5), new(12,9), new(20,0)], [0,0,0,0,.1,1,1,1,1], [1,2,.5,4,1]),
            CadSplineGeometry.FromFitPoints([new(0,0),new(3,8),new(10,2)], true).Curve };
        foreach (var curve in curves)
        {
            var tessellation = curve.Tessellate(1e-3);
            Assert.False(tessellation.ReachedBudget);
            Assert.InRange(tessellation.ErrorBound, 0, 1e-3);
            Assert.InRange(tessellation.Points[0].DistanceTo(curve.EvaluateNormalized(0)), 0, 1e-10);
            Assert.InRange(tessellation.Points[^1].DistanceTo(curve.EvaluateNormalized(1)), 0, 1e-10);
            for (var i = 0; i <= 500; i++)
            {
                var point = curve.EvaluateNormalized(i / 500.0);
                var distance = tessellation.Points.Zip(tessellation.Points.Skip(1)).Min(pair =>
                {
                    var delta = pair.Second - pair.First;
                    var t = delta.LengthSquared == 0 ? 0 : Math.Clamp((point-pair.First).Dot(delta)/delta.LengthSquared,0,1);
                    return point.DistanceTo(pair.First+delta*t);
                });
                Assert.InRange(distance, 0, tessellation.ErrorBound + 1e-10);
            }
        }
    }

    private static CadNurbsCurve QuarterCircle(double weight = .7071067811865476) =>
        new(2, [new(1, 0), new(1, 1), new(0, 1)], [0, 0, 0, 1, 1, 1], [1, weight, 1]);

    [Fact]
    public void RationalQuadraticRepresentsACircleAndMeasuresItsActualNurbsLength()
    {
        var curve = QuarterCircle();
        Assert.True(curve.IsRational);
        for (var i = 0; i <= 100; i++)
        {
            var p = curve.EvaluateNormalized(i / 100.0);
            Assert.Equal(1, p.X * p.X + p.Y * p.Y, 12);
        }
        var spline = CadDocument.Create("NURBS").AddSpline(new CadSplineGeometry(curve));
        Assert.Empty(spline.FitPoints);
        Assert.Equal(Math.PI / 2, spline.Length, 8);
        Assert.InRange(CadCurveMeasurements.Measure(spline).LengthErrorEstimate, 0, 1e-6);
        var polynomial = QuarterCircle(1).Evaluate(.5);
        Assert.True(Math.Abs(polynomial.X * polynomial.X + polynomial.Y * polynomial.Y - 1) > .1);
        foreach (var b in spline.GetBezierSegments())
            for (var i = 0; i <= 10; i++)
            {
                var p = b.Evaluate(i / 10.0);
                Assert.InRange(Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 1), 0, 2e-6);
            }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExistingFitCurveMigratesExactlyToNurbs(bool closed)
    {
        CadPointD[] fit = [new(-20, 3), new(10, 45), new(90, -20), new(125, 15)];
        var original = CadSpline.CreateBezierSegments(fit, closed);
        var geometry = CadSplineGeometry.FromFitPoints(fit, closed);
        for (var segment = 0; segment < original.Count; segment++)
            for (var sample = 0; sample <= 20; sample++)
                Assert.InRange(original[segment].Evaluate(sample / 20.0).DistanceTo(
                    geometry.Curve.EvaluateNormalized((segment + sample / 20.0) / original.Count)), 0, 1e-10);
    }

    [Fact]
    public void PeriodicControlsCloseWithMatchingTangentsAndEditingKeepsTheSeam()
    {
        var curve = CadNurbsCurve.CreateUniform([new(0, 0), new(10, 0), new(10, 10), new(0, 10)], 3, [1, 2, 1, 2], true);
        Assert.True(curve.IsPeriodic);
        Assert.InRange(curve.Evaluate(0).DistanceTo(curve.Evaluate(1)), 0, 1e-12);
        Assert.InRange((curve.Derivative(0) - curve.Derivative(1)).Length, 0, 1e-12);
        var changed = curve.WithControlPoint(0, new(-5, -4), 3);
        Assert.Equal(changed.ControlPoints[0], changed.ControlPoints[4]);
        Assert.Equal(3, changed.Weights[4]);
        Assert.InRange(changed.Evaluate(0).DistanceTo(changed.Evaluate(1)), 0, 1e-12);
    }

    [Fact]
    public void NonUniformKnotsAndTheirNativeDomainAffectEvaluation()
    {
        var curve = new CadNurbsCurve(1, [new(0, 0), new(10, 0), new(10, 20)], [2, 2, 3, 7, 7]);
        Assert.Equal(new CadPointD(5, 0), curve.Evaluate(2.5));
        Assert.Equal(new CadPointD(10, 10), curve.Evaluate(5));
        Assert.Equal(new CadVectorD(0, 5), curve.Derivative(5));
        Assert.Equal(30, CadDocument.Create("NURBS").AddSpline(new CadSplineGeometry(curve)).Length, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Evaluate(1));
    }

    [Fact]
    public void InvalidDefinitionsCannotMutateExistingCurveAndInputArraysAreCopied()
    {
        CadPointD[] points = [new(0, 0), new(10, 10), new(20, 0)];
        double[] knots = [0, 0, 0, 1, 1, 1]; double[] weights = [1, 1, 1];
        var curve = new CadNurbsCurve(2, points, knots, weights);
        points[0] = new(999, 999); weights[0] = 0; knots[0] = 123;
        Assert.Equal(default, curve.Evaluate(0));
        Assert.Throws<ArgumentException>(() => new CadNurbsCurve(2, points, knots, weights));
        Assert.Throws<ArgumentException>(() => curve.WithControlPoint(1, new(0, 0), -1));
        Assert.Equal(new CadPointD(10, 5), curve.Evaluate(.5));
    }
}
