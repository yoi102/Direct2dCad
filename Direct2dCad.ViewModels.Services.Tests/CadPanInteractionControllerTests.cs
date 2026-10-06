using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadPanInteractionControllerTests
{
    [Fact]
    public void DragPublishesEveryMoveButRetainsOneUndoEntry()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        editor.Viewport.SetView(2, new CadPointD(100, 200));
        var controller = new CadPanInteractionController();
        var updates = 0;
        editor.EditorStateChanged += (_, _) => updates++;
        controller.Begin(CadPointD.Origin);

        for (var index = 1; index <= 100; index++)
            Assert.True(controller.Move(editor, new CadPointD(index, index * 2)));

        Assert.Equal(100, updates);
        Assert.True(controller.End());
        Assert.False(controller.End());
        Assert.Equal(new CadPointD(200, 400), editor.Viewport.Offset);
        editor.UndoEditor();
        Assert.Equal(new CadPointD(100, 200), editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
        editor.RedoEditor();
        Assert.Equal(new CadPointD(200, 400), editor.Viewport.Offset);
        Assert.Equal(2, editor.Viewport.Zoom);
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    [Fact]
    public void SeparateGesturesStaySeparateAndRespectRetention()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        editor.EditorHistorySettings.MaximumUndoCommands = 1;
        var controller = new CadPanInteractionController();
        for (var gesture = 0; gesture < 2; gesture++)
        {
            controller.Begin(CadPointD.Origin);
            controller.Move(editor, new CadPointD(10, 0));
            controller.Move(editor, new CadPointD(20, 0));
            controller.End();
        }

        editor.UndoEditor();
        Assert.Equal(new CadPointD(20, 0), editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
    }

    [Fact]
    public void AnotherEditorCommandSplitsCoalescingWithoutChangingUndoOrder()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var controller = new CadPanInteractionController();
        controller.Begin(CadPointD.Origin);
        controller.Move(editor, new CadPointD(10, 0));
        editor.Execute(new ClearSelectionCommand());
        controller.Move(editor, new CadPointD(20, 0));
        controller.End();

        editor.UndoEditor();
        Assert.Equal(new CadPointD(10, 0), editor.Viewport.Offset);
        editor.UndoEditor();
        Assert.Equal(new CadPointD(10, 0), editor.Viewport.Offset);
        editor.UndoEditor();
        Assert.Equal(CadPointD.Origin, editor.Viewport.Offset);
    }

    [Fact]
    public void StationaryGestureDoesNotCreateHistory()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var controller = new CadPanInteractionController();
        controller.Begin(CadPointD.Origin);
        Assert.False(controller.Move(editor, CadPointD.Origin));
        Assert.False(controller.End());
        Assert.False(editor.EditorCommands.CanUndo);
    }

    [Fact]
    public void UndoThenContinuingTheGestureStartsANewBranch()
    {
        var editor = new CadEditor(CadDocument.Create("Pan"));
        var controller = new CadPanInteractionController();
        controller.Begin(CadPointD.Origin);
        controller.Move(editor, new CadPointD(10, 0));
        editor.UndoEditor();
        Assert.True(editor.EditorCommands.CanRedo);

        controller.Move(editor, new CadPointD(15, 0));
        controller.End();

        Assert.Equal(new CadPointD(5, 0), editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanRedo);
        editor.UndoEditor();
        Assert.Equal(CadPointD.Origin, editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
    }
}
