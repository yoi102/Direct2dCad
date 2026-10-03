using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels;

public partial class MainViewModel
{
    private readonly IFileLocationService? _fileLocationService;

    private bool CanUseEditorDocument(EditorTabViewModel? document) => document is not null &&
        _dockLayoutService.Documents.Contains(document);

    [RelayCommand(CanExecute = nameof(CanUseEditorDocument))]
    private void ActivateEditorDocument(EditorTabViewModel? document)
    {
        if (!CanUseEditorDocument(document)) return;
        _dockLayoutService.ActiveDockable = document;
        ActiveDockContent = document;
    }

    [RelayCommand(CanExecute = nameof(CanUseEditorDocument))]
    private async Task CloseEditorDocumentAsync(EditorTabViewModel? document)
    {
        if (!CanUseEditorDocument(document) || !await document!.ConfirmCloseAsync()) return;
        var wasCurrent = ReferenceEquals(CurrentEditorTabViewModel, document);
        _dockLayoutService.CloseDocument(document);
        DocumentClosed(document);
        if (wasCurrent)
        {
            var next = _dockLayoutService.ActiveDockable as EditorTabViewModel ??
                _dockLayoutService.Documents.OfType<EditorTabViewModel>().LastOrDefault();
            ActiveDockContent = next;
        }
    }

    private bool CanOpenFileFolder(string? path) => _fileLocationService?.CanOpenContainingFolder(path) == true;

    [RelayCommand(CanExecute = nameof(CanOpenFileFolder))]
    private void OpenFileFolder(string? path)
    {
        if (!CanOpenFileFolder(path)) return;
        try { _fileLocationService!.OpenContainingFolder(path!); }
        catch (Exception ex) { _snackbarService.Enqueue($"{CadUiText.Get(LangKeys.OpenContainingFolder)}: {ex.Message}"); }
    }

    [RelayCommand(CanExecute = nameof(CanUseRecoveryEntry))]
    private void OpenRecoveryFolder(CadRecoveryEntry? entry)
    {
        if (entry is not null) OpenFileFolder(Path.Combine(_recoveryStore.DirectoryPath, entry.SnapshotName));
    }
}
