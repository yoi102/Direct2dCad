using AvalonDock.Core;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tools;

public sealed record CadToolWorkspaceDocument(
    string DocumentId,
    Guid CadDocumentId,
    string Name,
    string FilePath,
    bool IsModified,
    bool IsActive,
    EditorTabViewModel EditorTab)
{
    public CadDocumentViewModel DocumentViewModel => EditorTab.CadDocumentViewModel;
}

public interface ICadToolWorkspace
{
    IReadOnlyList<CadToolWorkspaceDocument> GetDocuments();
    CadToolWorkspaceDocument? GetActiveDocument();
    CadToolWorkspaceDocument GetRequiredDocument(string documentId);
    CadToolWorkspaceDocument CreateDocument(string? name);
    Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken);
    bool ActivateDocument(string documentId);
    bool RenameDocument(string documentId, string name);
    Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken cancellationToken);
    Task<bool> CloseDocumentAsync(string documentId);
}

internal sealed class CadToolWorkspace(
    IServiceProvider serviceProvider,
    IDialogService dialogService,
    IActiveEditorContext activeEditorContext) : ICadToolWorkspace
{
    private readonly CadDocumentStorage _storage = new();
    private IDockLayoutService DockLayoutService =>
        serviceProvider.GetRequiredService<IDockLayoutService>();

    public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments()
    {
        var dockLayoutService = DockLayoutService;
        var active = ResolveActiveEditor(dockLayoutService);
        return dockLayoutService.Documents
            .OfType<EditorTabViewModel>()
            .Select(tab => Describe(tab, ReferenceEquals(active, tab)))
            .ToArray();
    }

    public CadToolWorkspaceDocument? GetActiveDocument()
    {
        var dockLayoutService = DockLayoutService;
        return ResolveActiveEditor(dockLayoutService) is { } tab
            ? Describe(tab, isActive: true)
            : null;
    }

    public CadToolWorkspaceDocument GetRequiredDocument(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        var dockLayoutService = DockLayoutService;
        var tab = dockLayoutService.Documents
            .OfType<EditorTabViewModel>()
            .FirstOrDefault(candidate => string.Equals(candidate.ContentId, documentId, StringComparison.Ordinal));
        var active = ResolveActiveEditor(dockLayoutService);
        return tab is null
            ? throw new ArgumentException($"Open document not found: {documentId}", nameof(documentId))
            : Describe(tab, ReferenceEquals(active, tab));
    }

    public CadToolWorkspaceDocument CreateDocument(string? name)
    {
        var dockLayoutService = DockLayoutService;
        var tab = dockLayoutService.OpenOrActivateDocument(
            _ => false,
            () => serviceProvider.GetRequiredService<EditorTabViewModel>());
        if (!string.IsNullOrWhiteSpace(name) && !tab.TryRenameDocument(name))
            throw new ArgumentException("Document name cannot be empty.", nameof(name));

        dockLayoutService.ActiveDockable = tab;
        activeEditorContext.SetCurrent(tab);
        RefreshDocumentExplorer();
        return Describe(tab, isActive: true);
    }

    public async Task<CadToolWorkspaceDocument> OpenDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var dockLayoutService = DockLayoutService;
        var fullPath = Path.GetFullPath(filePath);
        var existing = dockLayoutService.Documents
            .OfType<EditorTabViewModel>()
            .FirstOrDefault(tab => string.Equals(tab.CurrentFilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            dockLayoutService.ActiveDockable = existing;
            activeEditorContext.SetCurrent(existing);
            return Describe(existing, isActive: true);
        }

        Direct2dCad.Db.Cad.CadDocument document;
        using (dialogService.ShowProgressBarDialog())
            document = await _storage.LoadAsync(fullPath, cancellationToken);

        var tab = dockLayoutService.OpenOrActivateDocument(
            candidate => string.Equals(candidate.CurrentFilePath, fullPath, StringComparison.OrdinalIgnoreCase),
            () =>
            {
                var created = serviceProvider.GetRequiredService<EditorTabViewModel>();
                created.Load(document, fullPath);
                return created;
            });
        dockLayoutService.ActiveDockable = tab;
        activeEditorContext.SetCurrent(tab);
        RefreshDocumentExplorer();
        return Describe(tab, isActive: true);
    }

    public bool ActivateDocument(string documentId)
    {
        var document = GetRequiredDocument(documentId);
        DockLayoutService.ActiveDockable = document.EditorTab;
        activeEditorContext.SetCurrent(document.EditorTab);
        return true;
    }

    public bool RenameDocument(string documentId, string name)
    {
        var document = GetRequiredDocument(documentId);
        var renamed = document.EditorTab.TryRenameDocument(name);
        if (renamed)
            RefreshDocumentExplorer();
        return renamed;
    }

    public async Task<bool> SaveDocumentAsync(
        string documentId,
        string? filePath,
        CancellationToken cancellationToken)
    {
        var document = GetRequiredDocument(documentId);
        return string.IsNullOrWhiteSpace(filePath)
            ? await document.EditorTab.SaveForWorkspaceToolAsync(cancellationToken)
            : await document.EditorTab.SaveToFileForWorkspaceToolAsync(Path.GetFullPath(filePath), cancellationToken);
    }

    public async Task<bool> CloseDocumentAsync(string documentId)
    {
        var document = GetRequiredDocument(documentId);
        if (!await document.EditorTab.ConfirmCloseAsync())
            return false;

        var dockLayoutService = DockLayoutService;
        var wasCurrent = ReferenceEquals(activeEditorContext.Current, document.EditorTab);
        dockLayoutService.CloseDocument(document.EditorTab);
        if (wasCurrent)
        {
            var next = dockLayoutService.ActiveDockable as EditorTabViewModel ??
                       dockLayoutService.Documents
                           .OfType<EditorTabViewModel>()
                           .LastOrDefault();
            activeEditorContext.SetCurrent(next);
        }

        RefreshDocumentExplorer();
        return true;
    }

    private EditorTabViewModel? ResolveActiveEditor(IDockLayoutService dockLayoutService)
    {
        if (dockLayoutService.ActiveDockable is EditorTabViewModel active)
        {
            activeEditorContext.SetCurrent(active);
            return active;
        }

        var remembered = activeEditorContext.Current;
        return remembered is not null && dockLayoutService.Documents.Any(
            document => ReferenceEquals(document, remembered))
            ? remembered
            : null;
    }

    private void RefreshDocumentExplorer()
    {
        DockLayoutService.GetAnchorable<DocumentExplorerToolboxViewModel>()?.RefreshDocuments();
    }

    private static CadToolWorkspaceDocument Describe(EditorTabViewModel tab, bool isActive) => new(
        tab.ContentId,
        tab.CadDocumentViewModel.CadEditor.Document.Id.Value,
        tab.DocumentName,
        tab.CurrentFilePath,
        tab.IsModified,
        isActive,
        tab);
}
