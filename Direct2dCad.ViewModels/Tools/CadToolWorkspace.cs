using AvalonDock.Core;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tools;

public sealed record CadToolWorkspaceDocument(
    string DocumentId,
    long CadDocumentId,
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
    IDialogService dialogService) : ICadToolWorkspace
{
    private readonly CadDocumentStorage _storage = new();
    private IDockLayoutService DockLayoutService =>
        serviceProvider.GetRequiredService<IDockLayoutService>();

    public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments()
    {
        var dockLayoutService = DockLayoutService;
        var active = dockLayoutService.ActiveDockable;
        return dockLayoutService.Documents
            .OfType<EditorTabViewModel>()
            .Select(tab => Describe(tab, ReferenceEquals(active, tab)))
            .ToArray();
    }

    public CadToolWorkspaceDocument? GetActiveDocument()
    {
        var dockLayoutService = DockLayoutService;
        return dockLayoutService.ActiveDockable is EditorTabViewModel tab
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
        return tab is null
            ? throw new ArgumentException($"Open document not found: {documentId}", nameof(documentId))
            : Describe(tab, ReferenceEquals(dockLayoutService.ActiveDockable, tab));
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
        RefreshDocumentExplorer();
        return Describe(tab, isActive: true);
    }

    public bool ActivateDocument(string documentId)
    {
        var document = GetRequiredDocument(documentId);
        DockLayoutService.ActiveDockable = document.EditorTab;
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

        DockLayoutService.CloseDocument(document.EditorTab);
        RefreshDocumentExplorer();
        return true;
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
