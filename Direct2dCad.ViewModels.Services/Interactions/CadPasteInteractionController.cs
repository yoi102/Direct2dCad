using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadPasteInteractionController
{
    private readonly ICadClipboardStore _clipboardStore;
    private bool _hasUserCopySnapshot;

    public bool IsPreviewActive { get; private set; }
    public bool HasUserCopySnapshot => _hasUserCopySnapshot && _clipboardStore.Snapshot is not null;
    public CadClipboardSnapshot? Snapshot => _clipboardStore.Snapshot;

    public CadPasteInteractionController(ICadClipboardStore clipboardStore)
    {
        _clipboardStore = clipboardStore ?? throw new ArgumentNullException(nameof(clipboardStore));
    }

    public CadClipboardSnapshot? Copy(CadClipboardInteractionService clipboardService)
    {
        var snapshot = clipboardService.CreateSelectionSnapshot();
        _clipboardStore.Set(snapshot);
        _hasUserCopySnapshot = snapshot is not null;
        return snapshot;
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

    public void SetSnapshot(CadClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _clipboardStore.Set(snapshot);
        IsPreviewActive = true;
        _hasUserCopySnapshot = false;
    }

    public IReadOnlyList<EntityId> Commit(
        CadClipboardInteractionService clipboardService,
        CadPointD target,
        LayerId targetLayerId,
        Func<CadClipboardSnapshot, CadClipboardSnapshot>? prepareSnapshot = null)
    {
        var snapshot = _clipboardStore.Snapshot;
        if (snapshot is null)
            return [];

        if (prepareSnapshot is not null)
        {
            snapshot = prepareSnapshot(snapshot);
            _clipboardStore.Set(snapshot);
        }

        var createdIds = clipboardService.CommitPaste(snapshot, target, targetLayerId);
        IsPreviewActive = false;
        return createdIds;
    }

    public void AddPreview(
        CadClipboardInteractionService clipboardService,
        List<CadTransientItem> items,
        CadPointD mouseWorld,
        LayerId targetLayerId)
    {
        clipboardService.AddPastePreview(items, _clipboardStore.Snapshot, IsPreviewActive, mouseWorld, targetLayerId);
    }

    public void Clear(bool clearClipboard)
    {
        IsPreviewActive = false;

        if (clearClipboard)
        {
            _clipboardStore.Clear();
            _hasUserCopySnapshot = false;
        }
    }
}
