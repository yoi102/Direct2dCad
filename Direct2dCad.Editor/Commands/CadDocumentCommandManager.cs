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

    public event EventHandler<CadDocumentChangeSet>? DocumentChanged;
    public event EventHandler<CadCommandActivity>? Activity;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
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

        var result = command.Execute(_document);
        _history.PushExecuted(command);
        _changes.Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.DocumentChanged);
        return result;
    }

    public CadDocumentChangeSet ExecuteInBatch(ICadCommand command, Guid batchId)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch id cannot be empty.", nameof(batchId));

        var result = command.Execute(_document);
        _history.PushExecuted(command, batchId);
        _changes.Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.DocumentChanged);
        return result;
    }

    public CadDocumentChangeSet ExecuteRange(IEnumerable<ICadCommand> commands, string name = "Command Batch")
    {
        ArgumentNullException.ThrowIfNull(commands);

        var commandArray = commands.ToArray();
        if (commandArray.Length == 0)
            return CadDocumentChangeSet.Empty;

        var batchId = Guid.NewGuid();
        var results = new List<CadDocumentChangeSet>(commandArray.Length);

        foreach (var command in commandArray)
        {
            try
            {
                var result = command.Execute(_document);
                _history.PushExecuted(command, batchId);
                results.Add(result);
            }
            catch
            {
                _changes.Publish(CadDocumentChangeSet.Combine(results));
                throw;
            }
        }

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity(name, CadCommandActivityKind.Execute, commandArray.Length, combined.DocumentChanged);
        return combined;
    }

    public CadDocumentChangeSet Undo()
    {
        var entries = _history.PopUndo(_settings.UndoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Undo", CadCommandActivityKind.Undo, 0, false);
            return CadDocumentChangeSet.Empty;
        }

        var results = new List<CadDocumentChangeSet>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                var result = entry.Command.Undo(_document);
                _history.PushUndone(entry);
                results.Add(result);
            }
            catch
            {
                _changes.Publish(CadDocumentChangeSet.Combine(results));
                throw;
            }
        }

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Undo, entries.Count, combined.DocumentChanged);
        return combined;
    }

    public CadDocumentChangeSet UndoBatch(Guid batchId)
    {
        var entries = _history.PopUndoBatch(batchId);
        if (entries.Count == 0)
            return CadDocumentChangeSet.Empty;

        var results = new List<CadDocumentChangeSet>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                var result = entry.Command.Undo(_document);
                _history.PushUndone(entry);
                results.Add(result);
            }
            catch
            {
                _changes.Publish(CadDocumentChangeSet.Combine(results));
                throw;
            }
        }

        var combined = CadDocumentChangeSet.Combine(results);
        _changes.Publish(combined);
        PublishActivity("Cancel Command Batch", CadCommandActivityKind.Undo, entries.Count, combined.DocumentChanged);
        return combined;
    }

    public CadDocumentChangeSet Redo()
    {
        var entries = _history.PopRedo(_settings.RedoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Redo", CadCommandActivityKind.Redo, 0, false);
            return CadDocumentChangeSet.Empty;
        }

        var results = new List<CadDocumentChangeSet>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                var result = entry.Command.Execute(_document);
                _history.PushRedone(entry);
                results.Add(result);
            }
            catch
            {
                _changes.Publish(CadDocumentChangeSet.Combine(results));
                throw;
            }
        }

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
        Activity?.Invoke(this, new CadCommandActivity(
            name,
            kind,
            CadCommandActivityScope.Document,
            commandCount,
            hasChanges));
    }

    private static string GetActivityName(IReadOnlyList<CommandHistoryEntry<ICadCommand>> entries) =>
        entries.Count == 1 ? entries[0].Command.Name : "Command Batch";

}
