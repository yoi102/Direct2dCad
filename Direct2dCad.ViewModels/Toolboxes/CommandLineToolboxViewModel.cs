using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.CommandLine;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.AI;
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
    private readonly ICadAiCommandLineService _aiCommandLineService;
    private readonly IDisposable _commandActivitySubscription;
    private readonly IDisposable _interactionActivitySubscription;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _pendingEntriesGate = new();
    private readonly Queue<CadCommandLineEntryViewModel> _pendingEntries = [];
    private readonly List<string> _commandHistory = [];
    private CadDocumentViewModel? _documentViewModel;
    private int _droppedPendingEntryCount;
    private int _historyIndex;

    public CommandLineToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        ICadCommandLineService commandLineService,
        ICadAiCommandLineService aiCommandLineService,
        IAsyncSubscriber<CadCommandActivityMessage> commandActivitySubscriber,
        IAsyncSubscriber<CadInteractionActivityMessage> interactionActivitySubscriber)
        : base(toolboxLayoutSettingsStore, "toolbox.command-line", DockZone.BottomRight, isOpenByDefault: true)
    {
        _commandLineService = commandLineService;
        _aiCommandLineService = aiCommandLineService;
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

        Title = Strings.Terminal;
        Icon = toolboxIconProvider.Terminal;
        Shortcut = "Ctrl+Oem3";
        CanClose = false;

        AddEntry(
            CadCommandLineEntryKind.Information,
            "Direct2dCad command line ready. Type HELP for terminal commands or TOOLS for AI CAD tools.");
    }

    [ObservableProperty]
    public partial string CommandText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedSuggestion { get; set; }

    public ObservableRangeCollection<CadCommandLineEntryViewModel> Entries { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasDocument => _documentViewModel is not null;
    public bool HasSuggestions => Suggestions.Count > 0;
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
        var commandLine = CommandText.Trim();
        if (commandLine.Length == 0)
        {
            if (_commandHistory.Count == 0)
                return;

            commandLine = _commandHistory[^1];
        }

        AddEntry(CadCommandLineEntryKind.Input, $"> {commandLine}");
        AddToHistory(commandLine);
        CommandText = string.Empty;

        try
        {
            var aiResult = await _aiCommandLineService.TryExecuteAsync(
                commandLine,
                _disposeCancellation.Token);
            if (aiResult is not null)
            {
                AddMessage(
                    aiResult.Success ? CadCommandLineEntryKind.Output : CadCommandLineEntryKind.Error,
                    aiResult.Message);
                return;
            }
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            AddMessage(CadCommandLineEntryKind.Error, exception.Message);
            return;
        }

        CadCommandLineResult result;
        try
        {
            result = _commandLineService.Execute(commandLine, _documentViewModel);
        }
        catch (Exception exception)
        {
            AddMessage(CadCommandLineEntryKind.Error, exception.Message);
            return;
        }

        if (result.ClearOutput)
        {
            ClearOutput();
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            AddMessage(
                result.Success ? CadCommandLineEntryKind.Output : CadCommandLineEntryKind.Error,
                result.Message);
        }
    }

    public void ShowPreviousCommand()
    {
        if (_commandHistory.Count == 0)
            return;

        _historyIndex = Math.Max(0, _historyIndex - 1);
        CommandText = _commandHistory[_historyIndex];
    }

    public void ShowNextCommand()
    {
        if (_commandHistory.Count == 0)
            return;

        _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
        CommandText = _historyIndex < _commandHistory.Count
            ? _commandHistory[_historyIndex]
            : string.Empty;
    }

    public void CompleteCommand()
    {
        if (Suggestions.Count == 0)
            return;

        var suggestion = SelectedSuggestion ?? Suggestions[0];
        CommandText = suggestion + (Suggestions.Count == 1 ? " " : string.Empty);
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
        CommandText = "CANCEL";
        _ = ExecuteCommandAsync();
    }

    partial void OnCommandTextChanged(string value)
    {
        Suggestions.Clear();
        SelectedSuggestion = null;
        var prefix = value.Trim();
        if (prefix.Length > 0)
        {
            var suggestions = _commandLineService.Complete(prefix)
                .Concat(_aiCommandLineService.Complete(prefix))
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
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        _commandActivitySubscription.Dispose();
        _interactionActivitySubscription.Dispose();
    }

    public int FlushPendingEntries(int maximumBatchSize = 100)
    {
        if (maximumBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));

        var batch = new List<CadCommandLineEntryViewModel>(maximumBatchSize);
        lock (_pendingEntriesGate)
        {
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
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        var activity = message.Activity;
        if (_documentViewModel.IsPanning &&
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
            $"[{scope}] {operation}: {activity.Name}{count}{outcome}");
    }

    private void OnInteractionActivity(CadInteractionActivityMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        AddEntry(CadCommandLineEntryKind.Activity, $"[Interaction] {message.Name}");
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
    }

    private void AddMessage(CadCommandLineEntryKind kind, string message)
    {
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
