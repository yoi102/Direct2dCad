using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Services.Documents;

namespace Direct2dCad.ViewModels;

public partial class MainViewModel
{
    private readonly CadRecoveryStore _recoveryStore;
    public ObservableCollection<CadRecoveryEntry> RecoveryEntries { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearAllRecoveryCommand))]
    public partial bool HasRecoveryEntries { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRecoveryCopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearRecoveryEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearAllRecoveryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRecoveryFolderCommand))]
    public partial bool IsRecoveryActionRunning { get; private set; }
    public CadLongOperation OpenOperation { get; } = new();

    public void RefreshRecoveryEntries()
    {
        try
        {
            RecoveryEntries.Clear();
            foreach (var entry in _recoveryStore.ListRecoverable().GroupBy(e => e.DocumentId).Select(g => g.First()))
                RecoveryEntries.Add(entry);
            HasRecoveryEntries = RecoveryEntries.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _snackbarService.Enqueue($"{CadUiText.Get(LangKeys.RecoveryCleanupFailed)}: {ex.Message}"); }
    }

    public async Task CaptureRecoveryCopiesAsync()
    {
        foreach (var tab in _dockLayoutService.Documents.OfType<EditorTabViewModel>().ToArray())
            await tab.TryWriteRecoveryAsync(_recoveryStore);
    }

    private async Task CompleteRecoverySessionAsync(EditorTabViewModel[] documents)
    {
        foreach (var document in documents) await document.CompleteRecoveryCloseAsync();
        try { await _recoveryStore.CompleteSessionAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _snackbarService.Enqueue($"{CadUiText.Get(LangKeys.RecoveryCleanupFailed)}: {ex.Message}");
        }
        RefreshRecoveryEntries();
    }

    private bool CanClearAllRecovery() => HasRecoveryEntries && !IsRecoveryActionRunning;
    private bool CanUseRecoveryEntry(CadRecoveryEntry? entry) => entry is not null && !IsRecoveryActionRunning;

    [RelayCommand(CanExecute = nameof(CanClearAllRecovery))]
    private Task ClearAllRecoveryAsync() => ClearRecoveryAsync(null);

    [RelayCommand(CanExecute = nameof(CanUseRecoveryEntry))]
    private Task ClearRecoveryEntryAsync(CadRecoveryEntry? entry) => entry is null ? Task.CompletedTask : ClearRecoveryAsync(entry.DocumentId);

    private async Task ClearRecoveryAsync(string? documentId)
    {
        if (IsRecoveryActionRunning) return;
        IsRecoveryActionRunning = true;
        try { await _recoveryStore.RemoveRecoverableAsync(documentId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _snackbarService.Enqueue($"{CadUiText.Get(LangKeys.RecoveryCleanupFailed)}: {ex.Message}");
        }
        finally
        {
            RefreshRecoveryEntries();
            IsRecoveryActionRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseRecoveryEntry))]
    private async Task OpenRecoveryCopyAsync(CadRecoveryEntry? entry)
    {
        if (entry is null || IsRecoveryActionRunning || OpenOperation.IsRunning) return;
        IsRecoveryActionRunning = true;
        try
        {
            var existing = _dockLayoutService.Documents.OfType<EditorTabViewModel>()
                .FirstOrDefault(tab => tab.RecoverySourceDocumentId == entry.DocumentId);
            if (existing is not null)
            {
                ActivateEditorDocument(existing);
                return;
            }
            var document = await OpenOperation.RunAsync(CadUiText.Get(LangKeys.OpeningDrawing), token => _recoveryStore.LoadAsync(entry, token));
            document.AssignIndependentIdentity();
            document.Rename($"{entry.Name} · {CadUiText.Get(LangKeys.RecoveredSuffix)}");
            var tab = _dockLayoutService.OpenOrActivateDocument<EditorTabViewModel>(_ => false, () => _editorTabFactory.Create(result =>
            {
                result.Load(document, "");
                result.AttachRecoverySource(_recoveryStore, entry, RefreshRecoveryEntries);
            }));
            CurrentEditorTabViewModel = tab;
            DocumentExplorer.RefreshDocuments();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, CadUiText.Get(LangKeys.RecoveryDrawings)); }
        finally { IsRecoveryActionRunning = false; }
    }
}
