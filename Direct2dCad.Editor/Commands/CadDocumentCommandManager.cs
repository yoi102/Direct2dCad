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

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public CommandHistorySettings Settings => _settings;

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
            var result = command.Execute(_document);
            _history.PushExecuted(command, batchId);
            _changes.Publish(result);
            results.Add(result);
        }

        return Combine(results);
    }

    public CadDocumentChangeSet Undo()
    {
        var entries = _history.PopUndo(_settings.UndoMode);
        if (entries.Count == 0)
            return CadDocumentChangeSet.Empty;

        var results = new List<CadDocumentChangeSet>(entries.Count);
        foreach (var entry in entries)
        {
            var result = entry.Command.Undo(_document);
            _history.PushUndone(entry);
            _changes.Publish(result);
            results.Add(result);
        }

        return Combine(results);
    }

    public CadDocumentChangeSet Redo()
    {
        var entries = _history.PopRedo(_settings.RedoMode);
        if (entries.Count == 0)
            return CadDocumentChangeSet.Empty;

        var results = new List<CadDocumentChangeSet>(entries.Count);
        foreach (var entry in entries)
        {
            var result = entry.Command.Execute(_document);
            _history.PushRedone(entry);
            _changes.Publish(result);
            results.Add(result);
        }

        return Combine(results);
    }

    private static CadDocumentChangeSet Combine(IEnumerable<CadDocumentChangeSet> results)
    {
        var entityChanges = new List<CadEntityChange>();
        var structureChanged = false;
        var viewSettingsChanged = false;

        foreach (var result in results)
        {
            entityChanges.AddRange(result.EntityChanges);
            structureChanged |= result.AffectsDocumentStructure;
            viewSettingsChanged |= result.AffectsViewSettings;
        }

        var combined = new CadDocumentChangeSet(entityChanges);
        if (structureChanged)
            combined = combined.WithDocumentStructureChanged();

        if (viewSettingsChanged)
            combined = combined.WithViewSettingsChanged();

        return combined;
    }
}
