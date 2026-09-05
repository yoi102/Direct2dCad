using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.Tests;

public sealed class AtomicCommandBatchTests
{
    [Fact]
    public void ReadOnlyAtomicCallDoesNotTrimHistoryOrDiscardRedo()
    {
        var editor = new CadEditor(CadDocument.Create("Query"));
        var id = editor.AddLine(new(0, 0), new(10, 10));
        editor.SetEntityZIndex(id, 1);
        editor.SetEntityZIndex(id, 2);
        editor.Undo();
        editor.DocumentCommands.Settings.MaximumUndoCommands = 1;
        Assert.Equal(42, editor.DocumentCommands.ExecuteAtomicBatch(Guid.NewGuid(), () => 42));
        Assert.True(editor.DocumentCommands.CanRedo);
        editor.Undo();
        Assert.True(editor.DocumentCommands.CanUndo);
        editor.Undo();
        Assert.True(editor.Document.GetEntity(id).IsErased);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailurePreservesUndoRedoAndPublishesRestoredAvailability(int retention)
    {
        var editor = new CadEditor(CadDocument.Create("Atomic"));
        var id = editor.AddLine(new(0, 0), new(10, 10));
        editor.SetEntityZIndex(id, 1);
        editor.SetEntityZIndex(id, 2);
        editor.Undo();
        editor.Undo();
        var snapshot = editor.CreateDocumentHistorySnapshot();
        var manager = editor.DocumentCommands;
        manager.Settings.MaximumUndoCommands = retention;
        var batch = Guid.NewGuid();
        var availabilityOnRollback = false;
        manager.Activity += (_, activity) =>
        {
            if (activity.Kind == CadCommandActivityKind.Undo)
                availabilityOnRollback = manager.CanRedo;
        };

        Assert.Throws<ArgumentException>(() => manager.ExecuteAtomicBatch<int>(batch, () =>
        {
            manager.ExecuteInBatch(new SetEntityZIndexCommand([id], 42), batch);
            manager.ExecuteInBatch(new SetEntityZIndexCommand([id], 43), batch);
            throw new ArgumentException("Invalid later property");
        }));

        Assert.True(manager.IsHistoryHealthy);
        Assert.True(availabilityOnRollback);
        Assert.True(editor.DocumentHistoryEquals(snapshot));
        Assert.Equal(0, editor.Document.GetEntity(id).ZIndex);
        editor.Redo();
        Assert.Equal(1, editor.Document.GetEntity(id).ZIndex);
        editor.Redo();
        Assert.Equal(2, editor.Document.GetEntity(id).ZIndex);
    }

    [Fact]
    public void FailedCallDoesNotRollbackEarlierSuccessInTheSameBatch()
    {
        var editor = new CadEditor(CadDocument.Create("Batch"));
        var id = editor.AddLine(new(0, 0), new(10, 10));
        var batch = Guid.NewGuid();
        var manager = editor.DocumentCommands;
        manager.ExecuteAtomicBatch(batch, () => manager.ExecuteInBatch(new SetEntityZIndexCommand([id], 1), batch));
        var saved = editor.CreateDocumentHistorySnapshot();
        Assert.Throws<ArgumentException>(() => manager.ExecuteAtomicBatch<int>(batch, () =>
        {
            manager.ExecuteInBatch(new SetEntityZIndexCommand([id], 2), batch);
            throw new ArgumentException();
        }));
        Assert.True(editor.DocumentHistoryEquals(saved));
        Assert.Equal(1, editor.Document.GetEntity(id).ZIndex);
        editor.Undo();
        Assert.Equal(0, editor.Document.GetEntity(id).ZIndex);
        editor.Redo();
        Assert.Equal(1, editor.Document.GetEntity(id).ZIndex);
    }

    [Theory]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("unbatched")]
    [InlineData("different_batch")]
    [InlineData("nested")]
    public void HistoryReentryIsRejectedWithoutChangingTheDocument(string operation)
    {
        var editor = new CadEditor(CadDocument.Create("Reentry"));
        var id = editor.AddLine(new(0, 0), new(10, 10));
        var manager = editor.DocumentCommands;
        var snapshot = editor.CreateDocumentHistorySnapshot();
        var batch = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => manager.ExecuteAtomicBatch<object>(batch, () => operation switch
        {
            "undo" => manager.Undo(),
            "redo" => manager.Redo(),
            "unbatched" => manager.Execute(new SetEntityZIndexCommand([id], 5)),
            "different_batch" => manager.ExecuteInBatch(new SetEntityZIndexCommand([id], 5), Guid.NewGuid()),
            _ => manager.ExecuteAtomicBatch(batch, () => 1)
        }));
        Assert.True(editor.DocumentHistoryEquals(snapshot));
        Assert.Equal(0, editor.Document.GetEntity(id).ZIndex);
        Assert.True(manager.IsHistoryHealthy);
    }
}
