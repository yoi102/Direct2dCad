using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.CommandLine;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Lang;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Tools;
using Direct2dCad.ViewModels.Collections;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class CommandLineToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private const int MaximumEntryCount = 1000;
    private const int MaximumPendingEntryCount = 4000;
    private readonly ICadCommandLineService _commandLineService;
    private readonly ICadToolCommandLineService _toolCommandLineService;
    private readonly IDisposable _commandActivitySubscription;
    private readonly IDisposable _interactionActivitySubscription;
    private readonly IDisposable? _toolActivitySubscription;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _pendingEntriesGate = new();
    private readonly Queue<CadCommandLineEntryViewModel> _pendingEntries = [];
    private readonly List<string> _commandHistory = [];
    private CadDocumentViewModel? _documentViewModel;
    private TerminalExecution? _execution;
    private string? _lastRepeatableCommand;
    private int _droppedPendingEntryCount;
    private int _historyIndex;
    private bool _recallingHistory;
    public bool IsNavigatingHistory { get; private set; }
    private bool _disposed;

    public CommandLineToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        ICadCommandLineService commandLineService,
        ICadToolCommandLineService toolCommandLineService,
        IAsyncSubscriber<CadCommandActivityMessage> commandActivitySubscriber,
        IAsyncSubscriber<CadInteractionActivityMessage> interactionActivitySubscriber,
        IAsyncSubscriber<CadToolActivityMessage>? toolActivitySubscriber = null)
        : base(toolboxLayoutSettingsStore, "toolbox.command-line", DockZone.BottomRight, isOpenByDefault: true)
    {
        _commandLineService = commandLineService;
        _toolCommandLineService = toolCommandLineService;
        _commandActivitySubscription = commandActivitySubscriber.Subscribe(
            (message, _) =>
            {
                OnCommandActivity(message);
                return ValueTask.CompletedTask;
            });
        _interactionActivitySubscription = interactionActivitySubscriber.Subscribe(
            (message, _) =>
            {
                OnInteractionActivity(message);
                return ValueTask.CompletedTask;
            });
        _toolActivitySubscription = toolActivitySubscriber?.Subscribe((message, _) =>
        {
            var kind = message.Outcome == "Failed" ? CadCommandLineEntryKind.Error :
                message.Outcome == "Canceled" ? CadCommandLineEntryKind.Warning : CadCommandLineEntryKind.Activity;
            var document = message.DocumentName is { } name ? $" [{name}]" : string.Empty;
            AddEntry(kind, $"[AI]{document} {message.ToolName} {message.Outcome}: {message.Summary}");
            return ValueTask.CompletedTask;
        });

        Title = Strings.Terminal;
        Icon = toolboxIconProvider.Terminal;
        Shortcut = "Ctrl+Oem3";
        CanClose = false;

        AddEntry(
            CadCommandLineEntryKind.Information,
            "Direct2dCad command line ready. Type HELP for terminal commands or TOOLS for CAD tools.");
    }

    [ObservableProperty]
    public partial string CommandText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedSuggestion { get; set; }

    public ObservableRangeCollection<CadCommandLineEntryViewModel> Entries { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public string LatestOutputText { get; private set; } = string.Empty;

    public bool HasDocument => _documentViewModel is not null;
    public bool IsCommandExecuting => _execution is not null;
    public bool HasSuggestions => Suggestions.Count > 0;
    public string? InputHint { get; private set; }
    public bool HasPendingEntries
    {
        get
        {
            lock (_pendingEntriesGate)
                return _pendingEntries.Count > 0 || _droppedPendingEntryCount > 0;
        }
    }

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        if (ReferenceEquals(_documentViewModel, documentViewModel))
            return;

        _documentViewModel = documentViewModel;
        OnPropertyChanged(nameof(HasDocument));
        AddEntry(
            CadCommandLineEntryKind.Information,
            documentViewModel is null
                ? "No active document."
                : $"Active document: {documentViewModel.CadEditor.Document.Name}");
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ExecuteCommandAsync()
    {
        if (_disposed) return;
        var commandLine = CommandText.Trim();
        if (IsCancelCommand(commandLine))
        {
            CancelCurrentCommand();
            return;
        }
        if (_execution is not null)
        {
            AddMessage(CadCommandLineEntryKind.Warning, CadUiText.Get("TerminalCommandBusy"));
            return;
        }
        var document = _documentViewModel;
        var explicitInput = commandLine.Length > 0;
        if (commandLine.Length == 0)
        {
            if (document is { HasActiveDrawingTool: true } or { IsGripEditing: true } or { IsPastePreviewActive: true })
                commandLine = "DONE";
            else if (_lastRepeatableCommand is { } repeated)
                commandLine = repeated;
            else
                return;
        }

        AddEntry(CadCommandLineEntryKind.Input, $"> {commandLine}");
        if (explicitInput) AddToHistory(commandLine);
        CommandText = string.Empty;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var execution = new TerminalExecution(commandLine, document, document?.CadEditor, cancellation);
        _execution = execution;
        OnPropertyChanged(nameof(IsCommandExecuting));
        try
        {
            var result = CommandName(commandLine) == "SCRIPT"
                ? await ExecuteScriptAsync(commandLine, cancellation.Token)
                : await DispatchLineAsync(commandLine, document, execution.Editor, cancellation.Token);
            if (_disposed) return;
            if (result.ClearOutput)
            {
                ClearOutput();
                return;
            }
            RememberSuccessfulCommand(commandLine, result.Success);
            if (!string.IsNullOrWhiteSpace(result.Message))
                AddMessage(result.Success ? CadCommandLineEntryKind.Output : CadCommandLineEntryKind.Error, result.Message);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
                AddMessage(CadCommandLineEntryKind.Warning,
                    string.Format(CadUiText.Get("TerminalCommandCanceledFormat"), commandLine));
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                if (cancellation.IsCancellationRequested)
                    AddMessage(CadCommandLineEntryKind.Warning,
                        string.Format(CadUiText.Get("TerminalCommandCanceledFormat"), commandLine));
                else AddMessage(CadCommandLineEntryKind.Error, exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_execution, execution))
            {
                _execution = null;
                if (!_disposed) OnPropertyChanged(nameof(IsCommandExecuting));
            }
        }
    }

    public void SubmitCommandInput()
    {
        if (_disposed) return;
        if (IsCancelCommand(CommandText.Trim())) CancelCurrentCommand();
        else if (ExecuteCommandCommand.CanExecute(null)) ExecuteCommandCommand.Execute(null);
        else AddMessage(CadCommandLineEntryKind.Warning, CadUiText.Get("TerminalCommandBusy"));
    }

    private static bool IsCancelCommand(string commandLine) =>
        CadCommandLineSyntax.TryTokenize(commandLine, out var tokens, out _) && tokens.Length == 1 &&
        CadCommandLineSyntax.NormalizeCommandName(tokens[0]) is "CANCEL" or "ESC";

    private static string CommandName(string commandLine) =>
        CadCommandLineSyntax.NormalizeCommandName(commandLine.TrimStart().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty);

    private void RememberSuccessfulCommand(string commandLine, bool success)
    {
        var name = CommandName(commandLine);
        if (!success || CadCommandLinePointParser.LooksLikePoint(commandLine) ||
            double.TryParse(commandLine, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _)) return;
        if (name is "CANCEL" or "ESC" or "DONE" or "D" or "BACK" or "UNDOPOINT" or "SCRIPT") return;
        _lastRepeatableCommand = commandLine;
    }

    private sealed record TerminalExecution(string CommandLine, CadDocumentViewModel? Document,
        Direct2dCad.Editor.CadEditor? Editor, CancellationTokenSource Cancellation);

    public void ShowPreviousCommand()
    {
        if (_commandHistory.Count == 0)
            return;

        _historyIndex = Math.Max(0, _historyIndex - 1);
        RecallHistory(_commandHistory[_historyIndex]);
    }

    public void ShowNextCommand()
    {
        if (_commandHistory.Count == 0)
            return;

        _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
        RecallHistory(_historyIndex < _commandHistory.Count
            ? _commandHistory[_historyIndex]
            : string.Empty);
    }

    private void RecallHistory(string text)
    {
        _recallingHistory = true;
        IsNavigatingHistory = true;
        try { CommandText = text; DismissSuggestions(); }
        finally { _recallingHistory = false; }
    }

    public void CompleteCommand()
    {
        if (Suggestions.Count == 0)
            return;

        var suggestion = SelectedSuggestion ?? Suggestions[0];
        CommandText = suggestion + " ";
    }

    public void SelectPreviousSuggestion() => MoveSuggestion(-1);

    public void SelectNextSuggestion() => MoveSuggestion(1);

    public bool AcceptSelectedSuggestion()
    {
        var suggestion = SelectedSuggestion ?? Suggestions.FirstOrDefault();
        if (suggestion is null)
            return false;

        CommandText = suggestion + " ";
        return true;
    }

    public void DismissSuggestions()
    {
        Suggestions.Clear();
        SelectedSuggestion = null;
        OnPropertyChanged(nameof(HasSuggestions));
    }

    public void CancelCurrentCommand()
    {
        if (_disposed) return;
        CommandText = string.Empty;
        DismissSuggestions();
        if (_execution is { } execution)
        {
            if (execution.Cancellation.IsCancellationRequested) return;
            AddMessage(CadCommandLineEntryKind.Warning,
                string.Format(CadUiText.Get("TerminalCommandCancellingFormat"), execution.CommandLine));
            execution.Cancellation.Cancel();
            return;
        }
        try
        {
            var result = _commandLineService.Execute("CANCEL", _documentViewModel);
            AddMessage(result.Success ? CadCommandLineEntryKind.Output : CadCommandLineEntryKind.Error, result.Message);
        }
        catch (Exception exception) { AddMessage(CadCommandLineEntryKind.Error, exception.Message); }
    }

    partial void OnCommandTextChanged(string value)
    {
        Suggestions.Clear();
        SelectedSuggestion = null;
        if (!_recallingHistory) IsNavigatingHistory = false;
        InputHint = _commandLineService.GetInputHint(value) ?? _toolCommandLineService.GetInputHint(value);
        OnPropertyChanged(nameof(InputHint));
        var prefix = value.TrimStart();
        if (prefix.Length > 0 && !_recallingHistory)
        {
            var suggestions = _commandLineService.Complete(prefix)
                .Concat(_toolCommandLineService.Complete(prefix))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Take(12);
            foreach (var suggestion in suggestions)
                Suggestions.Add(suggestion);
        }

        OnPropertyChanged(nameof(HasSuggestions));
    }

    private void MoveSuggestion(int direction)
    {
        if (Suggestions.Count == 0)
            return;

        var currentIndex = SelectedSuggestion is null
            ? -1
            : Suggestions.IndexOf(SelectedSuggestion);
        var nextIndex = currentIndex < 0
            ? direction > 0 ? 0 : Suggestions.Count - 1
            : (currentIndex + direction + Suggestions.Count) % Suggestions.Count;
        SelectedSuggestion = Suggestions[nextIndex];
    }

    public void Dispose()
    {
        lock (_pendingEntriesGate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingEntries.Clear();
            _droppedPendingEntryCount = 0;
        }
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        _commandActivitySubscription.Dispose();
        _interactionActivitySubscription.Dispose();
        _toolActivitySubscription?.Dispose();
    }

    public int FlushPendingEntries(int maximumBatchSize = 100)
    {
        if (maximumBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));

        var batch = new List<CadCommandLineEntryViewModel>(maximumBatchSize);
        lock (_pendingEntriesGate)
        {
            if (_disposed) return 0;
            if (_droppedPendingEntryCount > 0)
            {
                batch.Add(new CadCommandLineEntryViewModel(
                    DateTimeOffset.Now,
                    CadCommandLineEntryKind.Warning,
                    $"[Terminal] {_droppedPendingEntryCount} buffered entries omitted."));
                _droppedPendingEntryCount = 0;
            }

            while (batch.Count < maximumBatchSize && _pendingEntries.TryDequeue(out var entry))
                batch.Add(entry);
        }

        Entries.AddRangeAndTrimStart(batch, MaximumEntryCount);
        return batch.Count;
    }

    private void OnCommandActivity(CadCommandActivityMessage message)
    {
        if (_disposed)
            return;

        var activity = message.Activity;
        if (message.DocumentViewModel is CadDocumentViewModel { IsPanning: true } &&
            activity.Scope == CadCommandActivityScope.Editor &&
            string.Equals(activity.Name, "Pan View", StringComparison.Ordinal))
        {
            return;
        }

        var operation = activity.Kind switch
        {
            CadCommandActivityKind.Execute => "Execute",
            CadCommandActivityKind.Undo => "Undo",
            CadCommandActivityKind.Redo => "Redo",
            _ => activity.Kind.ToString()
        };
        var scope = activity.Scope == CadCommandActivityScope.Document ? "Document" : "Editor";
        var count = activity.CommandCount > 1 ? $" x{activity.CommandCount}" : string.Empty;
        var outcome = activity.CommandCount == 0
            ? " (nothing available)"
            : activity.HasChanges ? string.Empty : " (no changes)";

        AddEntry(
            activity.CommandCount == 0 ? CadCommandLineEntryKind.Warning : CadCommandLineEntryKind.Activity,
            $"[{scope}] [{message.DocumentName}] {operation}: {activity.Name}{count}{outcome}");
    }

    private void OnInteractionActivity(CadInteractionActivityMessage message)
    {
        AddEntry(CadCommandLineEntryKind.Activity, $"[Interaction] [{message.DocumentName}] {message.Name}");
    }

    private void AddToHistory(string commandLine)
    {
        if (_commandHistory.Count == 0 ||
            !string.Equals(_commandHistory[^1], commandLine, StringComparison.Ordinal))
        {
            _commandHistory.Add(commandLine);
        }

        _historyIndex = _commandHistory.Count;
    }

    private void AddEntry(CadCommandLineEntryKind kind, string text)
    {
        lock (_pendingEntriesGate)
        {
            if (_disposed) return;
            if (_pendingEntries.Count >= MaximumPendingEntryCount)
            {
                _pendingEntries.Dequeue();
                _droppedPendingEntryCount++;
            }

            _pendingEntries.Enqueue(new CadCommandLineEntryViewModel(DateTimeOffset.Now, kind, text));
        }
    }

    private void ClearOutput()
    {
        lock (_pendingEntriesGate)
        {
            _pendingEntries.Clear();
            _droppedPendingEntryCount = 0;
        }

        Entries.Clear();
        LatestOutputText = string.Empty;
        OnPropertyChanged(nameof(LatestOutputText));
    }

    private void AddMessage(CadCommandLineEntryKind kind, string message)
    {
        if (_disposed) return;
        LatestOutputText = message;
        OnPropertyChanged(nameof(LatestOutputText));
        foreach (var line in message.Replace("\r\n", "\n").Split('\n'))
            AddEntry(kind, line);
    }


}

public enum CadCommandLineEntryKind
{
    Information,
    Input,
    Output,
    Activity,
    Warning,
    Error
}

public sealed record CadCommandLineEntryViewModel(
    DateTimeOffset Timestamp,
    CadCommandLineEntryKind Kind,
    string Text);
