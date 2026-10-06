using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Application.Tools;

/// <summary>A tool session for scripts and other hosts without a window or rendering backend.</summary>
public sealed class CadToolDocumentSession : ICadToolDocumentSession, IDisposable
{
    public CadToolDocumentSession(CadDocument document)
    {
        CadEditor = new CadEditor(document ?? throw new ArgumentNullException(nameof(document)));
        CadEditor.Viewport.SetSize(1024, 768);
        CadEditor.DocumentChanged += OnDocumentChanged;
    }

    public CadEditor CadEditor { get; }
    public bool IsDisposed { get; private set; }
    public LayerId DrawingLayerId { get; set; } = LayerId.Default;
    public string ToolMode => "Select";
    public double CurrentPointerWorldX { get; set; }
    public double CurrentPointerWorldY { get; set; }
    public LayoutId? ActiveLayoutId { get; private set; }
    public LayoutViewportId? ActiveLayoutViewportId { get; private set; }
    public BlockId? EditingBlockId { get; private set; }
    public string EditingBlockName => EditingBlockId is { } id ? CadEditor.Document.GetBlock(id).Name : "";
    public bool IsEditingBlock => EditingBlockId is not null;
    public bool IsModelSpaceActive => ActiveLayoutId is null;
    public bool IsLayoutViewportActive => ActiveLayoutViewportId is not null;
    public bool IsPaperSpaceActive => ActiveLayoutId is not null && ActiveLayoutViewportId is null;
    public event EventHandler? RenderRequested;

    public void FitToWindow()
    {
        ThrowIfDisposed();
        CadEditor.Execute(new FitViewportCommand(ownerBlockId: CadEditor.ActiveOwnerBlockId));
    }

    public void RequestRender()
    {
        ThrowIfDisposed();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SelectEntities(IEnumerable<EntityId> entityIds)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entityIds);
        var document = CadEditor.Document;
        var ids = entityIds.Where(id => document.TryGetEntity(id, out var entity) &&
            entity is not null && entity.OwnerBlockId == CadEditor.ActiveOwnerBlockId &&
            CadEntityAccessPolicy.IsSelectable(document, entity)).Distinct().ToArray();
        // Tool-result selection is a side effect of the document operation.
        // Only an explicit selection command belongs in editor undo history.
        CadEditor.Selection.Replace(ids);
        RequestRender();
    }

    public void EditBlockDefinition(BlockId blockId)
    {
        ThrowIfDisposed();
        var block = CadEditor.Document.GetBlock(blockId);
        if (block.IsSystem || block.IsReadOnly)
            throw new InvalidOperationException("System or read-only blocks cannot be edited.");
        CadEditor.Selection.Clear();
        EditingBlockId = blockId;
        CadEditor.ActiveOwnerBlockId = blockId;
        RequestRender();
    }

    public void ExitBlockEditing()
    {
        ThrowIfDisposed();
        CadEditor.Selection.Clear();
        EditingBlockId = null;
        CadEditor.ActiveOwnerBlockId = IsPaperSpaceActive
            ? CadEditor.Document.GetLayout(ActiveLayoutId!.Value).PaperSpaceBlockId : BlockId.ModelSpace;
        RequestRender();
    }

    public void ActivateModelSpace()
    {
        ThrowIfDisposed();
        EditingBlockId = null;
        ActiveLayoutId = null;
        ActiveLayoutViewportId = null;
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        CadEditor.Selection.Clear();
        RequestRender();
    }

    public void ActivateLayout(LayoutId layoutId)
    {
        ThrowIfDisposed();
        var layout = CadEditor.Document.GetLayout(layoutId);
        EditingBlockId = null;
        ActiveLayoutId = layout.Id;
        ActiveLayoutViewportId = null;
        CadEditor.ActiveOwnerBlockId = layout.PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        RequestRender();
    }

    public void ActivateLayoutViewport(LayoutViewportId viewportId)
    {
        ThrowIfDisposed();
        if (ActiveLayoutId is not { } id)
            throw new InvalidOperationException("Activate a layout first.");
        var viewport = CadEditor.Document.GetLayout(id).GetViewport(viewportId);
        if (!viewport.IsVisible) throw new InvalidOperationException("A hidden viewport cannot be activated.");
        EditingBlockId = null;
        ActiveLayoutViewportId = viewportId;
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        CadEditor.Selection.Clear();
        RequestRender();
    }

    public void ExitLayoutViewport()
    {
        if (ActiveLayoutId is { } id) ActivateLayout(id);
    }

    public void Dispose()
    {
        IsDisposed = true;
        CadEditor.DocumentChanged -= OnDocumentChanged;
        RenderRequested = null;
    }

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet changes)
    {
        if (ActiveLayoutId is not { } id) return;
        if (!CadEditor.Document.TryGetLayout(id, out var layout) || layout is null)
            ActivateModelSpace();
        else if (ActiveLayoutViewportId is { } viewportId && !layout.Viewports.Any(v => v.Id == viewportId && v.IsVisible))
            ExitLayoutViewport();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
}
