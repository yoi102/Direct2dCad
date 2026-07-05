using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadPasteInteractionController
{
    private ClipboardSnapshot? _clipboard;

    public bool IsPreviewActive { get; private set; }

    public void Copy(CadClipboardInteractionService clipboardService)
    {
        _clipboard = clipboardService.CreateSelectionSnapshot();
    }

    public bool BeginPreview(CadClipboardInteractionService clipboardService)
    {
        if (_clipboard is null)
            Copy(clipboardService);

        if (_clipboard is null)
            return false;

        IsPreviewActive = true;
        return true;
    }

    public IReadOnlyList<EntityId> Commit(CadClipboardInteractionService clipboardService, CadPointD target)
    {
        if (_clipboard is null)
            return [];

        var createdIds = clipboardService.CommitPaste(_clipboard, target);
        IsPreviewActive = false;
        return createdIds;
    }

    public void AddPreview(
        CadClipboardInteractionService clipboardService,
        List<CadTransientItem> items,
        CadPointD mouseWorld)
    {
        clipboardService.AddPastePreview(items, _clipboard, IsPreviewActive, mouseWorld);
    }

    public void Clear(bool clearClipboard)
    {
        IsPreviewActive = false;

        if (clearClipboard)
            _clipboard = null;
    }
}
