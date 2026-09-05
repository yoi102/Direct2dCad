using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadLayoutViewportPanControllerTests
{
    [Fact]
    public void MultipleMovesCommitOneUndoableCommandWithoutMovingPaperView()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var viewport = editor.Document.GetLayout(LayoutId.Default).Viewports[0];
        var initial = CadLayoutViewportSnapshot.From(viewport);
        var paperOffset = editor.Viewport.Offset;
        var controller = new CadLayoutViewportPanController();
        Assert.True(controller.Begin(editor, LayoutId.Default, viewport, CadPointD.Origin));

        Assert.True(controller.Move(new CadPointD(20, 10)));
        Assert.True(controller.Move(new CadPointD(40, 30)));
        var target = CadLayoutViewportSnapshot.From(viewport);
        Assert.NotEqual(initial, target);
        Assert.Equal(0, editor.DocumentChangeVersion);
        Assert.Equal(paperOffset, editor.Viewport.Offset);
        Assert.True(controller.End());
        Assert.False(controller.IsPanning);
        Assert.Equal(1, editor.DocumentChangeVersion);
        Assert.Equal(target, CadLayoutViewportSnapshot.From(viewport));

        editor.Undo();
        Assert.Equal(initial, CadLayoutViewportSnapshot.From(viewport));
        editor.Redo();
        Assert.Equal(target, CadLayoutViewportSnapshot.From(viewport));
        Assert.False(controller.End());
    }

    [Theory]
    [InlineData(1, 1, 0, -40, 20)]
    [InlineData(2, 4, 0, -5, 2.5)]
    [InlineData(2, 4, 90, 2.5, 5)]
    public void MoveConvertsScreenDeltaUsingPaperZoomAndModelScaleAndRotation(
        double zoom, double scale, double angle, double expectedX, double expectedY)
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        editor.Viewport.SetView(zoom, new CadPointD(150, 200));
        var viewport = editor.Document.GetLayout(LayoutId.Default).Viewports[0];
        viewport.SetView(viewport.Bounds, CadPointD.Origin, scale, angle * Math.PI / 180);
        var controller = new CadLayoutViewportPanController();
        controller.Begin(editor, LayoutId.Default, viewport, new CadPointD(100, 100));

        controller.Move(new CadPointD(140, 120));

        Assert.Equal(expectedX, viewport.ModelCenter.X, 8);
        Assert.Equal(expectedY, viewport.ModelCenter.Y, 8);
        controller.Cancel();
        Assert.Equal(CadPointD.Origin, viewport.ModelCenter);
    }

    [Fact]
    public void LockedViewportCannotBeginAndLockDuringDragCancelsWithoutUnlocking()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var viewport = editor.Document.GetLayout(LayoutId.Default).Viewports[0];
        var initialCenter = viewport.ModelCenter;
        var controller = new CadLayoutViewportPanController();
        viewport.SetLocked(true);
        Assert.False(controller.Begin(editor, LayoutId.Default, viewport, CadPointD.Origin));
        viewport.SetLocked(false);
        controller.Begin(editor, LayoutId.Default, viewport, CadPointD.Origin);
        controller.Move(new CadPointD(20, 10));
        viewport.SetLocked(true);

        Assert.False(controller.Move(new CadPointD(40, 20)));
        Assert.False(controller.IsPanning);
        Assert.True(viewport.IsLocked);
        Assert.Equal(initialCenter, viewport.ModelCenter);
        Assert.Equal(0, editor.DocumentChangeVersion);
    }

    [Fact]
    public void NoMovementAndReturningToStartDoNotCreateHistoryEntries()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var viewport = editor.Document.GetLayout(LayoutId.Default).Viewports[0];
        var controller = new CadLayoutViewportPanController();
        controller.Begin(editor, LayoutId.Default, viewport, CadPointD.Origin);
        Assert.False(controller.Move(CadPointD.Origin));
        Assert.False(controller.End());
        controller.Begin(editor, LayoutId.Default, viewport, CadPointD.Origin);
        controller.Move(new CadPointD(20, 10));
        controller.Move(CadPointD.Origin);
        Assert.False(controller.End());
        Assert.Equal(0, editor.DocumentChangeVersion);
    }

    [Fact]
    public void RemovedViewportDoesNotCommitToAnotherViewport()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var layout = editor.Document.GetLayout(LayoutId.Default);
        var viewport = layout.Viewports[0];
        var original = viewport.ModelCenter;
        var controller = new CadLayoutViewportPanController();
        controller.Begin(editor, layout.Id, viewport, CadPointD.Origin);
        controller.Move(new CadPointD(20, 10));
        editor.RemoveLayoutViewport(layout.Id, viewport.Id);
        var version = editor.DocumentChangeVersion;

        Assert.False(controller.End());
        Assert.Equal(version, editor.DocumentChangeVersion);
        Assert.Equal(original, viewport.ModelCenter);
        Assert.False(controller.IsPanning);
    }

    [Fact]
    public void ViewportCreationAndPanShareTheRequestedUndoBatch()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var batchId = Guid.NewGuid();
        var id = editor.AddLayoutViewport(LayoutId.Default, CadRectD.FromXYWH(10, 10, 80, 60),
            CadPointD.Origin, 1, 0, batchId);
        var viewport = editor.Document.GetLayout(LayoutId.Default).GetViewport(id);
        var controller = new CadLayoutViewportPanController();
        controller.Begin(editor, LayoutId.Default, viewport, CadPointD.Origin, batchId);
        controller.Move(new CadPointD(20, 10));
        Assert.True(controller.End());

        editor.UndoBatch(batchId);
        Assert.DoesNotContain(editor.Document.GetLayout(LayoutId.Default).Viewports, v => v.Id == id);
    }
}
