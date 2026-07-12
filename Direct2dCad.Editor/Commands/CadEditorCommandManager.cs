using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor.History;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Commands;

public sealed class CadEditorCommandManager
{
    private readonly CadEditorCommandContext _context;
    private readonly CadDocumentChangeDispatcher _documentChanges;
    private readonly CommandHistory<ICadEditorCommand> _history;
    private readonly CommandHistorySettings _settings;

    public event EventHandler<CadEditorCommandResult>? Changed;
    public event EventHandler<CadDocumentChangeSet>? DocumentChanged;
    public event EventHandler<CadCommandActivity>? Activity;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public CommandHistorySettings Settings => _settings;

    public CadEditorCommandManager(
        CadDocument document,
        CadViewport viewport,
        CadSelectionSet selection,
        ICadSpatialIndex spatialIndex,
        CadDocumentChangeDispatcher documentChanges,
        CommandHistory<ICadEditorCommand>? history = null,
        CommandHistorySettings? settings = null)
    {
        _context = new CadEditorCommandContext(
            document ?? throw new ArgumentNullException(nameof(document)),
            viewport ?? throw new ArgumentNullException(nameof(viewport)),
            selection ?? throw new ArgumentNullException(nameof(selection)),
            spatialIndex ?? throw new ArgumentNullException(nameof(spatialIndex)));
        _documentChanges = documentChanges ?? throw new ArgumentNullException(nameof(documentChanges));
        _history = history ?? new CommandHistory<ICadEditorCommand>();
        _settings = settings ?? new CommandHistorySettings();
        _documentChanges.DocumentChanged += (_, result) => DocumentChanged?.Invoke(this, result);
    }

    public CadEditorCommandResult Execute(ICadEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = command.Execute(_context);
        _history.PushExecuted(command);
        Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.HasChanges);
        return result;
    }

    public CadEditorCommandResult ExecuteRange(
        IEnumerable<ICadEditorCommand> commands,
        string name = "Command Batch")
    {
        ArgumentNullException.ThrowIfNull(commands);

        var commandArray = commands.ToArray();
        if (commandArray.Length == 0)
            return CadEditorCommandResult.Empty;

        var batchId = Guid.NewGuid();
        var results = new List<CadEditorCommandResult>(commandArray.Length);

        foreach (var command in commandArray)
        {
            var result = command.Execute(_context);
            _history.PushExecuted(command, batchId);
            Publish(result);
            results.Add(result);
        }

        var combined = CadEditorCommandResult.Combine(results);
        PublishActivity(name, CadCommandActivityKind.Execute, commandArray.Length, combined.HasChanges);
        return combined;
    }



    public CadEditorCommandResult Undo()
    {
        var entries = _history.PopUndo(_settings.UndoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Undo", CadCommandActivityKind.Undo, 0, false);
            return CadEditorCommandResult.Empty;
        }

        var results = new List<CadEditorCommandResult>(entries.Count);
        foreach (var entry in entries)
        {
            var result = entry.Command.Undo(_context);
            _history.PushUndone(entry);
            Publish(result);
            results.Add(result);
        }

        var combined = CadEditorCommandResult.Combine(results);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Undo, entries.Count, combined.HasChanges);
        return combined;
    }

    public CadEditorCommandResult Redo()
    {
        var entries = _history.PopRedo(_settings.RedoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Redo", CadCommandActivityKind.Redo, 0, false);
            return CadEditorCommandResult.Empty;
        }

        var results = new List<CadEditorCommandResult>(entries.Count);
        foreach (var entry in entries)
        {
            var result = entry.Command.Execute(_context);
            _history.PushRedone(entry);
            Publish(result);
            results.Add(result);
        }

        var combined = CadEditorCommandResult.Combine(results);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Redo, entries.Count, combined.HasChanges);
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
            CadCommandActivityScope.Editor,
            commandCount,
            hasChanges));
    }

    private static string GetActivityName(IReadOnlyList<CommandHistoryEntry<ICadEditorCommand>> entries) =>
        entries.Count == 1 ? entries[0].Command.Name : "Command Batch";

    private void Publish(CadEditorCommandResult result)
    {
        if (!result.HasChanges)
            return;

        if (result.DocumentChanges.DocumentChanged)
            _documentChanges.Publish(result.DocumentChanges);

        Changed?.Invoke(this, result);
    }
}
