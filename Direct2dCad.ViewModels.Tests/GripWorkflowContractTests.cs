using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Geometry;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;

namespace Direct2dCad.ViewModels.Tests;

public sealed class GripWorkflowContractTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void MixedPathCornerScalePreservesSegmentsAndMatchesPreview(double scale)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var path = editor.Document.AddCompositePath(new(0, 0),
        [
            new CadCompositeLineSegment(new(20, 0)),
            new CadCompositeArcSegment(new(20, 10), Math.PI / 2),
            new CadCompositeSplineSegment([new(40, 20), new(20, 30)])
        ]);
        var grip = new CadHandleSceneBuilder().BuildSelectionHandles(editor.Document, [path.Id])
            .OfType<CadGripHandle>().First(item => item.Type == CadHandleType.BoundsCorner);
        var pivot = new CadPointD(path.Bounds.MinX + path.Bounds.MaxX - grip.Position.X,
            path.Bounds.MinY + path.Bounds.MaxY - grip.Position.Y);
        var target = pivot + (grip.Position - pivot) * scale;
        var drag = new GripDragState(grip, grip.Position, -1, new HashSet<EntityId> { path.Id })
        {
            CurrentPointerWorld = target
        };
        Assert.True(CadGripDragGeometryFactory.TryCreateUniformBoundsGripScale(path.Bounds, drag,
            out var actualPivot, out var actualScale, out var transform));
        Assert.Equal(pivot, actualPivot);
        Assert.Equal(scale, actualScale, 8);
        var before = path.EnumerateFlattenedPoints().ToArray();
        var previews = new List<CadTransientItem>();
        var measurement = new CadTextMeasurementService(editor.Document,
            context.Document.Direct2DImageRenderHost, editor.Viewport);
        new CadGripDragPreviewBuilder(editor, new CadPreviewStyleService(editor.Document, context.Document.UserSettings), measurement)
            .AddPreview(previews, drag);
        Assert.Equal(transform, Assert.IsType<CadTransientGroup>(Assert.Single(previews)).Transform);
        new CadGripDragCommitter(editor, measurement).Commit(drag);
        Assert.Collection(path.Segments,
            item => Assert.IsType<CadCompositeLineSegment>(item),
            item => Assert.Equal(Math.PI / 2, Assert.IsType<CadCompositeArcSegment>(item).SweepAngleRadians),
            item => Assert.IsType<CadCompositeSplineSegment>(item));
        var after = path.EnumerateFlattenedPoints().ToArray();
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
            Assert.True(transform.TransformPoint(before[i]).NearEquals(after[i]));
        editor.Undo();
        AssertPoints(before, path.EnumerateFlattenedPoints().ToArray());
        editor.Redo();
        AssertPoints(after, path.EnumerateFlattenedPoints().ToArray());
    }

    public static IEnumerable<object[]> Grips
    {
        get
        {
            foreach (var kind in Enum.GetValues<TestEntityKind>())
            {
                var document = CadDocument.Create("Grip discovery");
                var entity = CadEntityTestCases.Add(document, kind);
                var grips = new CadHandleSceneBuilder().BuildSelectionHandles(document, [entity.Id]).OfType<CadGripHandle>().ToArray();
                for (var i = 0; i < grips.Length; i++) yield return [kind, i];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Grips))]
    public void EveryExposedGripCommitsUndoableGeometryAndClearsItsHiddenTarget(TestEntityKind kind, int gripIndex)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var grip = new CadHandleSceneBuilder().BuildSelectionHandles(editor.Document, [entity.Id]).OfType<CadGripHandle>().ElementAt(gripIndex);
        var scene = new CadHandleScene();
        scene.Replace([grip]);
        var controller = new CadGripDragController(new CadHandleHitTester());
        var history = editor.CreateDocumentHistorySnapshot();
        var originalBounds = entity.Bounds;
        Assert.True(controller.TryBegin(editor, scene, point => point, point => point, grip.Position));
        Assert.Contains(entity.Id, controller.HiddenEntityIds);
        var target = grip.Position + new CadVectorD(7, 9);
        controller.UpdatePointer(point => point, target);
        Assert.Equal(originalBounds, entity.Bounds);
        Assert.NotEmpty(controller.CreateActiveHandleItems(editor, CadHandleSceneBuildOptions.Default, 1)!);
        var previews = new List<CadTransientItem>();
        new CadGripDragPreviewBuilder(editor, new CadPreviewStyleService(editor.Document, context.Document.UserSettings),
            new CadTextMeasurementService(editor.Document, context.Document.Direct2DImageRenderHost, editor.Viewport))
            .AddPreview(previews, controller.ActiveDrag);
        Assert.NotEmpty(previews);
        var committer = new CadGripDragCommitter(editor,
            new CadTextMeasurementService(editor.Document, context.Document.Direct2DImageRenderHost, editor.Viewport));
        Assert.True(controller.Commit(editor, committer, point => point, target));
        Assert.False(controller.IsActive);
        Assert.Empty(controller.HiddenEntityIds);
        Assert.False(editor.DocumentHistoryEquals(history));
        var changedBounds = entity.Bounds;
        editor.Undo();
        Assert.True(editor.DocumentHistoryEquals(history));
        AssertRect(originalBounds, entity.Bounds);
        editor.Redo();
        AssertRect(changedBounds, entity.Bounds);
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public void CancelAndLockDuringGripDragNeverCommit(TestEntityKind kind)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var grip = new CadHandleSceneBuilder().BuildSelectionHandles(editor.Document, [entity.Id]).OfType<CadGripHandle>().First();
        var scene = new CadHandleScene();
        scene.Replace([grip]);
        var controller = new CadGripDragController(new CadHandleHitTester());
        var committer = new CadGripDragCommitter(editor,
            new CadTextMeasurementService(editor.Document, context.Document.Direct2DImageRenderHost, editor.Viewport));
        var history = editor.CreateDocumentHistorySnapshot();
        Assert.True(controller.TryBegin(editor, scene, point => point, point => point, grip.Position));
        controller.UpdatePointer(point => point, grip.Position + new CadVectorD(7, 9));
        controller.Clear();
        Assert.False(controller.Commit(editor, committer, point => point, grip.Position));
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.True(controller.TryBegin(editor, scene, point => point, point => point, grip.Position));
        editor.SetLayerState(entity.LayerId, true, true, false);
        history = editor.CreateDocumentHistorySnapshot();
        Assert.False(controller.Commit(editor, committer, point => point, grip.Position + new CadVectorD(7, 9)));
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.False(controller.TryBegin(editor, scene, point => point, point => point, grip.Position));
    }

    [Fact]
    public void CenterGripMovesTheEditableSelectionAsOneUndoableOperation()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var first = CadEntityTestCases.Add(editor.Document, TestEntityKind.Rectangle);
        var second = CadEntityTestCases.Add(editor.Document, TestEntityKind.Circle);
        var lockedLayer = editor.Document.CreateLayer("Locked", CadColor.Red, CadLineWeight.Default);
        var locked = editor.Document.AddLine(CadPointD.Origin, new(10, 10), layerId: lockedLayer);
        editor.SetLayerState(lockedLayer, true, true, false);
        editor.Selection.Replace([first.Id, second.Id, locked.Id]);
        var grip = new CadHandleSceneBuilder().BuildSelectionHandles(editor.Document, [first.Id]).OfType<CadGripHandle>().Single(item => item.Type == CadHandleType.Center);
        var scene = new CadHandleScene();
        scene.Replace([grip]);
        var controller = new CadGripDragController(new CadHandleHitTester());
        var original = new[] { first, second, locked }.ToDictionary(entity => entity.Id, entity => entity.Bounds);
        Assert.True(controller.TryBegin(editor, scene, point => point, point => point, grip.Position));
        Assert.Equal(2, controller.HiddenEntityIds.Count);
        var delta = new CadVectorD(10, 20);
        Assert.True(controller.Commit(editor, new CadGripDragCommitter(editor,
            new CadTextMeasurementService(editor.Document, context.Document.Direct2DImageRenderHost, editor.Viewport)),
            point => point, grip.Position + delta));
        AssertRect(original[first.Id].Translate(delta), first.Bounds);
        AssertRect(original[second.Id].Translate(delta), second.Bounds);
        AssertRect(original[locked.Id], locked.Bounds);
        editor.Undo();
        AssertRect(original[first.Id], first.Bounds);
        AssertRect(original[second.Id], second.Bounds);
    }

    private static void AssertPoints(CadPointD[] expected, CadPointD[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(expected[i].NearEquals(actual[i]));
    }

    private static void AssertRect(CadRectD expected, CadRectD actual)
    {
        Assert.Equal(expected.Left, actual.Left, 6);
        Assert.Equal(expected.Top, actual.Top, 6);
        Assert.Equal(expected.Right, actual.Right, 6);
        Assert.Equal(expected.Bottom, actual.Bottom, 6);
    }
}
