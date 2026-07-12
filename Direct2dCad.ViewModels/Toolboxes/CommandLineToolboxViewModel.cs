using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.CommandLine;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class CommandLineToolboxViewModel : ObservableToolboxBase, IDisposable
{
    private const int MaximumEntryCount = 1000;
    private readonly ICadCommandLineService _commandLineService;
    private readonly IDisposable _commandActivitySubscription;
    private readonly IDisposable _interactionActivitySubscription;
    private readonly List<string> _commandHistory = [];
    private CadDocumentViewModel? _documentViewModel;
    private int _historyIndex;

    public CommandLineToolboxViewModel(
        IToolboxIconProvider toolboxIconProvider,
        ICadCommandLineService commandLineService,
        ISubscriber<CadCommandActivityMessage> commandActivitySubscriber,
        ISubscriber<CadInteractionActivityMessage> interactionActivitySubscriber)
    {
        _commandLineService = commandLineService;
        _commandActivitySubscription = commandActivitySubscriber.Subscribe(OnCommandActivity);
        _interactionActivitySubscription = interactionActivitySubscriber.Subscribe(OnInteractionActivity);

        Title = Strings.Terminal;
        Zone = DockZone.BottomRight;
        Icon = toolboxIconProvider.Terminal;
        Shortcut = "Ctrl+Oem3";
        IsOpenByDefault = true;
        ContentId = Id = Guid.NewGuid().ToString();
        CanClose = false;

        AddEntry(CadCommandLineEntryKind.Information, "Direct2dCad command line ready. Type HELP for commands.");
    }

    [ObservableProperty]
    public partial string ContentId { get; private set; }

    [ObservableProperty]
    public partial string CommandText { get; set; } = string.Empty;

    public ObservableCollection<CadCommandLineEntryViewModel> Entries { get; } = [];

    public bool HasDocument => _documentViewModel is not null;

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

    [RelayCommand]
    private void ExecuteCommand()
    {
        var commandLine = CommandText.Trim();
        if (commandLine.Length == 0)
            return;

        AddEntry(CadCommandLineEntryKind.Input, $"> {commandLine}");
        AddToHistory(commandLine);
        CommandText = string.Empty;

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
            Entries.Clear();
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

    public void Dispose()
    {
        _commandActivitySubscription.Dispose();
        _interactionActivitySubscription.Dispose();
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
        Entries.Add(new CadCommandLineEntryViewModel(DateTimeOffset.Now, kind, text));
        while (Entries.Count > MaximumEntryCount)
            Entries.RemoveAt(0);
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
