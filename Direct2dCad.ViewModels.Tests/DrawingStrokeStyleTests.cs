using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DrawingStrokeStyleTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.Line, true, false)]
    [InlineData(CadCanvasToolMode.ArcThreePoint, true, false)]
    [InlineData(CadCanvasToolMode.CircleCenterRadius, false, false)]
    [InlineData(CadCanvasToolMode.EllipseCenter, false, false)]
    [InlineData(CadCanvasToolMode.EllipseArc, true, false)]
    [InlineData(CadCanvasToolMode.Rectangle, false, true)]
    [InlineData(CadCanvasToolMode.Polyline, true, true)]
    [InlineData(CadCanvasToolMode.Polygon, false, true)]
    [InlineData(CadCanvasToolMode.Spline, true, true)]
    public void DrawingStyleUpdatesPreviewAndSurvivesToolSwitch(
        CadCanvasToolMode mode, bool caps, bool joins)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetToolMode(mode);
        context.Properties.Attach(vm);
        var panel = Assert.IsAssignableFrom<EntityPropertyViewModel>(context.Properties.Entity);
        Assert.Equal(caps, panel.SupportsStartEndCaps);
        Assert.Equal(joins, panel.SupportsLineJoin);
        Assert.Equal(CadStrokeDashStyle.Solid, panel.SelectedDashStyleOption!.Value);
        var resolver = new CadDrawingStyleResolver(vm.CadEditor.Document,
            vm.CadEditor.Document.GetLayer(vm.DrawingLayerId), vm.DrawingDefaults,
            new CadPreviewStyleService(vm.CadEditor.Document, CadUserSettings.CreateDefault()));
        var changes = 0;
        vm.DrawingDefaults.DefaultsChanged += (_, _) => changes++;
        panel.SelectedDashStyleOption = panel.StrokeDashStyleOptions.Single(option => option.Value == CadStrokeDashStyle.Dot);
        panel.SelectedDashCapOption = panel.StrokeCapOptions.Single(option => option.Value == CadStrokeCap.Round);
        var expected = CadStrokeStyle.Default with { DashStyle = CadStrokeDashStyle.Dot, DashCap = CadStrokeCap.Round };
        Assert.Equal(2, changes);
        Assert.Equal(expected, Preview(resolver, mode).StrokeStyle);
        Assert.Null(resolver.CreateLineGuideStyle().StrokeStyle);
        Assert.Equal(CadTransientLinePattern.Dash, resolver.CreateLineGuideStyle().LinePattern);

        vm.SetToolMode(CadCanvasToolMode.Text);
        vm.SetToolMode(mode);
        panel = Assert.IsAssignableFrom<EntityPropertyViewModel>(context.Properties.Entity);
        Assert.Equal(CadStrokeDashStyle.Dot, panel.SelectedDashStyleOption!.Value);
        Assert.Equal(CadStrokeCap.Round, panel.SelectedDashCapOption!.Value);
        var changesBeforeRefresh = changes;
        context.Publish();
        Assert.Equal(changesBeforeRefresh, changes);
        Assert.Equal(expected, Preview(resolver, mode).StrokeStyle);
        Assert.Equal(CadStrokeStyle.Default,
            mode == CadCanvasToolMode.Line ? vm.DrawingDefaults.CircleStrokeStyle : vm.DrawingDefaults.LineStrokeStyle);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.Polyline)]
    [InlineData(CadCanvasToolMode.Spline)]
    public void ClosingCurveHidesCapsAndReopeningPreservesTheirValues(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        context.Document.SetToolMode(mode);
        context.Properties.Attach(context.Document);
        var panel = Assert.IsAssignableFrom<EntityPropertyViewModel>(context.Properties.Entity);
        panel.SelectedStartCapOption = panel.StrokeCapOptions.Single(option => option.Value == CadStrokeCap.Round);
        SetClosed(true);
        Assert.False(panel.SupportsStartEndCaps);
        Assert.True(panel.SupportsLineJoin);
        SetClosed(false);
        Assert.True(panel.SupportsStartEndCaps);
        Assert.Equal(CadStrokeCap.Round, panel.SelectedStartCapOption!.Value);

        void SetClosed(bool closed)
        {
            if (panel is TransientPolylinePropertyViewModel polyline) polyline.IsClosed = closed;
            else Assert.IsType<TransientSplinePropertyViewModel>(panel).IsClosed = closed;
        }
    }

    [Fact]
    public void SwitchingEllipseAndEllipseArcRefreshesCapAvailabilityOnSamePanel()
    {
        using var context = new CadToolboxTestContext();
        context.Document.SetToolMode(CadCanvasToolMode.EllipseCenter);
        context.Properties.Attach(context.Document);
        var panel = Assert.IsType<TransientEllipsePropertyViewModel>(context.Properties.Entity);
        Assert.False(panel.SupportsStartEndCaps);
        context.Document.SetToolMode(CadCanvasToolMode.EllipseArc);
        Assert.Same(panel, context.Properties.Entity);
        Assert.True(panel.SupportsStartEndCaps);
        context.Document.SetToolMode(CadCanvasToolMode.EllipseAxisEnd);
        Assert.False(panel.SupportsStartEndCaps);
    }

    private static CadTransientStyle Preview(CadDrawingStyleResolver resolver, CadCanvasToolMode mode) => mode switch
    {
        CadCanvasToolMode.Line => resolver.CreateLineTransientStyle(),
        CadCanvasToolMode.ArcThreePoint => resolver.CreateArcTransientStyle(),
        CadCanvasToolMode.CircleCenterRadius => resolver.CreateCircleTransientStyle(),
        CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseArc => resolver.CreateEllipseTransientStyle(),
        CadCanvasToolMode.Rectangle => resolver.CreateRectangleTransientStyle(),
        CadCanvasToolMode.Polyline => resolver.CreatePolylineTransientStyle(),
        CadCanvasToolMode.Polygon => resolver.CreatePolygonTransientStyle(),
        CadCanvasToolMode.Spline => resolver.CreateSplineTransientStyle(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
