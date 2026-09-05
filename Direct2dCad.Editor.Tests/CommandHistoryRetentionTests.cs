using Direct2dCad.Editor.Commands;
using Direct2dCad.Editor.History;

namespace Direct2dCad.Editor.Tests;

public sealed class CommandHistoryRetentionTests
{
    [Fact]
    public void SnapshotSurvivesUndoRedoButNotANewBranch()
    {
        var history = new CommandHistory<object>();
        var empty = history.CreateUndoSnapshot();
        var command = new object();
        history.PushExecuted(command);
        var saved = history.CreateUndoSnapshot();
        var entries = history.PeekUndo(CadCommandBatchUndoMode.StepByStep);
        history.CommitUndo(entries);
        Assert.True(history.UndoStackEquals(empty));
        history.CommitRedo(history.PeekRedo(CadCommandBatchUndoMode.StepByStep));
        Assert.True(history.UndoStackEquals(saved));
        history.CommitUndo(history.PeekUndo(CadCommandBatchUndoMode.StepByStep));
        history.PushExecuted(command);
        Assert.False(history.UndoStackEquals(saved));
    }

    [Fact]
    public void TrimRemovesCompleteOldBatchesAndPreservesTheSavedState()
    {
        var history = new CommandHistory<object>();
        var batch = Guid.NewGuid();
        history.PushExecuted(new object(), batch);
        history.PushExecuted(new object(), batch);
        var beforeRetained = history.CreateUndoSnapshot();
        history.PushExecuted(new object());
        history.PushExecuted(new object());
        var saved = history.CreateUndoSnapshot();

        history.TrimUndo(3);

        Assert.Equal(2, history.UndoCount);
        Assert.True(history.UndoStackEquals(saved));
        for (var i = 0; i < 2; i++)
            history.CommitUndo(history.PeekUndo(CadCommandBatchUndoMode.StepByStep));
        Assert.True(history.UndoStackEquals(beforeRetained));
        for (var i = 0; i < 2; i++)
            history.CommitRedo(history.PeekRedo(CadCommandBatchUndoMode.StepByStep));
        Assert.True(history.UndoStackEquals(saved));
    }

    [Fact]
    public void LatestOversizedBatchIsKeptForRollback()
    {
        var history = new CommandHistory<object>();
        history.PushExecuted(new object());
        var batch = Guid.NewGuid();
        for (var i = 0; i < 1000; i++)
        {
            history.PushExecuted(new object(), batch);
            history.TrimUndo(2);
        }
        Assert.Equal(1000, history.CountUndoBatch(batch));
        Assert.Equal(1000, history.UndoCount);

        history.PushExecuted(new object());
        history.TrimUndo(2);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void UnlimitedRetentionAndDequeCompactionPreserveOrder()
    {
        var history = new CommandHistory<string>();
        for (var i = 0; i < 2000; i++)
        {
            history.PushExecuted(i.ToString());
            history.TrimUndo(10);
        }
        Assert.Equal(10, history.UndoCount);
        history.TrimUndo(0);
        for (var i = 1999; i >= 1990; i--)
            Assert.Equal(i.ToString(), Assert.Single(history.PopUndo(CadCommandBatchUndoMode.StepByStep)).Command);
    }

    [Fact]
    public void CommitValidatesTheEntirePrefixBeforeMutatingHistory()
    {
        var history = new CommandHistory<object>();
        var batch = Guid.NewGuid();
        history.PushExecuted(new object(), batch);
        history.PushExecuted(new object(), batch);
        var entries = history.PeekUndo(CadCommandBatchUndoMode.Batch).ToArray();
        entries[1] = new CommandHistoryEntry<object>(new object(), batch);
        Assert.Throws<InvalidOperationException>(() => history.CommitUndo(entries));
        Assert.Equal(2, history.UndoCount);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void SnapshotDoesNotRetainTheHistoryOrCommandPayloads()
    {
        var (snapshot, command) = CreateSnapshot();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(command.IsAlive);
        GC.KeepAlive(snapshot);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (object Snapshot, WeakReference Command) CreateSnapshot()
    {
        var history = new CommandHistory<object>();
        var command = new object();
        history.PushExecuted(command);
        return (history.CreateUndoSnapshot(), new WeakReference(command));
    }
}
