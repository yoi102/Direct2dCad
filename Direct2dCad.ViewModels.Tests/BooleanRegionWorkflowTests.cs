using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class BooleanRegionWorkflowTests
{
    [Fact]
    public async Task CancellingPendingWorkerCannotReappearOrCommit()
    {
        using var c = new CadToolboxTestContext(); var vm = c.Document;
        var ids = Enumerable.Range(0, 200).Select(i => vm.CadEditor.AddCircle(new(i * 30, 0), 10)).ToArray();
        vm.SelectEntities(ids); var before = vm.CadEditor.CreateDocumentHistorySnapshot();
        var pending = vm.BeginBoolean(CadBooleanOperation.Union); vm.Escape(); await pending;
        Assert.False(vm.IsBooleanTool); Assert.False(vm.IsBooleanCalculating); Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(before)); Assert.Equal(200, vm.CadEditor.Document.Entities.Count);
    }
    [Theory][InlineData(CadBooleanOperation.Union)][InlineData(CadBooleanOperation.Intersection)]
    public async Task PreviewDoesNotMutateAndEnterCommitsOneUndo(CadBooleanOperation operation)
    {
        using var c = new CadToolboxTestContext(); var vm = c.Document; vm.SetViewportSize(800, 600); c.Properties.Attach(vm);
        var a = vm.CadEditor.AddCircle(default, 10); var b = vm.CadEditor.AddCircle(new(10, 0), 10);
        Assert.False(vm.CanBooleanSelection); vm.SelectEntities([a]); Assert.False(vm.CanBooleanSelection);
        vm.SelectEntities([a, b]); Assert.True(vm.CanBooleanSelection);
        var before = vm.CadEditor.CreateDocumentHistorySnapshot(); await vm.BeginBoolean(operation);
        Assert.False(vm.HasDynamicInput);
        Assert.True(new Direct2dCad.CommandLine.CadCommandLineService().Execute("STATUS", vm).Success);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(before)); vm.Escape(); Assert.Equal(2, vm.CadEditor.Document.Entities.Count);
        vm.SelectEntities([a, b]); await vm.BeginBoolean(operation); vm.CompleteCurrentDrawing();
        var result = Assert.IsType<CadRegion>(vm.CadEditor.Document.GetEntity(Assert.Single(vm.CadEditor.Selection.EntityIds)));
        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode); Assert.Equal(3, vm.CadEditor.Document.Entities.Count);
        var properties = Assert.IsType<CommonEntityPropertyViewModel>(c.Properties.Entity); Assert.True(properties.IsRegion); Assert.True(properties.SupportsStrokeStyle); Assert.True(properties.FillControlsEnabled);
        vm.Undo(); Assert.True(result.IsErased); Assert.False(vm.CadEditor.Document.GetEntity(a).IsErased);
        vm.Redo(); Assert.False(result.IsErased);
    }
    [Fact]
    public async Task DifferencePicksSubjectWithRawPointerAndRejectsUnsupportedSelection()
    {
        using var c = new CadToolboxTestContext(); var vm = c.Document; vm.SetViewportSize(800, 600); c.Properties.Attach(vm); vm.CadEditor.Viewport.SetView(20, new(400, 300));
        var small = vm.CadEditor.AddCircle(default, 3); var large = vm.CadEditor.AddCircle(default, 10);
        vm.SelectEntities([small, large]); vm.IsGridSnapEnabled = true; await vm.BeginBoolean(CadBooleanOperation.Difference);
        vm.CompleteCurrentDrawing(); Assert.NotEmpty(vm.StepInputError); Assert.Equal(2, vm.CadEditor.Document.Entities.Count);
        var pointer = vm.CadEditor.Viewport.WorldToScreen(new(10, 0)); vm.PointerDown(pointer, CadCanvasPointerButton.Left, false); vm.PointerUp(pointer, CadCanvasPointerButton.Left); await vm.BooleanPreviewCompletion;
        vm.CompleteCurrentDrawing();
        var r = Assert.IsType<CadRegion>(vm.CadEditor.Document.GetEntity(Assert.Single(vm.CadEditor.Selection.EntityIds)));
        Assert.Equal(Math.PI * 91, r.Area, 7); Assert.False(r.Contains(default));
        vm.Undo(); var line = vm.CadEditor.AddLine(default, new(10, 0)); vm.SelectEntities([large, line]); Assert.False(vm.CanBooleanSelection);
    }
    [Fact]
    public async Task DifferenceSubjectPickingFollowsLayerDrawingPriority()
    {
        using var c = new CadToolboxTestContext(); var vm = c.Document; var document = vm.CadEditor.Document;
        vm.SetViewportSize(800, 600); vm.CadEditor.Viewport.SetView(20, new(400, 300));
        var foreground = document.CreateLayer("Foreground", CadColor.Blue, CadLineWeight.Default);
        var outer = vm.CadEditor.AddCircle(default, 10); document.ChangeEntityLayer(outer, foreground);
        var inner = vm.CadEditor.AddCircle(default, 9.8);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(foreground, 10);
        vm.SelectEntities([outer, inner]); await vm.BeginBoolean(CadBooleanOperation.Difference);
        // Both boundaries are within the picking tolerance; the visible foreground wins.
        var pointer = vm.CadEditor.Viewport.WorldToScreen(new(10, 0));
        vm.PointerDown(pointer, CadCanvasPointerButton.Left, false); vm.PointerUp(pointer, CadCanvasPointerButton.Left);
        await vm.BooleanPreviewCompletion; vm.CompleteCurrentDrawing();
        var result = Assert.IsType<CadRegion>(document.GetEntity(Assert.Single(vm.CadEditor.Selection.EntityIds)));
        Assert.Equal(foreground, result.LayerId); Assert.Equal(Math.PI * (100 - 9.8 * 9.8), result.Area, 7);
    }

    [Fact]
    public async Task EmptyPreviewKeepsHistoryAndDocument()
    {
        using var c = new CadToolboxTestContext(); var vm = c.Document;
        var a = vm.CadEditor.AddCircle(default, 3); var b = vm.CadEditor.AddCircle(new(100, 0), 3); vm.SelectEntities([a, b]);
        var before = vm.CadEditor.CreateDocumentHistorySnapshot(); await vm.BeginBoolean(CadBooleanOperation.Intersection); vm.CompleteCurrentDrawing();
        Assert.NotEmpty(vm.StepInputError); Assert.True(vm.CadEditor.DocumentHistoryEquals(before)); Assert.Equal(2, vm.CadEditor.Document.Entities.Count); vm.Escape();
    }
}
