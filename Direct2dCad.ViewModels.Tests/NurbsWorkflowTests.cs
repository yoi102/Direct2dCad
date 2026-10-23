using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Commands;
using Direct2dCad.CommandLine;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class NurbsWorkflowTests
{
    [Fact]
    public void ControlWeightAndKnotEditsAreValidatedAndUndoable()
    {
        using var context=new CadToolboxTestContext(); var editor=context.Document.CadEditor;
        var geometry = new CadSplineGeometry(CadNurbsCurve.CreateUniform([new(0,0),new(10,20),new(20,20),new(30,0)], 3));
        var spline=Assert.IsType<CadSpline>(editor.Document.GetEntity(editor.AddSpline(geometry)));
        var panel=new SplinePropertyViewModel(context.Document,spline.Id);
        var original=spline.Geometry;
        Assert.True(panel.IsControlDefined); Assert.Empty(spline.FitPoints);
        panel.ControlPoints[1].Weight=2;
        Assert.Equal(2,spline.Weights[1]); Assert.NotEqual(original.Curve.Evaluate(.15),spline.Nurbs.Evaluate(.15));
        editor.Undo(); panel.RefreshFromEntity(); Assert.Equal(1,panel.ControlPoints[1].Weight);
        var before=spline.Geometry;panel.KnotVector="0 1";panel.ApplyKnotVectorCommand.Execute(null);
        Assert.NotEmpty(panel.GeometryError);Assert.Same(before,spline.Geometry);
        panel.RefreshFromEntity();panel.Degree=2;panel.ApplyUniformKnotsCommand.Execute(null);
        Assert.Equal(2,spline.Degree);Assert.Empty(panel.GeometryError);
        editor.Undo();Assert.Same(before,spline.Geometry);
    }

    [Fact]
    public void DrawingModesSharePreviewDefinitionAndClosedControlEditingWorks()
    {
        using var context=new CadToolboxTestContext();var vm=context.Document;
        vm.SetToolMode(CadCanvasToolMode.Spline);context.Properties.Attach(vm);
        var panel=Assert.IsType<TransientSplinePropertyViewModel>(context.Properties.Entity);
        var resolver=new CadDrawingStyleResolver(vm.CadEditor.Document,vm.CadEditor.Document.GetLayer(vm.DrawingLayerId),vm.DrawingDefaults,
            new CadPreviewStyleService(vm.CadEditor.Document,CadUserSettings.CreateDefault()));
        CadPointD[] points=[new(0,0),new(10,20),new(30,20),new(40,0)];
        Assert.True(resolver.CreateSplineGeometry(points).IsFitDefined);
        Assert.True(panel.IsFitDefined);
        vm.SetToolMode(CadCanvasToolMode.SplineCv);
        panel=Assert.IsType<TransientSplinePropertyViewModel>(context.Properties.Entity);
        Assert.True(panel.IsControlDefined);
        var geometry=resolver.CreateSplineGeometry(points, useControlPoints: true);Assert.False(geometry.IsFitDefined);Assert.Equal(3,geometry.Curve.Degree);
        Assert.Equal(new CadPointD(20,15),geometry.Curve.Evaluate(.5));
        var spline=Assert.IsType<CadSpline>(vm.CadEditor.Document.GetEntity(vm.CadEditor.AddSpline(geometry)));
        var properties=new SplinePropertyViewModel(vm,spline.Id);properties.IsClosed=true;
        Assert.True(spline.Closed);Assert.True(spline.Nurbs.IsPeriodic);
        properties.ControlPoints[0].Weight=2;
        Assert.Equal(2,spline.Weights[4]);Assert.Empty(properties.GeometryError);
        var dragged=spline.Geometry.EditPoints.ToArray();dragged[0]=new(-10,0);
        vm.CadEditor.Execute(new SetSplineGeometryCommand(spline.Id,spline.Geometry.WithEditPoints(dragged)));
        Assert.Equal(dragged[0],spline.ControlPoints[4]);Assert.Equal(2,spline.Weights[0]);
    }

    [Theory]
    [InlineData("SPLINE", "SPLINECV", false)]
    [InlineData("SPLINECV", "SPLINE", true)]
    public void SwitchingSplineToolsDiscardsPendingPointsAndCreatesTheChosenDefinition(string from, string to, bool fitDefined)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var commands = new CadCommandLineService();
        vm.SetViewportSize(800, 600);
        foreach (var command in new[] { from, "-100,-100", "-200,-200", to })
            Assert.True(commands.Execute(command, vm).Success);
        Assert.Null(vm.DrawingAnchor);
        Assert.False(vm.CanCompletePointSequence);
        foreach (var command in new[] { "0,0", "10,20", "30,20", "40,0", "DONE" })
            Assert.True(commands.Execute(command, vm).Success);
        var spline = Assert.IsType<CadSpline>(Assert.Single(vm.CadEditor.Document.Entities.Values));
        Assert.Equal(fitDefined, spline.Geometry.IsFitDefined);
        Assert.Equal(4, spline.EditPoints.Count);
        Assert.Equal(new CadPointD(0, 0), spline.EditPoints[0]);
        Assert.Equal(new CadPointD(40, 0), spline.EditPoints[^1]);
        if (!fitDefined) Assert.Equal(new CadPointD(20, 15), spline.Nurbs.Evaluate(.5));
        vm.Undo();
        Assert.True(spline.IsErased);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.Spline)]
    [InlineData(CadCanvasToolMode.SplineCv)]
    public void PointerPreviewAndCompletedCurveUseTheSameDefinition(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetSnapping(new CadSnapSettings { GridEnabled = false, ObjectsEnabled = false });
        vm.SetToolMode(mode);
        void Click(CadPointD point)
        {
            var screen = vm.CadEditor.Viewport.WorldToScreen(point);
            vm.PointerMove(screen);
            vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
            vm.PointerUp(screen, CadCanvasPointerButton.Left);
        }
        Click(new(0, 0)); Click(new(10, 20)); Click(new(30, 20));
        vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(40, 0)));
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientSpline>());
        Assert.Equal(mode == CadCanvasToolMode.Spline, preview.Geometry.IsFitDefined);
        Click(new(40, 0));
        Assert.True(vm.CompleteCurrentDrawing().Handled);
        var spline = Assert.IsType<CadSpline>(Assert.Single(vm.CadEditor.Document.Entities.Values));
        Assert.Equal(preview.Geometry.IsFitDefined, spline.Geometry.IsFitDefined);
        Assert.Equal(preview.Geometry.Curve.ControlPoints, spline.ControlPoints);
        Assert.Equal(preview.Geometry.Curve.Knots, spline.Knots);
        Assert.Equal(preview.Geometry.Curve.Weights, spline.Weights);
    }
}
