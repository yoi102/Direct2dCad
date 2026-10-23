using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class GripDynamicInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CvControlPolygonTracksGripPreviewCommitUndoAndCancel(bool closed)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        CadPointD[] points = [new(0, 0), new(0, 20), new(20, 20), new(20, 0)];
        var id = vm.CadEditor.AddSpline(new CadSplineGeometry(CadNurbsCurve.CreateUniform(points, 3, closed: closed), closed));
        Begin(vm, id, CadHandleType.Vertex, points[1]);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Move(vm, new(5, 25));
        var guide = Assert.Single(vm.CreateTransientItems().OfType<CadTransientPolyline>());
        Assert.Equal(new CadPointD(5, 25), guide.Points[1]);
        Assert.Equal(closed, guide.Closed);
        Assert.Equal(CadTransientLinePattern.Dash, guide.Style.LinePattern);
        Assert.Null(guide.Style.FillColor);
        vm.PointerLeave();
        Assert.Equal(guide.Points, Assert.Single(vm.CreateTransientItems().OfType<CadTransientPolyline>()).Points);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm, new(5, 25));
        var spline = (CadSpline)vm.CadEditor.Document.GetEntity(id);
        Assert.Equal(guide.Points, spline.EditPoints);
        vm.Undo();
        Assert.Equal(points, spline.EditPoints);
        Begin(vm, id, CadHandleType.Vertex, points[1]);
        Move(vm, new(8, 28));
        vm.Escape();
        Assert.False(vm.IsGripEditing);
        Assert.Equal(points, spline.EditPoints);
        Assert.Empty(vm.CreateTransientItems().OfType<CadTransientPolyline>());
    }

    [Fact]
    public void MovingMixedSelectionTranslatesOnlyCvControlPolygonGuides()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        CadPointD[] points = [new(0, 0), new(0, 20), new(20, 20), new(20, 0)];
        var cv = vm.CadEditor.AddSpline(new CadSplineGeometry(CadNurbsCurve.CreateUniform(points, 3)));
        var fit = vm.CadEditor.AddSpline([new(25, 0), new(30, 20), new(40, 0)]);
        vm.SelectEntities([cv, fit]);
        var center = new CadHandleSceneBuilder().BuildSelectionHandles(vm.CadEditor.Document, [cv])
            .OfType<CadGripHandle>().Single(g => g.Type == CadHandleType.Center).Position;
        Click(vm, center);
        Assert.True(vm.IsGripEditing);
        var delta = new CadVectorD(5, 7);
        Move(vm, center + delta);
        var group = Assert.Single(vm.CreateTransientItems().OfType<CadTransientGroup>());
        var guide = Assert.Single(group.Items.OfType<CadTransientPolyline>());
        Assert.Equal(points.Select(p => p + delta), guide.Points.Select(group.Transform.TransformPoint));
        Assert.Equal(2, group.Items.OfType<CadTransientEntityReference>().Count());
    }

    [Theory] [InlineData(CadCanvasToolMode.Line)] [InlineData(CadCanvasToolMode.Rectangle)] [InlineData(CadCanvasToolMode.CircleCenterRadius)]
    public void UnlockedDrawingPreviewSurvivesLeavingAndResumesWhenReturning(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        vm.SetToolMode(mode); Click(vm,default); Move(vm,new(12,8));
        var before=vm.CreateTransientItems().Where(i=>i is not CadTransientCircle c || c.Radius>2).ToArray();
        Assert.NotEmpty(before); var history=vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.PointerLeave(); var after=vm.CreateTransientItems().ToArray();
        Assert.Contains(before[0],after); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Move(vm,new(15,10)); Assert.NotEqual(before[0],vm.CreateTransientItems()[0]);
        vm.Escape(); vm.PointerLeave(); Assert.Empty(vm.CreateTransientItems());
    }

    [Fact]
    public void LineGripOffersLengthAndAngleAndCommitsTypedGeometryOnce()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var line=vm.CadEditor.AddLine(default,new(20,0)); Begin(vm,line,CadHandleType.Vertex,new(20,0));
        Assert.Equal(new[]{"L:","A:"},vm.DynamicInputFields.Select(f=>f.Prefix));
        var history=vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.DynamicInputFields[0].Text="30"; vm.DynamicInputFields[1].Text="90";
        vm.PointerLeave(); Assert.Contains(vm.CreateTransientItems().OfType<CadTransientLine>(),l=>l.End.NearEquals(new(0,30)));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(vm.SubmitDynamicInput());
        Assert.True(((CadLine)vm.CadEditor.Document.GetEntity(line)).End.NearEquals(new(0,30)));
        Assert.False(vm.HasDynamicInput); vm.Undo(); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Redo(); Assert.True(((CadLine)vm.CadEditor.Document.GetEntity(line)).End.NearEquals(new(0,30)));
    }

    [Fact]
    public void CircleGripRetainsPreviewOutsideCanvasAndRejectsBadRadius()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddCircle(default,10); var circle=(CadCircle)vm.CadEditor.Document.GetEntity(id);
        Begin(vm,id,CadHandleType.Radius,new(10,0)); Move(vm,new(14,0));
        var history=vm.CadEditor.CreateDocumentHistorySnapshot(); vm.PointerLeave();
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientCircle>(),c=>Math.Abs(c.Radius-14)<1e-8);
        Assert.Single(vm.DynamicInputFields); Assert.Equal("R:",vm.DynamicInputFields[0].Prefix);
        vm.DynamicInputFields[0].Text="bad"; Assert.False(vm.SubmitDynamicInput());
        Assert.False(new Direct2dCad.CommandLine.CadCommandLineService().Execute("DONE",vm).Success);
        Assert.True(vm.IsGripEditing); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.DynamicInputFields[0].Text="18"; Move(vm,new(25,0)); Click(vm,new(25,0));
        Assert.Equal(18,circle.Radius); Assert.False(vm.IsGripEditing);
        vm.Undo(); Assert.Equal(10,circle.Radius);
    }

    [Fact]
    public void CenterGripUsesCoordinatesAndEscapeLeavesNoChanges()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddCircle(default,10); Begin(vm,id,CadHandleType.Center,default);
        Assert.Equal(new[]{"X:","Y:"},vm.DynamicInputFields.Select(f=>f.Prefix));
        var history=vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.DynamicInputFields[0].Text="12"; vm.DynamicInputFields[1].Text="7"; vm.PointerLeave();
        Assert.NotEmpty(vm.CreateTransientItems()); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Escape(); Assert.False(vm.IsGripEditing); Assert.False(vm.HasDynamicInput); Assert.Empty(vm.DynamicInputFields);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Fact]
    public void RotatedEllipseGripEditsItsLocalAxisInDocumentUnits()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddEllipse(default,10,6); var ellipse=(CadEllipse)vm.CadEditor.Document.GetEntity(id);
        ellipse.SetRotation(Math.PI/4); vm.SetDocumentUnit(CadUnit.Inch);
        Begin(vm,id,CadHandleType.Radius,ellipse.GetPointAtAngle(0));
        Assert.Equal("X:",Assert.Single(vm.DynamicInputFields).Prefix);
        vm.DynamicInputFields[0].Text="1"; Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(25.4,ellipse.RadiusX,8); Assert.Equal(6,ellipse.RadiusY); Assert.Equal(Math.PI/4,ellipse.RotationRadians);
        vm.Undo(); Assert.Equal(10,ellipse.RadiusX);
    }

    [Fact]
    public void RectangleGripUsesWidthAndHeightWithRotationPreserved()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddRectangle(CadRectD.FromLTRB(0,0,20,10)); var rectangle=(CadRectangle)vm.CadEditor.Document.GetEntity(id);
        rectangle.SetRotation(Math.PI/6); Begin(vm,id,CadHandleType.BoundsCorner,rectangle.GeometryTransform.TransformPoint(new(20,10)));
        Assert.Equal(new[]{"W:","H:"},vm.DynamicInputFields.Select(f=>f.Prefix));
        vm.DynamicInputFields[0].Text="30"; vm.DynamicInputFields[1].Text="12";
        Assert.True(vm.SubmitDynamicInput()); Assert.Equal(30,rectangle.FrameBounds.Width,8); Assert.Equal(12,rectangle.FrameBounds.Height,8);
        Assert.Equal(Math.PI/6,rectangle.RotationRadians);
    }

    [Fact]
    public void ArcEndpointOffersRadiusAndAngleAndRemainsAnArc()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddArc(default,10,0,Math.PI/2); Begin(vm,id,CadHandleType.Vertex,new(0,10));
        Assert.Equal(new[]{"R:","A:"},vm.DynamicInputFields.Select(f=>f.Prefix));
        vm.DynamicInputFields[0].Text="15"; vm.DynamicInputFields[1].Text="120";
        Assert.True(vm.SubmitDynamicInput()); var arc=Assert.IsType<CadArc>(vm.CadEditor.Document.GetEntity(id));
        Assert.Equal(15,arc.Radius,8); Assert.Equal(120*Math.PI/180,arc.SweepAngleRadians,8);
    }

    [Theory] [InlineData(CadCanvasToolMode.Fillet)] [InlineData(CadCanvasToolMode.Chamfer)]
    public void SamePathAdjacentSegmentHoverPreviewsAndClickCanConfirm(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddPolyline([new(0,0),new(30,0),new(30,20)]); vm.SetToolMode(mode); vm.EditDistance=3; vm.EditSecondDistance=5;
        Click(vm,new(15,0)); Move(vm,new(30,10));
        Assert.Contains(vm.CreateTransientItems(),i=>i is CadTransientArc || i is CadTransientLine l && l.Start.NearEquals(new(27,0)) && l.End.X==30);
        Click(vm,new(30,10)); Assert.True(vm.CanAdvanceEdit); vm.PointerLeave(); Assert.NotEmpty(vm.CreateTransientItems());
        vm.CompleteCurrentDrawing(); Assert.Empty(vm.StepInputError);
        Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased); vm.Undo(); Assert.False(vm.CadEditor.Document.GetEntity(id).IsErased);
    }

    [Theory] [InlineData(CadCanvasToolMode.Fillet)] [InlineData(CadCanvasToolMode.Chamfer)]
    public void WholePathOptionPreviewsAndConfirmsWithOneSource(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        vm.CadEditor.AddPolyline([new(0,0),new(30,0),new(30,20),new(0,20)],true);
        vm.SetToolMode(mode); vm.EditWholePolyline=true; vm.EditDistance=3; vm.EditSecondDistance=5;
        Click(vm,new(15,0)); Assert.True(vm.CanAdvanceEdit);
        Assert.NotEmpty(vm.CreateTransientItems()); Click(vm,new(15,10)); Assert.Empty(vm.StepInputError);
        var curve=Assert.IsAssignableFrom<Curve>(Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased));
        Assert.True(curve.IsClosed);
    }

    private static CadDocumentViewModel Prepare(CadToolboxTestContext context)
    {
        var vm=context.Document; vm.SetViewportSize(800,600); vm.CadEditor.Viewport.SetView(10,new(200,400));
        vm.IsObjectSnapEnabled=false; vm.IsGridSnapEnabled=false; return vm;
    }

    [Fact]
    public void PolylineVertexOffersExactCoordinatesAndWholeCornerOptionKeepsInvalidText()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var id=vm.CadEditor.AddPolyline([new(0,0),new(20,0),new(20,20)]);
        Begin(vm,id,CadHandleType.Vertex,new(20,0));
        Assert.Equal(new[]{"X:","Y:"},vm.DynamicInputFields.Select(f=>f.Prefix));
        vm.DynamicInputFields[0].Text="24"; vm.DynamicInputFields[1].Text="3"; Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(new CadPointD(24,3),((CadPolyline)vm.CadEditor.Document.GetEntity(id)).Points[1]);
        vm.Undo(); vm.SetToolMode(CadCanvasToolMode.Fillet); vm.DynamicInputFields[0].Text="bad"; vm.EditWholePolyline=true;
        Assert.Equal("bad",vm.DynamicInputFields[0].Text); Assert.False(vm.CanAdvanceEdit);
    }
    private static void Begin(CadDocumentViewModel vm,Direct2dCad.Db.EntityId id,CadHandleType type,CadPointD near)
    {
        vm.SelectEntities([id]); var grip=new CadHandleSceneBuilder().BuildSelectionHandles(vm.CadEditor.Document,[id])
            .OfType<CadGripHandle>().Where(h=>h.Type==type).OrderBy(h=>h.Position.DistanceTo(near)).First();
        Click(vm,grip.Position); Assert.True(vm.IsGripEditing); Assert.True(vm.HasDynamicInput);
    }
    private static void Move(CadDocumentViewModel vm,CadPointD point)=>vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(point));
    private static void Click(CadDocumentViewModel vm,CadPointD point)
    {
        var screen=vm.CadEditor.Viewport.WorldToScreen(point); vm.PointerMove(screen);
        vm.PointerDown(screen,CadCanvasPointerButton.Left,false); vm.PointerUp(screen,CadCanvasPointerButton.Left);
    }
}
