using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadPasteInteractionController
{
    private readonly ICadClipboardStore _clipboardStore;

    public bool IsPreviewActive { get; private set; }

    public CadPasteInteractionController(ICadClipboardStore clipboardStore)
    {
        _clipboardStore = clipboardStore ?? throw new ArgumentNullException(nameof(clipboardStore));
    }

    public void Copy(CadClipboardInteractionService clipboardService)
    {
        _clipboardStore.Set(clipboardService.CreateSelectionSnapshot());
    }

    public bool BeginPreview(CadClipboardInteractionService clipboardService)
    {
        if (_clipboardStore.Snapshot is null)
            Copy(clipboardService);

        if (_clipboardStore.Snapshot is null)
            return false;

        IsPreviewActive = true;
        return true;
    }

    public IReadOnlyList<EntityId> Commit(CadClipboardInteractionService clipboardService, CadPointD target)
    {
        var snapshot = _clipboardStore.Snapshot;
        if (snapshot is null)
            return [];

        var createdIds = clipboardService.CommitPaste(snapshot, target);
        IsPreviewActive = false;
        return createdIds;
    }

    public void AddPreview(
        CadClipboardInteractionService clipboardService,
        List<CadTransientItem> items,
        CadPointD mouseWorld)
    {
        clipboardService.AddPastePreview(items, _clipboardStore.Snapshot, IsPreviewActive, mouseWorld);
    }

    public void Clear(bool clearClipboard)
    {
        IsPreviewActive = false;

        if (clearClipboard)
            _clipboardStore.Clear();
    }
}
