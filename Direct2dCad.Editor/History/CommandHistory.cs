using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.History;

public sealed class CommandHistory<TCommand>
    where TCommand : class
{
    private readonly Stack<CommandHistoryEntry<TCommand>> _undoStack = [];
    private readonly Stack<CommandHistoryEntry<TCommand>> _redoStack = [];

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public object CreateUndoSnapshot()
    {
        return _undoStack.ToArray();
    }

    public bool UndoStackEquals(object? snapshot)
    {
        if (snapshot is not CommandHistoryEntry<TCommand>[] entries ||
            entries.Length != _undoStack.Count)
        {
            return false;
        }

        var index = 0;
        foreach (var current in _undoStack)
        {
            var saved = entries[index++];
            if (!ReferenceEquals(current.Command, saved.Command) ||
                current.BatchId != saved.BatchId)
            {
                return false;
            }
        }

        return true;
    }

    public void PushExecuted(TCommand command, Guid? batchId = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        _undoStack.Push(new CommandHistoryEntry<TCommand>(command, batchId));
        _redoStack.Clear();
    }

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PopUndo(CadCommandBatchUndoMode mode)
    {
        return Pop(_undoStack, mode);
    }

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PopRedo(CadCommandBatchUndoMode mode)
    {
        return Pop(_redoStack, mode);
    }

    public void PushUndone(CommandHistoryEntry<TCommand> entry)
    {
        _redoStack.Push(entry);
    }

    public void PushRedone(CommandHistoryEntry<TCommand> entry)
    {
        _undoStack.Push(entry);
    }

    private static IReadOnlyList<CommandHistoryEntry<TCommand>> Pop(
        Stack<CommandHistoryEntry<TCommand>> stack,
        CadCommandBatchUndoMode mode)
    {
        if (!stack.TryPop(out var first))
            return [];

        var entries = new List<CommandHistoryEntry<TCommand>> { first };

        if (mode == CadCommandBatchUndoMode.Batch && first.BatchId is not null)
        {
            while (stack.TryPeek(out var next) && next.BatchId == first.BatchId)
                entries.Add(stack.Pop());
        }

        return entries;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

public readonly record struct CommandHistoryEntry<TCommand>(
    TCommand Command,
    Guid? BatchId)
    where TCommand : class;
