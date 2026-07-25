using Direct2dCad.Editor.Commands;
using Direct2dCad.Editor.History;

namespace Direct2dCad.Editor.Tests;

public sealed class CommandHistoryTests
{
    [Fact]
    public void PopUndo_BatchModePopsAdjacentEntriesWithSameBatchId()
    {
        var history = new CommandHistory<object>();
        var first = new object();
        var second = new object();
        var outsideBatch = new object();
        var batchId = Guid.NewGuid();
        history.PushExecuted(outsideBatch);
        history.PushExecuted(first, batchId);
        history.PushExecuted(second, batchId);

        var entries = history.PopUndo(CadCommandBatchUndoMode.Batch);

        Assert.Equal([second, first], entries.Select(x => x.Command));
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void PopUndo_IndividualModePopsOnlyLatestBatchEntry()
    {
        var history = new CommandHistory<object>();
        var batchId = Guid.NewGuid();
        history.PushExecuted(new object(), batchId);
        history.PushExecuted(new object(), batchId);

        var entries = history.PopUndo(CadCommandBatchUndoMode.StepByStep);

        Assert.Single(entries);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void PushExecuted_AfterUndoClearsRedoStack()
    {
        var history = new CommandHistory<object>();
        history.PushExecuted(new object());
        var undone = Assert.Single(history.PopUndo(CadCommandBatchUndoMode.StepByStep));
        history.PushUndone(undone);
        Assert.True(history.CanRedo);

        history.PushExecuted(new object());

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoSnapshotDetectsHistoryChanges()
    {
        var history = new CommandHistory<object>();
        history.PushExecuted(new object());
        var snapshot = history.CreateUndoSnapshot();

        Assert.True(history.UndoStackEquals(snapshot));

        history.PushExecuted(new object());

        Assert.False(history.UndoStackEquals(snapshot));
    }
}
