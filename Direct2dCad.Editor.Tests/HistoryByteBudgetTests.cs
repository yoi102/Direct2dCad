using Direct2dCad.Editor.History;
using Direct2dCad.Editor.Commands;
namespace Direct2dCad.Editor.Tests;
public sealed class HistoryByteBudgetTests
{
    [Fact]public void ByteBudgetExpiresWholeOlderBatchesAndKeepsNewestEvenIfOversized()
    {
        var history=new CommandHistory<object>();var old=Guid.NewGuid();var current=Guid.NewGuid();
        history.PushExecuted(new(),old,400);history.PushExecuted(new(),old,400);var state=history.CreateUndoSnapshot();
        history.PushExecuted(new(),current,500);history.PushExecuted(new(),current,500);history.TrimUndo(0,900);
        Assert.Equal(2,history.UndoCount);Assert.Equal(1000,history.EstimatedRetainedBytes);
        history.CommitUndo(history.PeekUndo(CadCommandBatchUndoMode.Batch));Assert.True(history.UndoStackEquals(state));Assert.Equal(1000,history.EstimatedRetainedBytes);
        history.CommitRedo(history.PeekRedo(CadCommandBatchUndoMode.Batch));Assert.Equal(1000,history.EstimatedRetainedBytes);
        history.PushExecuted(new(),estimatedBytes:100);history.TrimUndo(0,900);Assert.Equal(1,history.UndoCount);Assert.Equal(100,history.EstimatedRetainedBytes);
    }
}
