using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class CurveEditingTests
{
    [Fact] public void OffsetAndHistoryPreserveAppearanceAndSource()
    {
        var d=CadDocument.Create("edit"); var source=d.AddLine(new(0,0),new(20,0));
        var command=new EditCurvesCommand("Offset",CadCurveEditing.Offset(source,2,new(10,5)));
        command.Execute(d); var copy=Assert.IsType<CadLine>(d.GetEntity(Assert.Single(command.ResultEntityIds)));
        Assert.Equal(new CadPointD(0,2),copy.Start); Assert.Equal(source.StrokeStyle,copy.StrokeStyle); Assert.False(source.IsErased);
        command.Undo(d); Assert.True(copy.IsErased); command.Execute(d); Assert.False(copy.IsErased); Assert.Equal(2,d.Entities.Count);
    }
    [Fact] public void TrimMiddleSplitsAndUndoRestoresOriginal()
    {
        var d=CadDocument.Create("trim"); var line=d.AddLine(new(0,0),new(10,0));
        var left=d.AddLine(new(3,-3),new(3,3));var right=d.AddLine(new(7,-3),new(7,3));
        var command=new EditCurvesCommand("trim",CadCurveEditing.Trim(line,[left,right],new(5,0)));
        command.Execute(d);Assert.True(line.IsErased);Assert.Equal(2,command.ResultEntityIds.Count);
        Assert.Equal(new[]{3.0,3.0},command.ResultEntityIds.Select(id=>((Curve)d.GetEntity(id)).Length));
        command.Undo(d);Assert.False(line.IsErased);command.Execute(d);Assert.True(line.IsErased);
    }
    [Fact] public void ExtendPreservesUniqueIdentityAndIsReversible()
    {
        var d=CadDocument.Create("extend");var line=d.AddLine(new(0,0),new(10,0));var boundary=d.AddCircle(new(20,0),3);
        var command=new EditCurvesCommand("extend",CadCurveEditing.Extend(line,[boundary],new(9,0)));
        command.Execute(d);Assert.Equal(line.Id,Assert.Single(command.ResultEntityIds));Assert.Equal(17,line.End.X,8);
        command.Undo(d);Assert.Equal(10,line.End.X);command.Execute(d);Assert.Equal(17,line.End.X,8);
    }
    [Fact] public void FilletIsTangentAndDoesNotMutateDuringPreview()
    {
        var d=CadDocument.Create("fillet");var a=d.AddLine(new(0,0),new(20,0));var b=d.AddLine(new(0,0),new(0,20));
        var plan=CadCurveEditing.Corner(a,b,new(18,0),new(0,18),2,2,true);
        Assert.Equal(new CadPointD(0,0),a.Start);
        var arc=Assert.Single(plan.Creations).Shape.Segments[0];Assert.Equal(2,arc.Radius);Assert.Equal(2,arc.Center.X,8);Assert.Equal(2,arc.Center.Y,8);
        var command=new EditCurvesCommand("fillet",plan);command.Execute(d);command.Undo(d);Assert.Equal(new CadPointD(0,0),a.Start);
    }
    [Fact] public void JoinMixedSegmentsAndBreakAreExact()
    {
        var d=CadDocument.Create("join");var a=d.AddLine(new(0,0),new(10,0));var b=d.AddArc(new(10,5),5,-Math.PI/2,Math.PI/2);
        var command=new EditCurvesCommand("join",CadCurveEditing.Join([a,b]));command.Execute(d);
        var joined=Assert.IsType<CadCompositePath>(d.GetEntity(Assert.Single(command.ResultEntityIds)));
        Assert.Equal(10+5*Math.PI/2,joined.Length,7);
        var split=new EditCurvesCommand("break",CadCurveEditing.Break(joined,new(5,0)));split.Execute(d);Assert.Equal(2,split.ResultEntityIds.Count);split.Undo(d);Assert.False(joined.IsErased);
    }
    [Fact] public void RectangularAndPolarArrayKeepOneUndoUnit()
    {
        var d=CadDocument.Create("array");var source=d.AddLine(new(1,0),new(2,0));
        var rectangle=new ArrayEntitiesCommand([source.Id],2,3,10,20);rectangle.Execute(d);Assert.Equal(5,rectangle.CreatedEntityIds.Count);rectangle.Undo(d);
        var polar=new ArrayEntitiesCommand([source.Id],default,4,Math.PI*2);polar.Execute(d);Assert.Equal(3,polar.CreatedEntityIds.Count);polar.Undo(d);Assert.Single(d.Entities.Values,e=>!e.IsErased);polar.Execute(d);Assert.Equal(4,d.Entities.Values.Count(e=>!e.IsErased));
    }
    [Fact] public void DegenerateAndUnsupportedOperationsLeaveDocumentUntouched()
    {
        var d=CadDocument.Create("fail");var circle=d.AddCircle(default,1);
        Assert.Throws<InvalidOperationException>(()=>CadCurveEditing.Offset(circle,2,default));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new ArrayEntitiesCommand([circle.Id],1000,1000,1,1));
        Assert.False(circle.IsErased);Assert.Single(d.Entities);
    }
    [Fact]public void ClosedTrimIncludesTheParameterSeamAndUndoRestoresTheCircle()
    {
        var d=CadDocument.Create("closed trim");var circle=d.AddCircle(default,10);var boundary=d.AddLine(new(-20,0),new(20,0));
        var command=new EditCurvesCommand("trim",CadCurveEditing.Trim(circle,[boundary],new(0,10)));command.Execute(d);
        var arc=Assert.IsType<CadArc>(d.GetEntity(Assert.Single(command.ResultEntityIds)));Assert.Equal(Math.PI*10,arc.Length,7);Assert.True(arc.GetPointAtAngle(3*Math.PI/2).Y<0);
        command.Undo(d);Assert.False(circle.IsErased);Assert.True(arc.IsErased);
    }
    [Fact]public void ClosedOffsetRejectsInvertedEdgesAndDoesNotDuplicateClosingVertex()
    {
        var d=CadDocument.Create("closed offset");var rectangle=d.AddPolyline([new(0,0),new(10,0),new(10,10),new(0,10)],true);
        Assert.Throws<InvalidOperationException>(()=>CadCurveEditing.Offset(rectangle,6,new(5,5)));
        var command=new EditCurvesCommand("offset",CadCurveEditing.Offset(rectangle,1,new(5,5)));command.Execute(d);
        var copy=Assert.IsType<CadPolyline>(d.GetEntity(Assert.Single(command.ResultEntityIds)));Assert.Equal(4,copy.Points.Count);Assert.Equal(32,copy.Length,7);
        Assert.Equal(4,CadPlanarCurves.Get(copy).Count);
    }
    [Fact]public void FilletBetweenLineAndArcUsesExactTangenciesAndUndo()
    {
        var d=CadDocument.Create("curved fillet");var line=d.AddLine(new(-20,0),new(20,0));var arc=d.AddArc(new(0,5),5,Math.PI,Math.PI);
        var plan=CadCurveEditing.Corner(line,arc,new(15,0),new(5,5),2,2,true);
        var connector=Assert.Single(plan.Creations).Shape.Segments[0];Assert.Equal(2,connector.Radius);
        Assert.Equal(0,(connector.Start-connector.Center).Dot(new CadVectorD(1,0)),7);
        Assert.Equal(0,(connector.End-connector.Center).Cross(connector.End-arc.Center),7);
        var command=new EditCurvesCommand("fillet",plan);command.Execute(d);command.Undo(d);Assert.Equal(new CadPointD(-20,0),line.Start);Assert.Equal(Math.PI,arc.SweepAngleRadians);
    }
    [Fact]public void ChamferBetweenLineAndArcMeasuresDistanceAlongBothCurves()
    {
        var d=CadDocument.Create("curved chamfer");var line=d.AddLine(new(-20,0),new(20,0));var arc=d.AddArc(default,10,0,Math.PI/2);
        var plan=CadCurveEditing.Corner(line,arc,new(-15,0),new(0,10),2,3,false);
        var connector=Assert.Single(plan.Creations).Shape.Segments[0];Assert.Equal(8,connector.Start.X,7);Assert.Equal(.3,Math.Atan2(connector.End.Y,connector.End.X),7);
    }
    [Fact]public void ClosedOffsetsKeepFillAndJoinRejectsDifferentLineWeights()
    {
        var d=CadDocument.Create("appearance");var fill=d.CreateGradientFillStyle("fill",Direct2dCad.Db.Data.Styles.FillStyles.CadGradientKind.Linear,[new(0,Direct2dCad.Db.Cad.CadColor.Blue),new(1,Direct2dCad.Db.Cad.CadColor.Red)]);
        var circle=d.AddCircle(default,10,fillStyleId:fill);var command=new EditCurvesCommand("offset",CadCurveEditing.Offset(circle,2,new(20,0)));command.Execute(d);Assert.Equal(fill,((CadCircle)d.GetEntity(Assert.Single(command.ResultEntityIds))).FillStyleId);
        var a=d.AddLine(default,new(10,0));var b=d.AddLine(new(10,0),new(20,0));b.SetLineWeight(new Direct2dCad.Db.Cad.CadLineWeight(2));Assert.Throws<InvalidOperationException>(()=>CadCurveEditing.Join([a,b]));
    }
    [Fact]public void TrimAndExtendPreviewOnlyTheChangedPortion()
    {
        var d=CadDocument.Create("preview");var line=d.AddLine(default,new(10,0));var left=d.AddLine(new(3,-5),new(3,5));var right=d.AddLine(new(7,-5),new(7,5));
        var removed=Assert.Single(CadCurveEditing.Trim(line,[left,right],new(5,0)).PreviewGeometry!).Segments[0];Assert.Equal(new CadPointD(3,0),removed.Start);Assert.Equal(new CadPointD(7,0),removed.End);
        var boundary=d.AddLine(new(15,-5),new(15,5));var added=Assert.Single(CadCurveEditing.Extend(line,[boundary],new(9,0)).PreviewGeometry!).Segments[0];Assert.Equal(line.End,added.Start);Assert.Equal(new CadPointD(15,0),added.End);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new ArrayEntitiesCommand([line.Id],2,3,double.MaxValue,10));
    }
}
