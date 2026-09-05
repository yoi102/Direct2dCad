using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.History;

public sealed class CommandHistory<TCommand>
    where TCommand : class
{
    private readonly HistoryDeque<CommandHistoryEntry<TCommand>> _undoStack = new();
    private HistoryDeque<CommandHistoryEntry<TCommand>> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    private object _baseState = new();

    // Tokens identify a document state without retaining command payloads.
    public object CreateUndoSnapshot() =>
        _undoStack.TryPeek(out var entry) ? entry.State : _baseState;

    public bool UndoStackEquals(object? snapshot) =>
        ReferenceEquals(snapshot, CreateUndoSnapshot());

    internal HistoryDeque<CommandHistoryEntry<TCommand>> CreateRedoSnapshot() => _redoStack;

    internal void RestoreRedoSnapshot(HistoryDeque<CommandHistoryEntry<TCommand>> entries) => _redoStack = entries;

    public void TrimUndo(int maximumCommandCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCommandCount);
        if (maximumCommandCount == 0)
            return;

        while (_undoStack.Count > maximumCommandCount)
        {
            _undoStack.TryPeek(out var newest);
            var oldest = _undoStack.Oldest;
            // A running AI batch must remain available for rollback.
            if (_undoStack.Count == 1 ||
                (oldest.BatchId is not null && oldest.BatchId == newest.BatchId))
                break;

            var batchId = oldest.BatchId;
            do
            {
                _baseState = _undoStack.RemoveOldest().State;
            }
            while (batchId is not null && _undoStack.Count > 0 &&
                   _undoStack.Oldest.BatchId == batchId);
        }
    }

    public void PushExecuted(TCommand command, Guid? batchId = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        _undoStack.Push(new CommandHistoryEntry<TCommand>(command, batchId));
        // Preserve a captured branch until an atomic caller commits or rolls back.
        if (_redoStack.Count > 0)
            _redoStack = new();
    }

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PopUndo(CadCommandBatchUndoMode mode)
    {
        return Pop(_undoStack, mode);
    }

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PeekUndo(CadCommandBatchUndoMode mode) =>
        Peek(_undoStack, mode);

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PopRedo(CadCommandBatchUndoMode mode)
    {
        return Pop(_redoStack, mode);
    }

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PeekRedo(CadCommandBatchUndoMode mode) =>
        Peek(_redoStack, mode);

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PopUndoBatch(Guid batchId)
        => PopUndoBatch(batchId, int.MaxValue);

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PopUndoBatch(Guid batchId, int maximumCount)
    {
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        if (batchId == Guid.Empty ||
            !_undoStack.TryPeek(out var first) ||
            first.BatchId != batchId)
        {
            return [];
        }

        var entries = new List<CommandHistoryEntry<TCommand>>();
        while (entries.Count < maximumCount &&
               _undoStack.TryPeek(out var next) &&
               next.BatchId == batchId)
            entries.Add(_undoStack.Pop());
        return entries;
    }

    public IReadOnlyList<CommandHistoryEntry<TCommand>> PeekUndoBatch(Guid batchId, int maximumCount = int.MaxValue)
    {
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (batchId == Guid.Empty)
            return [];

        var entries = new List<CommandHistoryEntry<TCommand>>();
        foreach (var entry in _undoStack)
        {
            if (entries.Count >= maximumCount || entry.BatchId != batchId)
                break;
            entries.Add(entry);
        }
        return entries;
    }

    public int CountUndoBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
            return 0;

        var count = 0;
        foreach (var entry in _undoStack)
        {
            if (entry.BatchId != batchId)
                break;
            count++;
        }

        return count;
    }

    public void CommitUndo(IReadOnlyList<CommandHistoryEntry<TCommand>> entries)
    {
        PopExpected(_undoStack, entries);
        foreach (var entry in entries)
            _redoStack.Push(entry);
    }

    public void CommitRedo(IReadOnlyList<CommandHistoryEntry<TCommand>> entries)
    {
        PopExpected(_redoStack, entries);
        foreach (var entry in entries)
            _undoStack.Push(entry);
    }

    public void DiscardUndo(IReadOnlyList<CommandHistoryEntry<TCommand>> entries) =>
        PopExpected(_undoStack, entries);

    public void PushUndone(CommandHistoryEntry<TCommand> entry)
    {
        _redoStack.Push(entry);
    }

    public void PushRedone(CommandHistoryEntry<TCommand> entry)
    {
        _undoStack.Push(entry);
    }

    private static IReadOnlyList<CommandHistoryEntry<TCommand>> Pop(
        HistoryDeque<CommandHistoryEntry<TCommand>> stack,
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

    private static IReadOnlyList<CommandHistoryEntry<TCommand>> Peek(
        HistoryDeque<CommandHistoryEntry<TCommand>> stack,
        CadCommandBatchUndoMode mode)
    {
        var entries = new List<CommandHistoryEntry<TCommand>>();
        foreach (var entry in stack)
        {
            if (entries.Count == 0)
            {
                entries.Add(entry);
                if (mode != CadCommandBatchUndoMode.Batch || entry.BatchId is null)
                    break;
                continue;
            }

            if (entry.BatchId != entries[0].BatchId)
            {
                break;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static void PopExpected(
        HistoryDeque<CommandHistoryEntry<TCommand>> stack,
        IReadOnlyList<CommandHistoryEntry<TCommand>> entries)
    {
        if (entries.Count > stack.Count)
            throw new InvalidOperationException("Command history changed while an operation was being committed.");

        using var enumerator = stack.GetEnumerator();
        for (var index = 0; index < entries.Count; index++)
        {
            if (!enumerator.MoveNext())
                throw new InvalidOperationException("Command history changed while an operation was being committed.");
            var actual = enumerator.Current;
            var expected = entries[index];
            if (!ReferenceEquals(actual.Command, expected.Command) ||
                actual.BatchId != expected.BatchId)
            {
                throw new InvalidOperationException("Command history changed while an operation was being committed.");
            }
        }

        foreach (var expected in entries)
        {
            _ = stack.Pop();
        }
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
    where TCommand : class
{
    internal object State { get; init; } = new object();
}
