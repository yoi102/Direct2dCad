using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.ViewModels.Services.Rendering;

namespace Direct2dCad.Tests;

public sealed class IncrementalSelectionGeometryTests
{
    [Fact]
    public void BulkGeometryAndLockChangesFallBackWithoutStaleAggregateGrip()
    {
        var document = CadDocument.Create("Batch selection");
        var lines = Enumerable.Range(0, 1000).Select(i => document.AddLine(new(i, i), new(i + 1, i + 1))).ToArray();
        var editor = new CadEditor(document);
        editor.Selection.Replace(lines.Select(line => line.Id));
        var coordinator = new CadOverlaySceneCoordinator();
        void Refresh() => coordinator.UpdateHandleScene(editor, null, CadHandleSceneBuildOptions.Default, 1);
        Refresh();
        coordinator.ApplyDocumentChanges(editor.Execute(new MoveEntitiesCommand(editor.Selection.EntityIds, new(50, 50))),
            editor.Selection.EntityIds);
        Refresh();
        Assert.Equal(new CadPointD(550, 550), coordinator.HandleScene.SelectionWorldBounds.Center);
        coordinator.ApplyDocumentChanges(editor.Execute(new SetEntityLockedCommand([lines[^1].Id], true)),
            editor.Selection.EntityIds);
        Refresh();
        var expected = lines.Take(lines.Length - 1).Aggregate(CadRectD.Empty, (bounds, line) => bounds.Union(line.Bounds));
        Assert.Equal(expected.Center, Assert.IsType<CadGripHandle>(Assert.Single(coordinator.HandleScene.NonSelectionItems)).Position);
        coordinator.ApplyDocumentChanges(editor.Undo(), editor.Selection.EntityIds);
        Refresh();
        Assert.Equal(coordinator.HandleScene.SelectionWorldBounds.Center,
            Assert.IsType<CadGripHandle>(Assert.Single(coordinator.HandleScene.NonSelectionItems)).Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleEditAndUndoKeepOrderAndUpdateAggregateBounds(bool locked)
    {
        var document = CadDocument.Create("Incremental selection");
        var lines = Enumerable.Range(0, 2000).Select(i => document.AddLine(new(i, i), new(i + 1, i + 1))).ToArray();
        lines[^1].SetLocked(locked);
        var editor = new CadEditor(document);
        editor.Selection.Replace(lines.Select(line => line.Id));
        var coordinator = new CadOverlaySceneCoordinator();
        var options = CadHandleSceneBuildOptions.Default;
        void Refresh() => coordinator.UpdateHandleScene(editor, null, options, 1);
        Refresh();
        var publishedList = coordinator.HandleScene.SelectionReferences;
        var unaffected = publishedList[1];
        var original = coordinator.HandleScene.SelectionWorldBounds;
        for (var i = 0; i < 3; i++)
        {
            var changes = editor.Execute(new MoveEntitiesCommand([lines[0].Id], new(-50, -50)));
            coordinator.ApplyDocumentChanges(changes, editor.Selection.EntityIds);
            Refresh();
            Assert.Same(publishedList, coordinator.HandleScene.SelectionReferences);
            Assert.Same(unaffected, publishedList[1]);
            Assert.Equal(lines[0].Bounds, publishedList[0].EntityBounds);
            var expected = lines.Aggregate(CadRectD.Empty, (bounds, line) => bounds.Union(line.Bounds));
            Assert.Equal(expected, coordinator.HandleScene.SelectionWorldBounds);
            var movableBounds = lines.Where(line => !line.IsLocked)
                .Aggregate(CadRectD.Empty, (bounds, line) => bounds.Union(line.Bounds));
            Assert.Equal(movableBounds.Center, Assert.IsType<CadGripHandle>(
                Assert.Single(coordinator.HandleScene.NonSelectionItems)).Position);
        }
        for (var i = 0; i < 3; i++)
        {
            coordinator.ApplyDocumentChanges(editor.Undo(), editor.Selection.EntityIds);
            Refresh();
        }
        Assert.Equal(original, coordinator.HandleScene.SelectionWorldBounds);
        coordinator.ApplyDocumentChanges(editor.Execute(new SetEntityZIndexCommand([lines[0].Id], 100)), editor.Selection.EntityIds);
        Refresh();
        Assert.Equal(lines[0].Id, coordinator.HandleScene.SelectionReferences[^1].EntityId);
    }

    [Fact]
    public void IncrementalSceneFallsBackForTranslatedOrIndividualGripScenes()
    {
        var document = CadDocument.Create("Fallback");
        var line = document.AddLine(new(0, 0), new(10, 10));
        var scene = new CadHandleScene();
        scene.Replace(new CadHandleSceneBuilder().BuildSelectionHandles(document, [line.Id]));
        Assert.False(scene.TryUpdateGeometry(document, [line.Id]));
        scene.Replace([new CadSelectionEntityReference(line.Id, line.Bounds, new(10, 10), CadHandleStyle.SelectionOutline)]);
        Assert.False(scene.TryUpdateGeometry(document, [line.Id]));
        scene.Clear();
        Assert.False(scene.TryUpdateGeometry(document, [line.Id]));
    }
}
