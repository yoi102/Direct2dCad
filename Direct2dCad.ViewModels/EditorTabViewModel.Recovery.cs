using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Services.Documents;

namespace Direct2dCad.ViewModels;

public partial class EditorTabViewModel
{
    private readonly CancellationTokenSource _recoveryLifetime = new();
    private bool _recoveryRunning;
    private bool _recoveryFailureReported;
    private long _lastRecoveryVersion = -1;
    private string _lastRecoveryName = "";
    private Direct2dCad.Editor.CadEditor? _lastRecoveryEditor;
    private CadRecoveryStore? _recoveryStore;
    private bool _discardRecoveryOnClose;
    private Task _pendingRecovery = Task.CompletedTask;
    private string? _recoverySourceDocumentId;
    internal string? RecoverySourceDocumentId => _recoverySourceDocumentId;
    private Action? _recoveryEntriesChanged;

    internal void UseRecoveryStore(CadRecoveryStore store) => _recoveryStore ??= store;

    internal void AttachRecoverySource(CadRecoveryStore store, CadRecoveryEntry entry, Action entriesChanged)
    {
        _recoveryStore = store;
        _recoverySourceDocumentId = entry.DocumentId;
        _recoveryEntriesChanged = entriesChanged;
    }

    private async Task RemoveRecoveryCopiesAsync()
    {
        try
        {
            var store = _recoveryStore ?? new CadRecoveryStore();
            await store.RemoveDocumentAsync(CadDocumentViewModel.CadEditor.Document.Id.ToString());
            if (_recoverySourceDocumentId is { } sourceId)
            {
                await store.RemoveRecoverableAsync(sourceId);
                _recoverySourceDocumentId = null;
            }
            _lastRecoveryVersion = -1;
            _lastRecoveryEditor = null;
            _recoveryEntriesChanged?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _snackbarService.Enqueue($"{CadUiText.Get(LangKeys.RecoveryCleanupFailed)}: {ex.Message}");
        }
    }

    internal async Task CompleteRecoveryCloseAsync()
    {
        _discardRecoveryOnClose = true;
        _recoveryLifetime.Cancel();
        await _pendingRecovery;
        await RemoveRecoveryCopiesAsync();
    }

    public Task TryWriteRecoveryAsync(CadRecoveryStore store)
    {
        _recoveryStore=store;
        if(DateTimeOffset.UtcNow-CadDocumentViewModel.LastInteractionAtUtc<TimeSpan.FromSeconds(5)) return Task.CompletedTask;
        if (_disposed || _discardRecoveryOnClose || _recoveryRunning || !IsModified || IsCompatibilityReadOnly ||
            !_userSettings.General.IsAutoRecoveryEnabled || Operation.IsRunning) return Task.CompletedTask;
        var editor = CadDocumentViewModel.CadEditor;
        if(editor.DocumentChangeVersion==0 && editor.Document.Entities.Count==0) return Task.CompletedTask;
        if (ReferenceEquals(editor, _lastRecoveryEditor) && editor.DocumentChangeVersion == _lastRecoveryVersion &&
            editor.Document.Name == _lastRecoveryName) return Task.CompletedTask;
        _recoveryRunning = true;
        return _pendingRecovery = WriteRecoveryCoreAsync(store, editor);
    }

    private async Task WriteRecoveryCoreAsync(CadRecoveryStore store, Direct2dCad.Editor.CadEditor editor)
    {
        try
        {
            var entry = await store.SaveAsync(editor, CurrentFilePath, _recoveryLifetime.Token);
            if(_disposed || !IsModified || _discardRecoveryOnClose)
            {
                await store.RemoveDocumentAsync(editor.Document.Id.ToString());
                return;
            }
            if (!_disposed && ReferenceEquals(editor, CadDocumentViewModel.CadEditor))
            {
                _lastRecoveryEditor = editor;
                _lastRecoveryVersion = entry.DocumentVersion;
                _lastRecoveryName = entry.Name;
            }
            _recoveryFailureReported = false;
        }
        catch (OperationCanceledException) { }
        catch (Direct2dCad.IO.CadSnapshotChangedException) { }
        catch (Exception ex)
        {
            if (!_disposed && !_recoveryFailureReported)
                _snackbarService.Enqueue($"{CadUiText.Get(LangKeys.RecoveryFailed)}: {ex.Message}");
            _recoveryFailureReported = true;
        }
        finally { _recoveryRunning = false; }
    }
}
