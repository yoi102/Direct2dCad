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
    private Guid? _coalescingGesture;
    private ICadCoalescibleEditorCommand? _coalescingCommand;

    public event EventHandler<CadEditorCommandResult>? Changed;
    public event EventHandler<CadDocumentChangeSet>? DocumentChanged;
    public event EventHandler<CadCommandActivity>? Activity;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public CommandHistorySettings Settings => _settings;
    public long EstimatedHistoryBytes => _history.EstimatedRetainedBytes;

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
        _documentChanges.DocumentChanged += OnDocumentChanged;
    }

    public CadEditorCommandResult Execute(ICadEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EndCoalescing();

        var result = command.Execute(_context);
        RecordExecuted(command);
        TrimHistory();
        Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.HasChanges);
        return result;
    }

    /// <summary>
    /// Publishes each live gesture update while retaining a single undo entry.
    /// A different command, history operation, or document change ends coalescing.
    /// </summary>
    public CadEditorCommandResult ExecuteCoalesced(ICadCoalescibleEditorCommand command, Guid gestureId)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (gestureId == Guid.Empty)
            throw new ArgumentException("Gesture id cannot be empty.", nameof(gestureId));

        var previous = _coalescingGesture == gestureId ? _coalescingCommand : null;
        var result = command.Execute(_context);
        if (previous is not null && _history.IsLatestExecuted(previous) && previous.TryMergeExecuted(command))
            _history.RefreshLatestExecuted(previous, CadCommandPayloadEstimate.Estimate(previous));
        else
        {
            RecordExecuted(command);
            _coalescingCommand = command;
        }
        _coalescingGesture = gestureId;
        TrimHistory();
        Publish(result);
        PublishActivity(command.Name, CadCommandActivityKind.Execute, 1, result.HasChanges);
        return result;
    }

    public CadEditorCommandResult ExecuteRange(
        IEnumerable<ICadEditorCommand> commands,
        string name = "Command Batch")
    {
        ArgumentNullException.ThrowIfNull(commands);
        EndCoalescing();

        var commandArray = commands.ToArray();
        foreach (var command in commandArray)
            ArgumentNullException.ThrowIfNull(command);
        if (commandArray.Length == 0)
            return CadEditorCommandResult.Empty;

        var batchId = Guid.NewGuid();
        var results = new List<CadEditorCommandResult>(commandArray.Length);

        foreach (var command in commandArray)
        {
            try
            {
                var result = command.Execute(_context);
                RecordExecuted(command, batchId);
                results.Add(result);
            }
            catch
            {
                TrimHistory();
                Publish(CadEditorCommandResult.Combine(results));
                throw;
            }
        }

        var combined = CadEditorCommandResult.Combine(results);
        TrimHistory();
        Publish(combined);
        PublishActivity(name, CadCommandActivityKind.Execute, commandArray.Length, combined.HasChanges);
        return combined;
    }



    public CadEditorCommandResult Undo()
    {
        EndCoalescing();
        var entries = _history.PopUndo(_settings.UndoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Undo", CadCommandActivityKind.Undo, 0, false);
            return CadEditorCommandResult.Empty;
        }

        var results = new List<CadEditorCommandResult>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                var result = entry.Command.Undo(_context);
                _history.PushUndone(entry);
                results.Add(result);
            }
            catch
            {
                Publish(CadEditorCommandResult.Combine(results));
                throw;
            }
        }

        var combined = CadEditorCommandResult.Combine(results);
        Publish(combined);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Undo, entries.Count, combined.HasChanges);
        return combined;
    }

    public CadEditorCommandResult Redo()
    {
        EndCoalescing();
        var entries = _history.PopRedo(_settings.RedoMode);
        if (entries.Count == 0)
        {
            PublishActivity("Redo", CadCommandActivityKind.Redo, 0, false);
            return CadEditorCommandResult.Empty;
        }

        var results = new List<CadEditorCommandResult>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                var result = entry.Command.Execute(_context);
                _history.PushRedone(entry);
                results.Add(result);
            }
            catch
            {
                Publish(CadEditorCommandResult.Combine(results));
                throw;
            }
        }

        var combined = CadEditorCommandResult.Combine(results);
        Publish(combined);
        PublishActivity(GetActivityName(entries), CadCommandActivityKind.Redo, entries.Count, combined.HasChanges);
        return combined;
    }

    private void RecordExecuted(ICadEditorCommand command, Guid? batchId = null) =>
        _history.PushExecuted(command, batchId, CadCommandPayloadEstimate.Estimate(command));

    private void TrimHistory() =>
        _history.TrimUndo(_settings.MaximumUndoCommands, _settings.MaximumUndoBytes);

    private void EndCoalescing()
    {
        _coalescingGesture = null;
        _coalescingCommand = null;
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

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet result)
    {
        if (!result.IsDerivedGeometry) EndCoalescing();
        if (MayAffectHitTestStrokePadding(result))
            _context.HitTesting.InvalidateCaches();
        DocumentChanged?.Invoke(this, result);
    }

    private static bool MayAffectHitTestStrokePadding(CadDocumentChangeSet result)
    {
        if (result.AffectsDocumentStructure)
            return true;

        const CadEntityChangeKind relevantChanges =
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.Rotation;
        return result.EntityChanges.Any(change => (change.Kind & relevantChanges) != 0);
    }
}
