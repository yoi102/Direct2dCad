using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadPasteInteractionController
{
    private readonly ICadClipboardStore _clipboardStore;
    private readonly List<CadTransientItem> _previewTemplateItems = [];
    private CadClipboardSnapshot? _previewTemplateSnapshot;
    private CadDocument? _previewTemplateDocument;
    private LayerId _previewTemplateLayerId;
    private CadTransientStyle _previewTemplateStyle = CadTransientStyle.PastePreview;

    public bool IsPreviewActive { get; private set; }
    public bool HasUserCopySnapshot => _clipboardStore.HasUserCopySnapshot;
    public CadClipboardSnapshot? Snapshot => _clipboardStore.Snapshot;

    public CadPasteInteractionController(ICadClipboardStore clipboardStore)
    {
        _clipboardStore = clipboardStore ?? throw new ArgumentNullException(nameof(clipboardStore));
    }

    public CadClipboardSnapshot? Copy(CadClipboardInteractionService clipboardService)
    {
        var snapshot = clipboardService.CreateSelectionSnapshot();
        _clipboardStore.Set(snapshot, isUserCopySnapshot: true);
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

        _clipboardStore.Set(snapshot, isUserCopySnapshot: false);
        InvalidatePreviewTemplate();
        IsPreviewActive = true;
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
            _clipboardStore.Set(snapshot, _clipboardStore.HasUserCopySnapshot);
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
        var snapshot = _clipboardStore.Snapshot;
        if (!IsPreviewActive || snapshot is null)
            return;

        EnsurePreviewTemplate(clipboardService, snapshot, targetLayerId);
        if (_previewTemplateItems.Count == 0)
            return;

        var delta = mouseWorld - snapshot.BasePoint;
        items.Add(new CadTransientGroup(
            _previewTemplateItems,
            CadMatrixD.CreateTranslation(delta.X, delta.Y),
            _previewTemplateStyle,
            snapshot.Bounds));
    }

    public void InvalidatePreviewTemplate()
    {
        _previewTemplateSnapshot = null;
        _previewTemplateDocument = null;
        _previewTemplateItems.Clear();
    }

    public void Clear(bool clearClipboard)
    {
        IsPreviewActive = false;

        if (clearClipboard)
        {
            _clipboardStore.Clear();
            InvalidatePreviewTemplate();
        }
    }

    private void EnsurePreviewTemplate(
        CadClipboardInteractionService clipboardService,
        CadClipboardSnapshot snapshot,
        LayerId targetLayerId)
    {
        var document = clipboardService.Document;
        if (ReferenceEquals(_previewTemplateSnapshot, snapshot) &&
            ReferenceEquals(_previewTemplateDocument, document) &&
            _previewTemplateLayerId.Equals(targetLayerId))
        {
            return;
        }

        _previewTemplateItems.Clear();
        clipboardService.AddPastePreview(
            _previewTemplateItems,
            snapshot,
            isPastePreviewActive: true,
            snapshot.BasePoint,
            targetLayerId);
        _previewTemplateStyle = CadTransientStyle.PastePreview with
        {
            StrokeWidth = ResolveMaximumStrokeWidth(_previewTemplateItems)
        };
        _previewTemplateSnapshot = snapshot;
        _previewTemplateDocument = document;
        _previewTemplateLayerId = targetLayerId;
    }

    private static double ResolveMaximumStrokeWidth(IReadOnlyList<CadTransientItem> items)
    {
        var maximum = CadTransientStyle.PastePreview.StrokeWidth;
        foreach (var item in items)
        {
            maximum = Math.Max(maximum, item.Style.StrokeWidth);
            if (item is CadTransientGroup group)
                maximum = Math.Max(maximum, ResolveMaximumStrokeWidth(group.Items));
        }

        return maximum;
    }
}
