using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DynamicInputTests
{
    [Fact]
    public void CoordinateInputFollowsPointerKeepsTypedAxisAndCommitsExactPointWithoutSnappingAgain()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetToolMode(CadCanvasToolMode.Line);
        Move(vm, new(10, 20));
        Assert.Equal(new[] { "X", "Y" }, vm.DynamicInputFields.Select(f => f.Key));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Field(vm, "X").Text = "1.25";
        Move(vm, new(60, 30));
        Assert.Equal("1.25", Field(vm, "X").Text);
        Assert.Equal("30", Field(vm, "Y").Text);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(new CadPointD(1.25, 30), vm.DrawingAnchor);
        Assert.All(vm.DynamicInputFields, f => Assert.False(f.IsLocked));
        Field(vm, "Length").Text = "25.4";
        Field(vm, "Angle").Text = "0";
        var linePreview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientLine>(), l => l.Start == new CadPointD(1.25, 30));
        Assert.Equal(new CadPointD(26.65, 30), linePreview.End);
        vm.IsGridSnapEnabled = true;
        Assert.True(vm.SubmitDynamicInput());
        var line = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadLine>());
        Assert.True(line.End.NearEquals(new(26.65, 30)));
        vm.Undo(); Assert.True(line.IsErased); vm.Redo(); Assert.False(line.IsErased);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.CircleCenterRadius, "Radius", 22, 22)]
    [InlineData(CadCanvasToolMode.CircleCenterDiameter, "Diameter", 22, 11)]
    public void CircleSizeChangesPreviewAndMouseClickUsesTheSameTypedSize(CadCanvasToolMode mode, string key, double size, double radius)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetToolMode(mode);
        Click(vm, new(10, 20)); Move(vm, new(40, 20));
        Assert.Equal(key, Assert.Single(vm.DynamicInputFields).Key);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Field(vm, key).Text = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.PointerLeave();
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientCircle>());
        Assert.Equal(radius, preview.Radius, 8);
        Assert.Empty(vm.CreateTransientItems().OfType<CadTransientText>());
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm, new(10, 120));
        var circle = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>());
        Assert.Equal(radius, circle.Radius, 8);
        Assert.Equal(new CadPointD(10, 20), circle.Center);
        Assert.Equal(new[] { "X", "Y" }, vm.DynamicInputFields.Select(f => f.Key));
        Assert.All(vm.DynamicInputFields, f => Assert.False(f.IsLocked));
    }

    [Fact]
    public void RectangleUsesTypedDimensionsAndPointerQuadrant()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetToolMode(CadCanvasToolMode.Rectangle);
        Click(vm, new(10, 20)); Move(vm, new(-20, -30));
        Field(vm, "Width").Text = "40"; Field(vm, "Height").Text = "25";
        var preview = Assert.Single(vm.CreateTransientItems().OfType<CadTransientRectangle>());
        Assert.Equal(40, preview.Bounds.Width, 8); Assert.Equal(25, preview.Bounds.Height, 8);
        Assert.True(vm.SubmitDynamicInput());
        var rectangle = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadRectangle>());
        Assert.Equal(40, rectangle.Bounds.Width, 8); Assert.Equal(25, rectangle.Bounds.Height, 8);
        Assert.Equal(-30, rectangle.Bounds.Left, 8); Assert.Equal(-5, rectangle.Bounds.Bottom, 8);
    }

    [Fact]
    public void DynamicScreenGeometryUsesTheConstrainedRectangleCornersWithoutChangingHistory()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetToolMode(CadCanvasToolMode.Rectangle);
        Click(vm, new(10, 20)); Move(vm, new(-20, -30));
        Field(vm, "Width").Text = "40"; Field(vm, "Height").Text = "25";
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        var geometry = vm.DynamicInputScreenGeometry;
        Assert.Equal(vm.CadEditor.Viewport.WorldToScreen(new(10, 20)), geometry.Anchor);
        Assert.Equal(vm.CadEditor.Viewport.WorldToScreen(new(-30, -5)), geometry.Point);
        Assert.Equal(vm.CadEditor.Viewport.WorldToScreen(new(-30, 20)), geometry.XCorner);
        Assert.Equal(vm.CadEditor.Viewport.WorldToScreen(new(10, -5)), geometry.YCorner);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData("")][InlineData("-")][InlineData("NaN")][InlineData("Infinity")][InlineData("abc")][InlineData("0")][InlineData("-1")]
    public void InvalidSizeKeepsStepGeometryAndHistoryAndCanBeCorrected(string invalid)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetToolMode(CadCanvasToolMode.CircleCenterRadius);
        Click(vm, new(10, 20)); Move(vm, new(40, 20));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Field(vm, "Radius").Text = invalid;
        Assert.False(vm.SubmitDynamicInput()); Assert.NotEmpty(vm.DynamicInputError);
        Assert.Empty(vm.CadEditor.Document.Entities); Assert.Equal(new CadPointD(10, 20), vm.DrawingAnchor);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Field(vm, "Radius").Text = "12.5";
        Assert.True(vm.SubmitDynamicInput()); Assert.Empty(vm.DynamicInputError);
        Assert.Equal(12.5, Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>()).Radius);
    }

    [Fact]
    public void UnitConversionCancelAndToolChangesKeepInputsWithinTheirDocument()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetDocumentUnit(CadUnit.Inch); vm.SetToolMode(CadCanvasToolMode.CircleCenterDiameter);
        Field(vm, "X").Text = "1"; Field(vm, "Y").Text = "2";
        Assert.True(vm.SubmitDynamicInput());
        Field(vm, "Diameter").Text = "2";
        Assert.Equal("in", vm.DynamicInputUnit);
        Assert.True(vm.SubmitDynamicInput());
        var circle = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>());
        Assert.Equal(new CadPointD(25.4, 50.8), circle.Center); Assert.Equal(25.4, circle.Radius, 8);
        Field(vm, "X").Text = "99";
        vm.SetToolMode(CadCanvasToolMode.Line);
        Assert.All(vm.DynamicInputFields, f => Assert.False(f.IsLocked));
        Field(vm, "X").Text = "42";
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.Escape(); Assert.False(vm.HasDynamicInput); Assert.Empty(vm.DynamicInputFields);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); Assert.False(circle.IsErased);
    }

    [Fact]
    public void UneditedFieldsCommitTheFullPrecisionPointerAndAngleCanBeLockedAlone()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetToolMode(CadCanvasToolMode.Line);
        Move(vm, new(1.123456789123, 2.123456789123));
        Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(1.123456789123, vm.DrawingAnchor!.Value.X, 11);
        Move(vm, new(31.123456789123, 42.123456789123));
        Field(vm, "Angle").Text = "90";
        Assert.True(vm.SubmitDynamicInput());
        var line = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadLine>());
        Assert.Equal(50, line.Length, 8); Assert.Equal(line.Start.X, line.End.X, 8);
    }

    [Fact]
    public void AnnotationDynamicCoordinatesRetainConfirmedMarkersAndDoNotCommitDuringTyping()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.SetToolMode(CadCanvasToolMode.Leader);
        Field(vm, "X").Text = "10"; Field(vm, "Y").Text = "20";
        Assert.True(vm.SubmitDynamicInput());
        Field(vm, "Length").Text = "50"; Field(vm, "Angle").Text = "0";
        Assert.True(vm.SubmitDynamicInput());
        Field(vm, "X").Text = "70"; Field(vm, "Y").Text = "50";
        Assert.Empty(vm.CadEditor.Document.Entities);
        Assert.Equal(2, vm.CreateTransientItems().OfType<CadTransientCircle>().Count(c => c.Style.FillColor is not null));
        Assert.True(vm.SubmitDynamicInput());
        var dimension = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        Assert.Equal(new CadPointD(70, 50), dimension.Definition.Placement);
    }

    private static CadDynamicInputField Field(CadDocumentViewModel vm, string key) => vm.DynamicInputFields.Single(f => f.Key == key);
    private static void Move(CadDocumentViewModel vm, CadPointD point) => vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(point));
    private static void Click(CadDocumentViewModel vm, CadPointD point)
    {
        var screen = vm.CadEditor.Viewport.WorldToScreen(point);
        vm.PointerMove(screen); vm.PointerDown(screen, CadCanvasPointerButton.Left, false); vm.PointerUp(screen, CadCanvasPointerButton.Left);
    }
}
