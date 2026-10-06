using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.Tests;

public sealed class EditorHistoryBudgetTests
{
    [Fact]
    public void EditorCommandCountLimitKeepsOnlyTheLatestPanAndLeavesDocumentHistoryIndependent()
    {
        var editor = new CadEditor(CadDocument.Create("History"));
        editor.EditorHistorySettings.MaximumUndoCommands = 1;
        var id = editor.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        for (var index = 0; index < 5; index++)
            editor.Execute(new PanViewportCommand(new CadVectorD(1, 0)));

        editor.UndoEditor();

        Assert.Equal(new CadPointD(4, 0), editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
        Assert.True(editor.DocumentCommands.CanUndo);
        editor.RedoEditor();
        Assert.Equal(new CadPointD(5, 0), editor.Viewport.Offset);
        editor.UndoDocument();
        Assert.True(editor.Document.GetEntity(id).IsErased);
    }

    [Fact]
    public void PrivateBaseSelectionSnapshotCountsAgainstEditorByteBudget()
    {
        var editor = new CadEditor(CadDocument.Create("Selection"));
        var ids = Enumerable.Range(1, 4096).Select(value => new EntityId(value)).ToArray();
        editor.Selection.Replace(ids);
        editor.EditorHistorySettings.MaximumUndoBytes = 1024;

        editor.Execute(new ClearSelectionCommand());

        Assert.True(editor.EditorCommands.EstimatedHistoryBytes >= ids.Length * sizeof(int));
        editor.UndoEditor();
        Assert.Equal(ids.Length, editor.Selection.EntityIds.Count);
        editor.RedoEditor();
        editor.Execute(new PanViewportCommand(new CadVectorD(1, 0)));
        Assert.True(editor.EditorCommands.EstimatedHistoryBytes < 1024);
        editor.UndoEditor();
        Assert.False(editor.EditorCommands.CanUndo);
        Assert.Empty(editor.Selection.EntityIds);
    }

    [Fact]
    public void NewestEditorBatchRemainsWholeUntilTheNextCommand()
    {
        var editor = new CadEditor(CadDocument.Create("Batch"));
        editor.EditorHistorySettings.MaximumUndoCommands = 1;
        editor.ExecuteRange(new ICadEditorCommand[]
        {
            new PanViewportCommand(new CadVectorD(2, 0)),
            new PanViewportCommand(new CadVectorD(3, 0))
        });

        editor.UndoEditor();
        Assert.Equal(CadPointD.Origin, editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
        editor.RedoEditor();
        editor.Execute(new PanViewportCommand(new CadVectorD(4, 0)));
        editor.UndoEditor();
        Assert.Equal(new CadPointD(5, 0), editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
    }
}
