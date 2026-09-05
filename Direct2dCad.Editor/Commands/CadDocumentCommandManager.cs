using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor.History;

namespace Direct2dCad.Editor.Commands;

public sealed class CadDocumentCommandManager
{
    private readonly CadDocument _document;
    private readonly CadDocumentChangeDispatcher _changes;
    private readonly CommandHistory<ICadCommand> _history;
    private readonly CommandHistorySettings _settings;
    private Exception? _historyRecoveryFailure;
    private Guid? _atomicBatchId;
    private CadCommandActivity? _atomicActivity;

    public event EventHandler<CadDocumentChangeSet>? DocumentChanged;
    public event EventHandler<CadCommandActivity>? Activity;

    public bool CanUndo => _historyRecoveryFailure is null && _history.CanUndo;
    public bool CanRedo => _historyRecoveryFailure is null && _history.CanRedo;
    public bool IsHistoryHealthy => _historyRecoveryFailure is null;
    public CommandHistorySettings Settings => _settings;
    public object CreateUndoHistorySnapshot() => _history.CreateUndoSnapshot();
    public bool UndoHistoryEquals(object? snapshot) => _history.UndoStackEquals(snapshot);

    public CadDocumentCommandManager(
        CadDocument document,
        CadDocumentChangeDispatcher changes,
        CommandHistory<ICadCommand>? history = null,
        CommandHistorySettings? settings = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _history = history ?? new CommandHistory<ICadCommand>();
        _settings = settings ?? new CommandHistorySettings();
        _changes.DocumentChanged += (_, result) => DocumentChanged?.Invoke(this, result);
    }

    public CadDocumentChangeSet Execute(ICadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureOutsideAtomicBatch();
        EnsureHistoryHealthy();

        var result = command.Execute(_document);
        _history.PushExecuted(command);
        _history.TrimUndo(_settings.MaximumUndoCommands);
        _changes.Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.DocumentChanged);
        return result;
    }

    public CadDocumentChangeSet ExecuteInBatch(ICadCommand command, Guid batchId)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureHistoryHealthy();
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch id cannot be empty.", nameof(batchId));
        if (_atomicBatchId is { } activeBatchId && activeBatchId != batchId)
            throw new InvalidOperationException("An atomic operation cannot execute a different command batch.");

        var result = command.Execute(_document);
        _history.PushExecuted(command, batchId);
        if (_atomicBatchId is null)
            _history.TrimUndo(_settings.MaximumUndoCommands);
        _changes.Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.DocumentChanged);
        return result;
    }

    /// <summary>
    /// Runs a synchronous tool operation whose document changes use ExecuteInBatch.
    /// Failure reverses only this operation and restores the previous redo branch.
    /// </summary>
    public T ExecuteAtomicBatch<T>(Guid batchId, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureHistoryHealthy();
        EnsureOutsideAtomicBatch();
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch id cannot be empty.", nameof(batchId));

        var undoCount = _history.UndoCount;
        var redo = _history.CreateRedoSnapshot();
        var updates = _changes.DeferUpdates();
        _atomicBatchId = batchId;
        try
        {
            var result = operation();
            if (_history.UndoCount > undoCount)
                _history.TrimUndo(_settings.MaximumUndoCommands);
            return result;
        }
        catch
        {
            var executedCount = _history.UndoCount - undoCount;
            _history.RestoreRedoSnapshot(redo);
            if (executedCount > 0)
            {
                try
                {
                    RollbackBatch(batchId, executedCount);
                }
                catch (Exception recoveryFailure)
                {
                    MarkHistoryUnhealthy(recoveryFailure);
                    throw;
                }
            }
            throw;
        }
        finally
        {
            _atomicBatchId = null;
            var activity = _atomicActivity;
            _atomicActivity = null;
            updates.Dispose();
            if (activity is not null)
                Activity?.Invoke(this, activity);
        }
    }

    public CadDocumentChangeSet ExecuteRange(IEnumerable<ICadCommand> commands, string name = "Command Batch")
    {
        ArgumentNullException.ThrowIfNull(commands);
        EnsureOutsideAtomicBatch();
        EnsureHistoryHealthy();

        var commandArray = commands.ToArray();
        if (commandArray.Length == 0)
            return CadDocumentChangeSet.Empty;

        var batchId = Guid.NewGuid();
        var results = new List<CadDocumentChangeSet>(commandArray.Length);
        var executed = new List<ICadCommand>(commandArray.Length);

        try
        {
            foreach (var command in commandArray)
            {
                var result = command.Execute(_document);
                results.Add(result);
                executed.Add(command);
            }
        }
        catch (Exception originalException)
        {
            for (var index = executed.Count - 1; index >= 0; index--)
            {
                try
                {
                    executed[index].Undo(_document);
                }
                catch (Exception rollbackException)
                {
                    var failure = new InvalidOperationException(
                        "Command batch failed and could not be rolled back completely.",
                        new AggregateException(originalException, rollbackException));
                    MarkHistoryUnhealthy(failure);
                    throw failure;
                }
            }

            throw;
        }

        foreach (var command in commandArray)
        {
            _history.PushExecuted(command, batchId);
        }

        _history.TrimUndo(_settings.MaximumUndoCommands);
        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity(name, CadCommandActivityKind.Execute, commandArray.Length, combined.DocumentChanged);
        return combined;
    }

    /// <summary>
    /// Removes and reverses the latest commands of a batch without putting them
    /// on the redo stack. This is used by higher-level tool transactions when a
    /// command-dependent operation fails after some commands have executed.
    /// </summary>
    public CadDocumentChangeSet RollbackBatch(Guid batchId, int commandCount)
    {
        EnsureHistoryHealthy();
        if (commandCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(commandCount));
        var entries = _history.PeekUndoBatch(batchId, commandCount);
        if (entries.Count != commandCount)
            throw new InvalidOperationException("The command batch no longer contains the expected commands.");

        var results = UndoEntriesAtomically(entries);
        _history.DiscardUndo(entries);

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity("Rollback Command Batch", CadCommandActivityKind.Undo, entries.Count, combined.DocumentChanged);
        return combined;
    }

    public CadDocumentChangeSet Undo()
    {
        EnsureOutsideAtomicBatch();
        EnsureHistoryHealthy();
        var entries = _history.PeekUndo(_settings.UndoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Undo", CadCommandActivityKind.Undo, 0, false);
            return CadDocumentChangeSet.Empty;
        }

        var results = UndoEntriesAtomically(entries);
        _history.CommitUndo(entries);

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Undo, entries.Count, combined.DocumentChanged);
        return combined;
    }

    public CadDocumentChangeSet UndoBatch(Guid batchId)
    {
        EnsureOutsideAtomicBatch();
        EnsureHistoryHealthy();
        var entries = _history.PeekUndoBatch(batchId);
        if (entries.Count == 0)
            return CadDocumentChangeSet.Empty;

        var results = UndoEntriesAtomically(entries);
        _history.CommitUndo(entries);

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity("Cancel Command Batch", CadCommandActivityKind.Undo, entries.Count, combined.DocumentChanged);
        return combined;
    }

    public CadDocumentChangeSet Redo()
    {
        EnsureOutsideAtomicBatch();
        EnsureHistoryHealthy();
        var entries = _history.PeekRedo(_settings.RedoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Redo", CadCommandActivityKind.Redo, 0, false);
            return CadDocumentChangeSet.Empty;
        }

        var results = RedoEntriesAtomically(entries);
        _history.CommitRedo(entries);

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Redo, entries.Count, combined.DocumentChanged);
        return combined;
    }

    private void PublishActivity(
        string name,
        CadCommandActivityKind kind,
        int commandCount,
        bool hasChanges)
    {
        if (_atomicBatchId is not null)
        {
            var previous = kind == CadCommandActivityKind.Execute ? _atomicActivity : null;
            _atomicActivity = new CadCommandActivity(
                previous is null ? name : "Command Batch", kind,
                CadCommandActivityScope.Document, (previous?.CommandCount ?? 0) + commandCount,
                hasChanges || previous?.HasChanges == true);
            return;
        }
        Activity?.Invoke(this, new CadCommandActivity(
            name,
            kind,
            CadCommandActivityScope.Document,
            commandCount,
            hasChanges));
    }

    private static string GetActivityName(IReadOnlyList<CommandHistoryEntry<ICadCommand>> entries) =>
        entries.Count == 1 ? entries[0].Command.Name : "Command Batch";

    private List<CadDocumentChangeSet> UndoEntriesAtomically(
        IReadOnlyList<CommandHistoryEntry<ICadCommand>> entries)
    {
        var results = new List<CadDocumentChangeSet>(entries.Count);
        try
        {
            foreach (var entry in entries)
                results.Add(entry.Command.Undo(_document));
        }
        catch (Exception exception)
        {
            RestoreExecutedEntries(entries, results.Count, exception);
            throw;
        }

        return results;
    }

    private List<CadDocumentChangeSet> RedoEntriesAtomically(
        IReadOnlyList<CommandHistoryEntry<ICadCommand>> entries)
    {
        var results = new List<CadDocumentChangeSet>(entries.Count);
        try
        {
            foreach (var entry in entries)
                results.Add(entry.Command.Execute(_document));
        }
        catch (Exception exception)
        {
            RestoreUndoneEntries(entries, results.Count, exception);
            throw;
        }

        return results;
    }

    private void RestoreExecutedEntries(
        IReadOnlyList<CommandHistoryEntry<ICadCommand>> entries,
        int count,
        Exception originalException)
    {
        try
        {
            for (var index = count - 1; index >= 0; index--)
                _ = entries[index].Command.Execute(_document);
        }
        catch (Exception restoreException)
        {
            var failure = new InvalidOperationException(
                "Undo failed and the document could not be restored.",
                new AggregateException(originalException, restoreException));
            MarkHistoryUnhealthy(failure);
            throw failure;
        }
    }

    private void RestoreUndoneEntries(
        IReadOnlyList<CommandHistoryEntry<ICadCommand>> entries,
        int count,
        Exception originalException)
    {
        try
        {
            for (var index = count - 1; index >= 0; index--)
                _ = entries[index].Command.Undo(_document);
        }
        catch (Exception restoreException)
        {
            var failure = new InvalidOperationException(
                "Redo failed and the document could not be restored.",
                new AggregateException(originalException, restoreException));
            MarkHistoryUnhealthy(failure);
            throw failure;
        }
    }

    private void EnsureHistoryHealthy()
    {
        if (_historyRecoveryFailure is null)
            return;

        throw new InvalidOperationException(
            "The document command history is unavailable because a previous recovery failed.",
            _historyRecoveryFailure);
    }

    private void EnsureOutsideAtomicBatch()
    {
        if (_atomicBatchId is not null)
            throw new InvalidOperationException("Only commands in the current batch can run inside an atomic operation.");
    }

    private void MarkHistoryUnhealthy(Exception failure) =>
        _historyRecoveryFailure ??= failure;

}
