using System.Globalization;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CurveDynamicInputTests
{
    [Fact]
    public void TwoPointDiameterInputUsesTheFixedEndpointAndItsOwnMeasurementLine()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.CircleTwoPoint);
        Click(vm, new(10, 20)); Move(vm, new(40, 20));
        Field(vm, "Diameter").Text = "80";
        var span = Assert.Single(vm.DynamicInputScreenMeasurements);
        Assert.Equal(vm.CadEditor.Viewport.WorldToScreen(new(10, 20)), span.Start);
        Assert.Equal(vm.CadEditor.Viewport.WorldToScreen(new(90, 20)), span.End);
        Assert.True(vm.SubmitDynamicInput());
        var circle = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>());
        Assert.Equal(new CadPointD(50, 20), circle.Center);
        Assert.Equal(40, circle.Radius, 8);
    }

    [Fact]
    public void ThreePointCircleRadiusControlsTheActualPreviewAndEntityWithoutMovingTheConfirmedPoints()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.CircleThreePoint);
        Click(vm, new(-10, 0)); Click(vm, new(10, 0)); Move(vm, new(0, 10));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Field(vm, "Radius").Text = "20";
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientCircle>());
        Assert.Equal(20, preview.Radius, 8);
        Assert.Empty(vm.CreateTransientItems().OfType<CadTransientText>());
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(vm.SubmitDynamicInput());
        var circle = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>());
        Assert.Equal(20, circle.Radius, 8);
        Assert.Equal(20, circle.Center.DistanceTo(new(-10, 0)), 8);
        Assert.Equal(20, circle.Center.DistanceTo(new(10, 0)), 8);
        vm.Undo(); Assert.True(circle.IsErased); vm.Redo(); Assert.False(circle.IsErased);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.EllipseCenter, false, 0)]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd, false, 10)]
    [InlineData(CadCanvasToolMode.EllipseCenter, true, 0)]
    public void BothEllipseSemiaxesAreEditableAndPreviewDoesNotAlterTheConfirmedConstruction(CadCanvasToolMode mode, bool vertical, double expectedCenterX)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, mode);
        Click(vm, mode == CadCanvasToolMode.EllipseAxisEnd ? new(-20, 0) : default);
        Click(vm, vertical ? new(0, 20) : new(20, 0));
        Move(vm, vertical ? new(10, 0) : new(0, 10));
        Assert.Equal(new[] { "AxisX", "AxisY" }, vm.DynamicInputFields.Select(f => f.Key));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        var anchor = vm.DrawingAnchor;
        Field(vm, "AxisX").Text = "30"; Field(vm, "AxisY").Text = "15";
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientEllipse>());
        Assert.Equal(30, preview.RadiusX, 8); Assert.Equal(15, preview.RadiusY, 8);
        Assert.Equal(anchor, vm.DrawingAnchor);
        Assert.Equal(new[] { "AxisX", "AxisY" }, vm.DynamicInputScreenMeasurements.Select(m => m.Key));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.ClearDynamicInputLocks();
        var originalPreview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientEllipse>());
        Assert.Equal(vertical ? 10 : 20, originalPreview.RadiusX, 8);
        Assert.Equal(vertical ? 20 : 10, originalPreview.RadiusY, 8);
        Field(vm, "AxisX").Text = "30"; Field(vm, "AxisY").Text = "15";
        Assert.True(vm.SubmitDynamicInput());
        var ellipse = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipse>());
        Assert.Equal(new CadPointD(expectedCenterX, 0), ellipse.Center);
        Assert.Equal(30, ellipse.RadiusX, 8); Assert.Equal(15, ellipse.RadiusY, 8);
    }

    [Fact]
    public void EllipseArcKeepsBothAxesEditableAtTheStartAndSweepStepsAndCommitsAllTypedValues()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.EllipseArc);
        Click(vm, new(-20, 0)); Click(vm, new(20, 0)); Click(vm, new(0, 10));
        Assert.Equal(new[] { "AxisX", "AxisY", "StartAngle" }, vm.DynamicInputFields.Select(f => f.Key));
        Field(vm, "AxisX").Text = "25"; Field(vm, "AxisY").Text = "15"; Field(vm, "StartAngle").Text = "30";
        Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(new[] { "AxisX", "AxisY", "Angle" }, vm.DynamicInputFields.Select(f => f.Key));
        Field(vm, "AxisX").Text = "30"; Field(vm, "AxisY").Text = "20"; Field(vm, "Angle").Text = "120";
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientEllipseArc>());
        Assert.Equal(30, preview.RadiusX, 8); Assert.Equal(20, preview.RadiusY, 8);
        Assert.Equal(Math.PI / 6, preview.StartAngleRadians, 8);
        Assert.Equal(Math.PI * 2 / 3, preview.SweepAngleRadians, 8);
        Assert.Equal(3, vm.DynamicInputScreenMeasurements.Count);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(vm.SubmitDynamicInput());
        var arc = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipseArc>());
        Assert.Equal(new CadPointD(10, 0), arc.Center);
        Assert.Equal(30, arc.RadiusX, 8); Assert.Equal(20, arc.RadiusY, 8);
        Assert.Equal(preview.StartAngleRadians, arc.StartAngleRadians, 8);
        Assert.Equal(preview.SweepAngleRadians, arc.SweepAngleRadians, 8);
        vm.Undo(); Assert.True(arc.IsErased); vm.Redo(); Assert.False(arc.IsErased);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.ArcStartCenterEnd)]
    [InlineData(CadCanvasToolMode.ArcStartCenterAngle)]
    [InlineData(CadCanvasToolMode.ArcCenterStartEnd)]
    [InlineData(CadCanvasToolMode.ArcCenterStartAngle)]
    public void CenterArcsAcceptAnExactRadiusThenAnExactSweepAngle(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, mode);
        var centerFirst = mode is CadCanvasToolMode.ArcCenterStartEnd or CadCanvasToolMode.ArcCenterStartAngle;
        Click(vm, centerFirst ? default : new(25, 0));
        Move(vm, centerFirst ? new(10, 0) : new(0, 0));
        Field(vm, "Radius").Text = "25";
        Assert.True(vm.SubmitDynamicInput());
        Field(vm, "Angle").Text = "90";
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientArc>());
        Assert.Equal(25, preview.Radius, 8); Assert.Equal(Math.PI / 2, preview.SweepAngleRadians, 8);
        Assert.True(vm.SubmitDynamicInput());
        var arc = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadArc>());
        Assert.Equal(25, arc.Radius, 8); Assert.Equal(Math.PI / 2, arc.SweepAngleRadians, 8);
        Assert.Equal(CadPointD.Origin, arc.Center);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.ArcStartCenterLength)]
    [InlineData(CadCanvasToolMode.ArcCenterStartLength)]
    public void CenterArcChordLengthRejectsImpossibleLengthsAndCreatesTheExactChord(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, mode);
        var centerFirst = mode == CadCanvasToolMode.ArcCenterStartLength;
        Click(vm, centerFirst ? default : new(10, 0)); Click(vm, centerFirst ? new(10, 0) : default);
        Move(vm, new(0, 10));
        Field(vm, "Length").Text = "21";
        Assert.False(vm.SubmitDynamicInput()); Assert.Empty(vm.CadEditor.Document.Entities);
        Field(vm, "Length").Text = "10";
        Assert.True(vm.SubmitDynamicInput());
        var arc = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadArc>());
        Assert.Equal(10, arc.StartPoint.DistanceTo(arc.EndPoint), 8);
        Assert.Equal(Math.PI / 3, arc.SweepAngleRadians, 8);
    }

    [Fact]
    public void StartEndArcAngleSupportsMajorArcsAndRadiusRejectsValuesBelowHalfTheChord()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.ArcStartEndAngle);
        Click(vm, default); Click(vm, new(20, 0)); Move(vm, new(10, 10));
        Field(vm, "Angle").Text = "270"; Assert.True(vm.SubmitDynamicInput());
        var major = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadArc>());
        Assert.Equal(Math.PI * 1.5, Math.Abs(major.SweepAngleRadians), 8);
        vm.SetToolMode(CadCanvasToolMode.ArcStartEndRadius);
        Click(vm, default); Click(vm, new(20, 0)); Move(vm, new(10, 10));
        Field(vm, "Radius").Text = "5"; Assert.False(vm.SubmitDynamicInput());
        Field(vm, "Radius").Text = "25"; Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(25, vm.CadEditor.Document.Entities.Values.OfType<CadArc>().Last().Radius, 8);
    }

    [Fact]
    public void ThreePointArcRadiusAndAngleMustAlsoPassThroughTheConfirmedSecondPoint()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.ArcThreePoint);
        Click(vm, new(10, 0)); Click(vm, new(0, 10)); Move(vm, new(-10, 0));
        Field(vm, "Radius").Text = "20"; Field(vm, "Angle").Text = "10";
        Assert.False(vm.SubmitDynamicInput()); Assert.Empty(vm.CadEditor.Document.Entities);
        Field(vm, "Angle").Text = "180";
        Assert.True(vm.SubmitDynamicInput());
        var arc = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadArc>());
        Assert.Equal(20, arc.Radius, 8); Assert.Equal(Math.PI, Math.Abs(arc.SweepAngleRadians), 8);
        Assert.True(arc.StartPoint.NearEquals(new(10, 0)));
        Assert.Equal(20, arc.Center.DistanceTo(new(0, 10)), 8);
    }

    [Fact]
    public void DirectionAndContinueArcInputsControlTangencyAndSize()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.ArcStartEndDirection);
        Click(vm, default); Click(vm, new(20, 20)); Move(vm, new(30, 0));
        Field(vm, "Direction").Text = "90"; Assert.True(vm.SubmitDynamicInput());
        var directed = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadArc>());
        Assert.True(directed.Center.NearEquals(new(20, 0)));
        Assert.True(directed.IsClockwise);
        vm.CadEditor.Document.AddLine(new(-20, 0), default);
        vm.SetToolMode(CadCanvasToolMode.ArcContinue); Move(vm, new(20, 20));
        Field(vm, "Radius").Text = "30"; Field(vm, "Angle").Text = "90";
        Assert.True(vm.SubmitDynamicInput());
        var continued = vm.CadEditor.Document.Entities.Values.OfType<CadArc>().Last();
        Assert.True(continued.StartPoint.NearEquals(default));
        Assert.Equal(30, continued.Radius, 8); Assert.Equal(Math.PI / 2, continued.SweepAngleRadians, 8);
    }

    [Theory]
    [InlineData("0")][InlineData("-1")][InlineData("360")][InlineData("NaN")][InlineData("1e309")]
    public void InvalidSweepPreservesTheStepAndHistory(string invalid)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.ArcCenterStartAngle);
        Click(vm, default); Click(vm, new(10, 0)); Move(vm, new(0, 10));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Field(vm, "Angle").Text = invalid;
        Assert.False(vm.SubmitDynamicInput()); Assert.NotEmpty(vm.DynamicInputError);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Field(vm, "Angle").Text = "90"; Assert.True(vm.SubmitDynamicInput());
    }

    [Fact]
    public void EllipseAxesUseDocumentUnitsAndNumericDisplaysAvoidScientificNotation()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.Line);
        Move(vm, new(2289042840, -465822898));
        Assert.DoesNotContain("E", Field(vm, "X").Text);
        vm.SetDocumentUnit(CadUnit.Inch); vm.SetToolMode(CadCanvasToolMode.EllipseCenter);
        Click(vm, default); Click(vm, new(25.4, 0)); Move(vm, new(0, 25.4));
        Field(vm, "AxisX").Text = "2"; Field(vm, "AxisY").Text = "1";
        Assert.True(vm.SubmitDynamicInput());
        var ellipse = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipse>());
        Assert.Equal(50.8, ellipse.RadiusX, 8); Assert.Equal(25.4, ellipse.RadiusY, 8);
        Assert.Equal(new[] { "X:", "Y:" }, new[] { new CadDynamicInputField("AxisX", () => { }).Prefix, new CadDynamicInputField("AxisY", () => { }).Prefix });
    }

    private static CadDocumentViewModel Prepare(CadToolboxTestContext context, CadCanvasToolMode mode)
    { var vm = context.Document; vm.SetViewportSize(800, 600); vm.SetToolMode(mode); return vm; }
    private static CadDynamicInputField Field(CadDocumentViewModel vm, string key) => vm.DynamicInputFields.Single(f => f.Key == key);
    private static void Move(CadDocumentViewModel vm, CadPointD point) => vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(point));
    private static void Click(CadDocumentViewModel vm, CadPointD point)
    {
        var screen = vm.CadEditor.Viewport.WorldToScreen(point);
        vm.PointerMove(screen); vm.PointerDown(screen, CadCanvasPointerButton.Left, false); vm.PointerUp(screen, CadCanvasPointerButton.Left);
    }
}
