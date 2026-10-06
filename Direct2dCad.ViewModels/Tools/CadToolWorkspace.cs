using AvalonDock.Core;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tools;

internal sealed class CadToolWorkspace(
    IServiceProvider serviceProvider,
    IEditorTabFactory editorTabFactory,
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
            () => editorTabFactory.Create(created =>
            {
                if (!string.IsNullOrWhiteSpace(name) && !created.TryRenameDocument(name))
                    throw new ArgumentException("Document name cannot be empty.", nameof(name));
            }));

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
            () => editorTabFactory.Create(created => created.Load(document, fullPath)));
        dockLayoutService.ActiveDockable = tab;
        activeEditorContext.SetCurrent(tab);
        RefreshDocumentExplorer();
        return Describe(tab, isActive: true);
    }

    public bool ActivateDocument(string documentId)
    {
        var tab = (EditorTabViewModel)GetRequiredDocument(documentId).Host;
        DockLayoutService.ActiveDockable = tab;
        activeEditorContext.SetCurrent(tab);
        return true;
    }

    public bool RenameDocument(string documentId, string name)
    {
        var tab = (EditorTabViewModel)GetRequiredDocument(documentId).Host;
        var renamed = tab.TryRenameDocument(name);
        if (renamed)
            RefreshDocumentExplorer();
        return renamed;
    }

    public async Task<bool> SaveDocumentAsync(
        string documentId,
        string? filePath,
        CancellationToken cancellationToken)
    {
        var tab = (EditorTabViewModel)GetRequiredDocument(documentId).Host;
        return string.IsNullOrWhiteSpace(filePath)
            ? await tab.SaveForWorkspaceToolAsync(cancellationToken)
            : await tab.SaveToFileForWorkspaceToolAsync(Path.GetFullPath(filePath), cancellationToken);
    }

    public async Task<bool> CloseDocumentAsync(string documentId)
    {
        var tab = (EditorTabViewModel)GetRequiredDocument(documentId).Host;
        if (!await tab.ConfirmCloseAsync())
            return false;

        var dockLayoutService = DockLayoutService;
        var wasCurrent = ReferenceEquals(activeEditorContext.Current, tab);
        dockLayoutService.CloseDocument(tab);
        try
        {
            tab.Dispose();
        }
        finally
        {
            if (wasCurrent)
            {
                var next = dockLayoutService.ActiveDockable as EditorTabViewModel ??
                           dockLayoutService.Documents
                               .OfType<EditorTabViewModel>()
                               .LastOrDefault();
                activeEditorContext.SetCurrent(next);
            }
            RefreshDocumentExplorer();
        }
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
