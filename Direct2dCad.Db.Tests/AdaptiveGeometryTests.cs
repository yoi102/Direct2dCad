using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class AdaptiveGeometryTests
{
    [Fact]public void EllipseFlatteningRespectsWorldErrorAfterRotation()
    {
        var d=CadDocument.Create("adaptive");var ellipse=d.AddEllipse(new(4,5),100,3);ellipse.SetRotation(.6);
        var tess=CadCurveTessellation.Create(ellipse,.001);Assert.False(tess.ReachedBudget);Assert.True(tess.ErrorBound<=.001);
        for(var i=0;i<1000;i++)
        {
            var p=ellipse.GetPointAtAngle(2*Math.PI*i/1000);var distance=tess.Points.Zip(tess.Points.Skip(1),(a,b)=>
            {var u=b-a;var t=u.LengthSquared==0 ? 0 : Math.Clamp((p-a).Dot(u)/u.LengthSquared,0,1);return p.DistanceTo(a+u*t);}).Min();
            Assert.True(distance<=.001001,$"Distance {distance}");
        }
    }
    [Fact]public void AnalyticIntersectionsDeduplicateTangenciesAndHandleTinyGeometry()
    {
        var circle=CadPlanarPrimitive.Arc(default,1e-5,0,Math.PI*2);var line=CadPlanarPrimitive.Line(new(-1e-4,1e-5),new(1e-4,1e-5));
        var p=Assert.Single(CadPlanarGeometry.Intersections(line,circle));Assert.Equal(1e-5,p.Y,12);
        Assert.Empty(CadPlanarGeometry.Intersections(CadPlanarPrimitive.Line(new(-1e-4,2e-5),new(1e-4,2e-5)),circle));
        Assert.Single(CadPlanarGeometry.Intersections(CadPlanarPrimitive.Line(new(-1e-6,0),new(1e-6,0)),CadPlanarPrimitive.Line(new(0,-1e-6),new(0,1e-6))));
    }
    [Fact]public void AdaptiveCalculationsRespectCancellation()
    {
        var d=CadDocument.Create("cancel");var spline=d.AddSpline([new(0,0),new(10,20),new(30,0)]);using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>CadCurveMeasurements.Measure(spline,token:c.Token));Assert.ThrowsAny<OperationCanceledException>(()=>CadCurveTessellation.Create(spline,token:c.Token));
    }
}
