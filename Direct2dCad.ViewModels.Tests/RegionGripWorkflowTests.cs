using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Geometry;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;

namespace Direct2dCad.ViewModels.Tests;

public sealed class RegionGripWorkflowTests
{
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    public void EveryRegionGripPreservesHolesIslandsAndAnalyticArcs(int gripIndex)
    {
        using var context = new CadToolboxTestContext(); var vm = context.Document; var editor = vm.CadEditor;
        var region = AddRegion(editor); editor.Selection.Replace([region.Id]);
        var handles = new CadHandleSceneBuilder().BuildSelectionHandles(editor.Document, [region.Id]);
        var grips = handles.OfType<CadGripHandle>().ToArray();
        Assert.Equal(5, grips.Length); Assert.Equal(4, grips.Count(g => g.Type == CadHandleType.BoundsCorner));
        Assert.Single(grips, g => g.Type == CadHandleType.Center);
        var grip = grips[gripIndex]; var scene = new CadHandleScene(); scene.Replace(handles);
        var controller = new CadGripDragController(new CadHandleHitTester());
        Assert.True(controller.TryBegin(editor, scene, p => p, p => p, grip.Position));
        var drag = controller.ActiveDrag!;
        var original = region.Contours.SelectMany(c => c.Edges).ToArray(); var area = region.Area;
        var history = editor.CreateDocumentHistorySnapshot();
        CadMatrixD expected;
        if (grip.Type == CadHandleType.Center)
        {
            Assert.False(region.Contains(grip.Position)); // Center is in the gap between the ring and island.
            drag.CurrentPointerWorld = grip.Position + new CadVectorD(5, 7);
            expected = CadMatrixD.CreateTranslation(5, 7);
        }
        else
        {
            var pivot = new CadPointD(region.Bounds.MinX + region.Bounds.MaxX - grip.Position.X,
                region.Bounds.MinY + region.Bounds.MaxY - grip.Position.Y);
            drag.CurrentPointerWorld = pivot + (grip.Position - pivot) * 1.25;
            Assert.True(CadGripDragGeometryFactory.TryCreateUniformBoundsGripScale(region.Bounds, drag,
                out _, out var scale, out expected));
            Assert.Equal(1.25, scale, 8);
        }
        var previews = new List<CadTransientItem>(); var measurement = new CadTextMeasurementService(editor.Document, vm.RenderSession, editor.Viewport);
        new CadGripDragPreviewBuilder(editor, new CadPreviewStyleService(editor.Document, vm.UserSettings), measurement).AddPreview(previews, drag);
        var preview = Assert.IsType<CadTransientGroup>(Assert.Single(previews)); Assert.Equal(expected, preview.Transform);
        Assert.Equal(region.Id, Assert.IsType<CadTransientEntityReference>(Assert.Single(preview.Items)).EntityId);
        var active = Assert.IsType<CadGripHandle>(Assert.Single(controller.CreateActiveHandleItems(editor, CadHandleSceneBuildOptions.Default, 1)!));
        Assert.True(expected.TransformPoint(grip.Position).NearEquals(active.Position));
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.True(controller.Commit(editor, new CadGripDragCommitter(editor, measurement), p => p, drag.CurrentPointerWorld));
        Assert.Empty(controller.HiddenEntityIds); Assert.Equal(3, region.Contours.Count);
        var after = region.Contours.SelectMany(c => c.Edges).ToArray(); Assert.Equal(original.Length, after.Length);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.False(after[i].IsLine); Assert.Equal(original[i].Sweep, after[i].Sweep);
            Assert.True(expected.TransformPoint(original[i].Start).NearEquals(after[i].Start));
            Assert.True(expected.TransformPoint(original[i].End).NearEquals(after[i].End));
        }
        Assert.False(region.Contains(expected.TransformPoint(default)));
        Assert.True(region.Contains(expected.TransformPoint(new(5, 0))));
        Assert.True(region.Contains(expected.TransformPoint(new(30, 0))));
        Assert.False(region.Contains(expected.TransformPoint(new(20, 0))));
        Assert.Equal(area * (grip.Type == CadHandleType.Center ? 1 : 1.25 * 1.25), region.Area, 7);
        editor.Undo(); Assert.True(editor.DocumentHistoryEquals(history));
        AssertBoundaryEquivalent(original, region.Contours.SelectMany(c => c.Edges).ToArray());
        editor.Redo(); AssertBoundaryEquivalent(after, region.Contours.SelectMany(c => c.Edges).ToArray());
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void CanvasClicksPreviewCancelAndCommitRegionGrips(bool corner)
    {
        using var context = new CadToolboxTestContext(); var vm = context.Document;
        vm.SetViewportSize(800, 600); vm.CadEditor.Viewport.SetView(10, new(400, 300));
        vm.IsGridSnapEnabled = false; vm.IsObjectSnapEnabled = false;
        var region = AddRegion(vm.CadEditor); vm.SelectEntities([region.Id]);
        var grip = new CadHandleSceneBuilder().BuildSelectionHandles(vm.CadEditor.Document, [region.Id]).OfType<CadGripHandle>()
            .First(g => g.Type == (corner ? CadHandleType.BoundsCorner : CadHandleType.Center));
        var start = vm.CadEditor.Viewport.WorldToScreen(grip.Position);
        var targetWorld = grip.Position + new CadVectorD(-3, -4);
        var target = vm.CadEditor.Viewport.WorldToScreen(targetWorld);
        var original = region.Bounds; var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Click(start); vm.PointerMove(target);
        Assert.Equal(original, region.Bounds); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Escape(); Assert.Equal(original, region.Bounds); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.SelectEntities([region.Id]); Click(start); vm.PointerMove(target); Click(target);
        Assert.NotEqual(original, region.Bounds); Assert.Equal(3, region.Contours.Count);
        vm.Undo(); Assert.Equal(original, region.Bounds); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));

        void Click(CadPointD point)
        {
            var result = vm.PointerDown(point, CadCanvasPointerButton.Left, false); Assert.True(result.Handled);
            vm.PointerUp(point, CadCanvasPointerButton.Left);
        }
    }

    [Fact]
    public void RegionGripsRespectLocksAndCancel()
    {
        using var context = new CadToolboxTestContext(); var editor = context.Document.CadEditor; var region = AddRegion(editor);
        var builder = new CadHandleSceneBuilder(); var scene = new CadHandleScene(); scene.Replace(builder.BuildSelectionHandles(editor.Document, [region.Id]));
        var grip = scene.Items.OfType<CadGripHandle>().First(); var controller = new CadGripDragController(new CadHandleHitTester());
        var committer = new CadGripDragCommitter(editor, new CadTextMeasurementService(editor.Document, context.Document.RenderSession, editor.Viewport));
        var history = editor.CreateDocumentHistorySnapshot();
        Assert.True(controller.TryBegin(editor, scene, p => p, p => p, grip.Position));
        controller.UpdatePointer(p => p, grip.Position + new CadVectorD(3, 4)); controller.Clear();
        Assert.False(controller.Commit(editor, committer, p => p, grip.Position)); Assert.True(editor.DocumentHistoryEquals(history));
        Assert.True(controller.TryBegin(editor, scene, p => p, p => p, grip.Position));
        editor.SetLayerState(region.LayerId, true, true, false); history = editor.CreateDocumentHistorySnapshot();
        Assert.False(controller.Commit(editor, committer, p => p, grip.Position + new CadVectorD(3, 4)));
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Empty(builder.BuildSelectionHandles(editor.Document, [region.Id]).OfType<CadGripHandle>());
        Assert.False(controller.TryBegin(editor, scene, p => p, p => p, grip.Position));
    }

    [Fact]
    public void RegionCenterMovesTheEditableSelectionTogether()
    {
        using var context = new CadToolboxTestContext(); var vm = context.Document; var editor = vm.CadEditor;
        var region = AddRegion(editor); var line = editor.AddLine(new(50, 0), new(60, 10));
        vm.SelectEntities([region.Id, line]);
        var scene = new CadHandleScene(); scene.Replace(new CadHandleSceneBuilder().BuildSelectionHandles(editor.Document, [region.Id]));
        var grip = scene.Items.OfType<CadGripHandle>().Single(g => g.Type == CadHandleType.Center);
        var controller = new CadGripDragController(new CadHandleHitTester()); var before = region.Bounds;
        Assert.True(controller.TryBegin(editor, scene, p => p, p => p, grip.Position));
        Assert.Equal(2, controller.HiddenEntityIds.Count);
        var delta = new CadVectorD(7, 9);
        Assert.True(controller.Commit(editor, new CadGripDragCommitter(editor,
            new CadTextMeasurementService(editor.Document, vm.RenderSession, editor.Viewport)), p => p, grip.Position + delta));
        Assert.Equal(before.Translate(delta), region.Bounds); Assert.Equal(new(57, 9), Assert.IsType<CadLine>(editor.Document.GetEntity(line)).Start);
        editor.Undo(); Assert.Equal(before, region.Bounds); Assert.Equal(new(50, 0), Assert.IsType<CadLine>(editor.Document.GetEntity(line)).Start);
    }

    private static CadRegion AddRegion(CadEditor editor)
    {
        var outer = editor.AddCircle(default, 10); var hole = editor.AddCircle(default, 3);
        var difference = new BooleanRegionsCommand([outer, hole], CadBooleanOperation.Difference, outer); editor.Execute(difference);
        var island = editor.AddCircle(new(30, 0), 2);
        var union = new BooleanRegionsCommand([difference.ResultEntityId!.Value, island], CadBooleanOperation.Union); editor.Execute(union);
        return Assert.IsType<CadRegion>(editor.Document.GetEntity(union.ResultEntityId!.Value));
    }

    private static void AssertBoundaryEquivalent(CadPlanarPrimitive[] expected, CadPlanarPrimitive[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].IsLine, actual[i].IsLine);
            Assert.True(expected[i].Start.NearEquals(actual[i].Start));
            Assert.True(expected[i].End.NearEquals(actual[i].End));
            Assert.True(expected[i].Center.NearEquals(actual[i].Center));
            Assert.Equal(expected[i].Radius, actual[i].Radius, 8);
            Assert.Equal(expected[i].Sweep, actual[i].Sweep);
        }
    }
}
