using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Tests;

internal sealed class RecordingDialogService : IDialogService
{
    public UnsavedDocumentDialogResult CloseResult { get; set; } = UnsavedDocumentDialogResult.Cancel;
    public GridSpacingPresetDialogResult? GridResult { get; set; }
    public int CloseRequests { get; private set; }
    public bool ConfirmationResult { get; set; }
    public Task<bool>? PendingConfirmation { get; set; }
    public int OpenProgressCount { get; private set; }
    public List<string> Errors { get; } = [];
    public IReadOnlyList<UnsavedDocumentInfo> UnsavedDocuments { get; private set; } = [];
    public IUserSettingsDialogViewModel? UserSettings { get; private set; }
    public IDocumentSettingsDialogViewModel? DocumentSettings { get; private set; }
    public void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) { }
    public Task ShowOrReplaceMessageDialogAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }
    public Task<bool> ShowOrReplaceMessageDialogWithCancelAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => PendingConfirmation ?? Task.FromResult(ConfirmationResult);
    public IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        OpenProgressCount++;
        return new ProgressScope(this);
    }
    public Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(false);
    public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentDialogAsync(string documentName, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        CloseRequests++;
        return Task.FromResult(CloseResult);
    }
    public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentsDialogAsync(IReadOnlyList<UnsavedDocumentInfo> documents, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        CloseRequests++;
        UnsavedDocuments = documents;
        return Task.FromResult(CloseResult);
    }
    public Task<GridSpacingPresetDialogResult?> ShowGridSpacingPresetDialogAsync(GridSpacingPresetDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.DocumentSettingsDialogHost) => Task.FromResult(GridResult);
    public Task<CreateBlockDialogResult?> ShowCreateBlockDialogAsync(CreateBlockDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult<CreateBlockDialogResult?>(null);
    public Task<AiAssistantSettingsDialogResult?> ShowAiAssistantSettingsDialogAsync(AiAssistantSettingsDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult<AiAssistantSettingsDialogResult?>(null);
    public void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel viewModel) => DocumentSettings = viewModel;
    public void ShowUserSettingsDialog(IUserSettingsDialogViewModel viewModel) => UserSettings = viewModel;

    private sealed class ProgressScope(RecordingDialogService owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.OpenProgressCount--;
        }
    }
}

internal sealed class RecordingWorkspaceStore : IWorkspaceSettingsStore
{
    public CadDocumentWorkspaceSettings LoadDocument(string documentFilePath) => new();
    public void SaveDocument(string documentFilePath, CadDocumentWorkspaceSettings settings) { }
}

internal sealed class RecordingFileDialogs : IFileDialogService
{
    public string? SavePath { get; set; }
    public string? OpenPath { get; set; }
    public string? SaveAsD2cad(string fileName) => SavePath;
    public string? OpenD2cadFile() => OpenPath;
    public string? OpenFile() => null;
    public string? OpenImageFile() => null;
}

internal sealed class RecordingDocumentWriter : ICadDocumentWriter
{
    public Exception? Failure { get; set; }
    public int Writes { get; private set; }
    public Task SaveAsync(CadDocument document, string filePath, CadSnapshotCaptureOptions capture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) return Task.FromException(Failure);
        Assert.True(capture.IsCurrent());
        Writes++;
        return Task.CompletedTask;
    }
}
