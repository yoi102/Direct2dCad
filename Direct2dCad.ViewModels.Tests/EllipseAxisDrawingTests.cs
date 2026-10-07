using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EllipseAxisDrawingTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.EllipseCenter, 40, 40)]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd, 40, 40)]
    [InlineData(CadCanvasToolMode.EllipseCenter, -40, -40)]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd, -40, -40)]
    [InlineData(CadCanvasToolMode.EllipseCenter, 0, 40)]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd, 0, -40)]
    [InlineData(CadCanvasToolMode.EllipseCenter, 40, 0)]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd, -40, 0)]
    public void PreviewAndCommitPassThroughSpecifiedAxisEndpoints(CadCanvasToolMode mode, double x, double y)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, mode);
        var center = new CadPointD(12, -8);
        var axis = new CadVectorD(x, y);
        var other = center + axis.Normalize().Perpendicular() * 12;
        Click(vm, mode == CadCanvasToolMode.EllipseCenter ? center : center - axis);
        Click(vm, center + axis); Move(vm, other);
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientEllipse>());
        OnEllipse(preview.Center, preview.RadiusX, preview.RadiusY, preview.RotationRadians, center + axis);
        OnEllipse(preview.Center, preview.RadiusX, preview.RadiusY, preview.RotationRadians, other);
        Click(vm, other);
        var ellipse = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipse>());
        Assert.Equal(preview.Center, ellipse.Center);
        Assert.Equal(preview.RadiusX, ellipse.RadiusX, 8);
        Assert.Equal(preview.RadiusY, ellipse.RadiusY, 8);
        Assert.Equal(preview.RotationRadians, ellipse.RotationRadians, 8);
        vm.Undo(); Assert.True(ellipse.IsErased);
        vm.Redo(); Assert.False(ellipse.IsErased);
        OnEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.RotationRadians, center + axis);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.EllipseCenter)]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd)]
    public void TypedRadiiKeepTiltAndConfirmedFirstPoint(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, mode);
        var first = new CadPointD(5, 8);
        Click(vm, first); Click(vm, first + new CadVectorD(40, 40));
        Move(vm, first + new CadVectorD(-10, 10));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Field(vm, "AxisX").Text = "30"; Field(vm, "AxisY").Text = "12";
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientEllipse>());
        Assert.Equal(Math.PI / 4, preview.RotationRadians, 8);
        Assert.Equal(30, preview.RadiusX, 8); Assert.Equal(12, preview.RadiusY, 8);
        var expectedCenter = mode == CadCanvasToolMode.EllipseCenter ? first : first + new CadVectorD(1, 1).Normalize() * 30;
        Assert.True(preview.Center.NearEquals(expectedCenter));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(vm.SubmitDynamicInput());
        var ellipse = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipse>());
        Assert.True(ellipse.Center.NearEquals(expectedCenter));
        Assert.Equal(preview.RotationRadians, ellipse.RotationRadians, 8);
    }

    [Fact]
    public void TiltedArcKeepsLocalStartParameterWhenRadiiAreEdited()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.EllipseArc);
        Click(vm, new(-40, -40)); Click(vm, new(40, 40)); Click(vm, new(-10, 10));
        Field(vm, "StartAngle").Text = "30";
        Assert.True(vm.SubmitDynamicInput());
        Field(vm, "AxisX").Text = "30"; Field(vm, "AxisY").Text = "15"; Field(vm, "Angle").Text = "120";
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientEllipseArc>());
        Assert.Equal(Math.PI / 4, preview.RotationRadians, 8);
        Assert.Equal(Math.PI / 6, preview.StartAngleRadians, 8);
        Assert.Equal(2 * Math.PI / 3, preview.SweepAngleRadians, 8);
        Assert.True(vm.SubmitDynamicInput());
        var arc = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipseArc>());
        Assert.Equal(preview.RotationRadians, arc.RotationRadians, 8);
        Assert.Equal(preview.StartAngleRadians, arc.StartAngleRadians, 8);
        Assert.Equal(preview.SweepAngleRadians, arc.SweepAngleRadians, 8);
        vm.Undo(); Assert.True(arc.IsErased); vm.Redo(); Assert.False(arc.IsErased);
    }

    [Fact]
    public void CollinearOtherAxisCannotCreateAnEllipse()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context, CadCanvasToolMode.EllipseCenter);
        Click(vm, default); Click(vm, new(40, 40)); Click(vm, new(10, 10));
        Assert.Empty(vm.CadEditor.Document.Entities);
        Click(vm, new(-10, 10));
        Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadEllipse>());
    }

    private static void OnEllipse(CadPointD center, double rx, double ry, double rotation, CadPointD point)
    {
        var local = CadMatrixD.CreateRotation(-rotation).TransformVector(point - center);
        Assert.Equal(1, local.X * local.X / (rx * rx) + local.Y * local.Y / (ry * ry), 8);
    }
    private static CadDocumentViewModel Prepare(CadToolboxTestContext context, CadCanvasToolMode mode)
    {
        var vm = context.Document; vm.SetViewportSize(800, 600);
        vm.SetSnapping(new CadSnapSettings { GridEnabled = false, ObjectsEnabled = false });
        vm.SetToolMode(mode); return vm;
    }
    private static CadDynamicInputField Field(CadDocumentViewModel vm, string key) => vm.DynamicInputFields.Single(f => f.Key == key);
    private static void Move(CadDocumentViewModel vm, CadPointD point) => vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(point));
    private static void Click(CadDocumentViewModel vm, CadPointD point)
    {
        var screen = vm.CadEditor.Viewport.WorldToScreen(point);
        vm.PointerMove(screen); vm.PointerDown(screen, CadCanvasPointerButton.Left, false); vm.PointerUp(screen, CadCanvasPointerButton.Left);
    }
}
