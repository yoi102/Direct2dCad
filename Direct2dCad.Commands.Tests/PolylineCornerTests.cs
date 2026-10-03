using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class PolylineCornerTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public void LocalCornerKeepsTheRemainingPathAndUndoRestoresIt(bool fillet)
    {
        var document=CadDocument.Create("path corner");
        var path=document.AddPolyline([new(0,0),new(40,0),new(40,40),new(80,40)]);
        var original=path.Points.ToArray();
        var plan=CadCurveEditing.Corner(path,path,new(20,0),new(40,20),3,5,fillet);
        Assert.Equal(original,path.Points);
        var result=Assert.Single(Assert.Single(plan.Replacements).Shapes);
        Assert.Equal(4,result.Segments.Count);
        Assert.True(result.Segments[0].End.NearEquals(new(37,0)));
        Assert.True(result.Segments[1].End.NearEquals(new(40,fillet ? 3 : 5)));
        Assert.Equal(new CadPointD(80,40),result.Segments[^1].End);
        if(fillet)
        {
            Assert.Equal(3,result.Segments[1].Radius);
            Assert.True(result.Segments[1].Center.NearEquals(new(37,3)));
        }
        var command=new EditCurvesCommand("corner",plan); command.Execute(document);
        var entity=Assert.Single(document.Entities.Values,e=>!e.IsErased);
        Assert.Equal(path.LayerId,entity.LayerId); Assert.Equal(path.StrokeStyle,entity.StrokeStyle);
        if(fillet) Assert.IsType<CadCompositePath>(entity); else Assert.Same(path,entity);
        command.Undo(document); Assert.Equal(original,path.Points); Assert.False(path.IsErased);
        command.Execute(document); Assert.Single(document.Entities.Values,e=>!e.IsErased);
    }

    [Fact]
    public void ReversePicksKeepChamferDistancesOnThePickedSegments()
    {
        var document=CadDocument.Create("reverse"); var path=document.AddPolyline([new(0,0),new(40,0),new(40,40)]);
        var plan=CadCurveEditing.Corner(path,path,new(40,20),new(20,0),3,5,false);
        var connector=Assert.Single(plan.Replacements).Shapes[0].Segments[1];
        Assert.True(connector.Start.NearEquals(new(35,0))); Assert.True(connector.End.NearEquals(new(40,3)));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void ClosedPathCanModifyTheCornerAtItsSeam(bool fillet)
    {
        var document=CadDocument.Create("seam"); var path=document.AddPolyline([new(0,0),new(40,0),new(40,40),new(0,40)],true);
        var plan=CadCurveEditing.Corner(path,path,new(0,20),new(20,0),3,5,fillet);
        var result=Assert.Single(plan.Replacements).Shapes[0];
        Assert.True(result.Closed); Assert.Equal(5,result.Segments.Count);
        Assert.True(CadGeometryTolerance.Coincident(result.Segments[^1].End,result.Segments[0].Start));
        new EditCurvesCommand("seam",plan).Execute(document);
        Assert.True(Assert.IsAssignableFrom<Curve>(Assert.Single(document.Entities.Values,e=>!e.IsErased)).IsClosed);
    }

    [Theory] [InlineData(false,true)] [InlineData(false,false)] [InlineData(true,true)] [InlineData(true,false)]
    public void WholePathPreservesOpenOrClosedTopology(bool closed,bool fillet)
    {
        var document=CadDocument.Create("whole"); var path=document.AddPolyline([new(0,0),new(40,0),new(40,40),new(0,40)],closed);
        var plan=CadCurveEditing.AllCorners(path,3,5,fillet);
        var result=Assert.Single(plan.Replacements).Shapes[0];
        Assert.Equal(closed,result.Closed);
        Assert.Equal(closed ? 8 : 5,result.Segments.Count);
        Assert.Equal(fillet ? closed ? 4 : 2 : 0,result.Segments.Count(s=>!s.IsLine));
        var command=new EditCurvesCommand("whole",plan); command.Execute(document);
        Assert.Single(document.Entities.Values,e=>!e.IsErased);
        command.Undo(document); Assert.False(path.IsErased); Assert.Equal(4,path.Points.Count);
    }

    [Fact]
    public void WholePathRejectsOverlappingCornersWithoutChangingTheSource()
    {
        var document=CadDocument.Create("overlap"); var path=document.AddPolyline([new(0,0),new(40,0),new(40,40),new(0,40)],true);
        Assert.Throws<InvalidOperationException>(()=>CadCurveEditing.AllCorners(path,21,21,true));
        Assert.Throws<InvalidOperationException>(()=>CadCurveEditing.Corner(path,path,new(20,0),new(20,40),3,3,true));
        Assert.Single(document.Entities); Assert.False(path.IsErased); Assert.Equal(4,path.Points.Count);
    }

    [Fact]
    public void DifferentPathEndpointRetainsUnselectedSegments()
    {
        var document=CadDocument.Create("endpoint");
        var path=document.AddPolyline([new(20,0),new(10,0),new(0,0)]); var line=document.AddLine(new(0,0),new(0,20));
        var plan=CadCurveEditing.Corner(path,line,new(5,0),new(0,10),2,3,false);
        var result=plan.Replacements.Single(r=>r.SourceId==path.Id).Shapes[0];
        Assert.Equal(new CadPointD(20,0),result.Segments[0].Start);
        Assert.Equal(new CadPointD(10,0),result.Segments[0].End);
        Assert.True(result.Segments[^1].End.NearEquals(new(2,0)));
    }

    [Theory] [InlineData(45,135)] [InlineData(0,90)] [InlineData(225,315)]
    public void BrokenCircleIsOneAnalyticArcAcrossItsParameterSeam(double firstAngle,double secondAngle)
    {
        var document=CadDocument.Create("circle break"); var circle=document.AddCircle(new(12,-7),10);
        var first=CadPlanarPrimitive.Point(circle.Center,circle.Radius,firstAngle*Math.PI/180);
        var second=CadPlanarPrimitive.Point(circle.Center,circle.Radius,secondAngle*Math.PI/180);
        var command=new EditCurvesCommand("break",CadCurveEditing.Break(circle,first,second)); command.Execute(document);
        var arc=Assert.IsType<CadArc>(document.GetEntity(Assert.Single(command.ResultEntityIds)));
        Assert.Equal(circle.Center,arc.Center); Assert.Equal(10,arc.Radius); Assert.Equal(1.5*Math.PI,arc.SweepAngleRadians,8);
        Assert.True(arc.GetPointAtAngle(arc.StartAngleRadians).NearEquals(second));
        Assert.True(arc.GetPointAtAngle(arc.EndAngleRadians).NearEquals(first));
        command.Undo(document); Assert.False(circle.IsErased); Assert.True(arc.IsErased);
        command.Execute(document); Assert.Same(arc,Assert.Single(document.Entities.Values,e=>!e.IsErased));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void MixedLineArcPathKeepsExactArcSegmentsAndContinuity(bool fillet)
    {
        var document=CadDocument.Create("mixed");
        var path=document.AddCompositePath(new(-20,0),[new CadCompositeLineSegment(default),new CadCompositeArcSegment(new(-5,0),Math.PI/2)]);
        var plan=CadCurveEditing.Corner(path,path,new(-15,0),new(-1,3),2,3,fillet);
        var result=Assert.Single(plan.Replacements).Shapes[0]; Assert.Equal(3,result.Segments.Count);
        Assert.Equal(5,result.Segments[^1].Radius); Assert.True(result.Segments[^1].End.NearEquals(new(-5,5)));
        var command=new EditCurvesCommand("mixed",plan); command.Execute(document);
        Assert.Equal(path.Id,Assert.Single(command.ResultEntityIds)); Assert.Equal(3,path.Segments.Count);
        command.Undo(document); Assert.Equal(2,path.Segments.Count);
    }

    [Fact]
    public void DifferentOpenPathsCanExtendTheirTerminalLinesToACorner()
    {
        var document=CadDocument.Create("gap");
        var path=document.AddPolyline([new(30,0),new(20,0),new(10,0)]); var line=document.AddLine(new(0,10),new(0,30));
        var plan=CadCurveEditing.Corner(path,line,new(15,0),new(0,20),2,2,true);
        var result=plan.Replacements.Single(r=>r.SourceId==path.Id).Shapes[0];
        Assert.True(result.Segments[^1].End.NearEquals(new(2,0)));
        new EditCurvesCommand("gap",plan).Execute(document); Assert.True(path.Points[^1].NearEquals(new(2,0)));
    }
}
