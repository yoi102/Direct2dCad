using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DrawingWorkflowContractTests
{
    public static IEnumerable<object[]> Modes => Enum.GetValues<CadCanvasToolMode>()
        .Where(mode => mode is not (CadCanvasToolMode.Select or CadCanvasToolMode.InsertBlock or
            CadCanvasToolMode.LayoutViewport or CadCanvasToolMode.SetOrigin))
        .Select(mode => new object[] { mode });

    [Theory]
    [MemberData(nameof(Modes))]
    public void EveryDrawingModeUsesTransientSettingsAndCreatesUndoableGeometry(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var editor = vm.CadEditor;
        vm.SetViewportSize(800, 600);
        if (mode == CadCanvasToolMode.ArcContinue)
            editor.Document.AddLine(new(-20, 0), CadPointD.Origin);
        var existing = editor.Document.Entities.Keys.ToHashSet();
        var layer = editor.Document.CreateLayer("Drawing", CadColor.Blue, new CadLineWeight(3));
        vm.SetToolMode(mode);
        context.Properties.Attach(vm);
        var panel = Assert.IsAssignableFrom<EntityPropertyViewModel>(context.Properties.Entity);
        panel.EntityName = "Contract entity";
        panel.SelectedLayerOption = panel.LayerOptions.Single(item => item.LayerId == layer);
        var appearance = Assert.IsAssignableFrom<IStrokeAppearancePropertySectionViewModel>(panel);
        appearance.UseByLayerColor = false;
        appearance.UseByLayerLineWeight = false;
        appearance.StrokeColor = CadColor.Red;
        appearance.LineWeight = 2.5;
        Assert.IsAssignableFrom<IEntitySettingsPropertySectionViewModel>(panel).ZIndex = 14;
        if (panel is TransientTextPropertyViewModel text)
        {
            text.TextContent = "Drawing contract";
            text.RotationDegrees = 30;
        }
        var history = editor.CreateDocumentHistorySnapshot();
        foreach (var point in Points(mode))
        {
            var screen = editor.Viewport.WorldToScreen(point);
            vm.PointerMove(screen);
            vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
            vm.PointerUp(screen, CadCanvasPointerButton.Left);
        }
        if (mode is CadCanvasToolMode.Polyline or CadCanvasToolMode.Polygon or CadCanvasToolMode.Spline)
            Assert.True(vm.CompleteCurrentDrawing().Handled);
        var entity = Assert.Single(editor.Document.Entities.Values, entity => !existing.Contains(entity.Id) && !entity.IsErased);
        Assert.Equal(ExpectedType(mode), entity.GetType());
        Assert.Equal("Contract entity", entity.Name);
        Assert.Equal(layer, entity.LayerId);
        Assert.Equal(14, entity.ZIndex);
        Assert.Equal(2.5, entity.LineWeight!.Value.Value);
        Assert.False(entity.UseLayerColor);
        Assert.True(double.IsFinite(entity.Bounds.Width));
        Assert.True(double.IsFinite(entity.Bounds.Height));
        if (entity is CadText createdText) Assert.Equal(Math.PI / 6, createdText.RotationRadians, 8);
        editor.Undo();
        Assert.True(entity.IsErased);
        Assert.True(editor.DocumentHistoryEquals(history));
        editor.Redo();
        Assert.False(entity.IsErased);
        Assert.Same(entity, editor.Document.GetEntity(entity.Id));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EscapeClearsPartialDrawingAndSelectionWithoutAddingHistory(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var editor = vm.CadEditor;
        vm.SetViewportSize(800, 600);
        var entity = CadEntityTestCases.Add(editor.Document, TestEntityKind.Line);
        vm.SelectEntities([entity.Id]);
        var history = editor.CreateDocumentHistorySnapshot();
        vm.SetToolMode(mode);
        Assert.Empty(editor.Selection.EntityIds);
        if (mode is not (CadCanvasToolMode.Text or CadCanvasToolMode.ArcContinue))
            vm.PointerDown(editor.Viewport.WorldToScreen(CadPointD.Origin), CadCanvasPointerButton.Left, false);
        vm.PointerMove(editor.Viewport.WorldToScreen(new(10, 20)));
        vm.Escape();
        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Single(editor.Document.Entities.Values, item => !item.IsErased);
        Assert.False(vm.IsPastePreviewActive);
        context.Properties.Attach(vm);
        Assert.Null(context.Properties.Entity);
    }

    [Fact]
    public void OriginPlacementIsUndoableAndRightButtonPanDoesNotDraw()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetToolMode(CadCanvasToolMode.SetOrigin);
        var screen = vm.CadEditor.Viewport.WorldToScreen(new(30, 40));
        vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
        Assert.Equal(new CadPointD(30, 40), vm.CadEditor.Document.ViewSettings.Origin.Position);
        vm.Undo();
        Assert.Equal(CadPointD.Origin, vm.CadEditor.Document.ViewSettings.Origin.Position);
        vm.SetToolMode(CadCanvasToolMode.Line);
        vm.PointerDown(new(100, 100), CadCanvasPointerButton.Right, false);
        Assert.True(vm.IsPanning);
        vm.PointerMove(new(200, 200));
        vm.PointerUp(new(200, 200), CadCanvasPointerButton.Right);
        Assert.False(vm.IsPanning);
        Assert.Empty(vm.CadEditor.Document.Entities);
        Assert.Equal(CadCanvasToolMode.Line, vm.CadCanvasToolMode);
    }

    private static CadPointD[] Points(CadCanvasToolMode mode) => mode switch
    {
        CadCanvasToolMode.Text => [new(10, 20)],
        CadCanvasToolMode.ArcContinue => [new(20, 20)],
        CadCanvasToolMode.Line or CadCanvasToolMode.Rectangle => [new(0, 0), new(40, 30)],
        CadCanvasToolMode.CircleCenterRadius or CadCanvasToolMode.CircleCenterDiameter or CadCanvasToolMode.CircleTwoPoint => [new(0, 0), new(20, 0)],
        CadCanvasToolMode.EllipseArc => [new(-20, 0), new(20, 0), new(0, 10), new(20, 0), new(0, 10)],
        _ => [new(0, 0), new(20, 0), new(0, 20)]
    };

    private static Type ExpectedType(CadCanvasToolMode mode) => mode switch
    {
        CadCanvasToolMode.Line => typeof(CadLine),
        CadCanvasToolMode.CircleCenterRadius or CadCanvasToolMode.CircleCenterDiameter or CadCanvasToolMode.CircleTwoPoint or CadCanvasToolMode.CircleThreePoint => typeof(CadCircle),
        CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseAxisEnd => typeof(CadEllipse),
        CadCanvasToolMode.EllipseArc => typeof(CadEllipseArc),
        CadCanvasToolMode.Rectangle => typeof(CadRectangle),
        CadCanvasToolMode.Polyline or CadCanvasToolMode.Polygon => typeof(CadPolyline),
        CadCanvasToolMode.Spline => typeof(CadSpline),
        CadCanvasToolMode.Text => typeof(CadText),
        _ => typeof(CadArc)
    };
}
