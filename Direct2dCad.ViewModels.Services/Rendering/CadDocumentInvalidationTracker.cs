using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadDocumentInvalidationTracker
{
    private const CadEntityChangeKind VisualChanges =
        CadEntityChangeKind.Geometry |
        CadEntityChangeKind.Appearance |
        CadEntityChangeKind.Visibility |
        CadEntityChangeKind.Layer |
        CadEntityChangeKind.Created |
        CadEntityChangeKind.Deleted |
        CadEntityChangeKind.DrawOrder |
        CadEntityChangeKind.Fill |
        CadEntityChangeKind.EmbeddedData |
        CadEntityChangeKind.Opacity |
        CadEntityChangeKind.Rotation;

    private readonly Dictionary<EntityId, CadEntityInvalidationSnapshot> _snapshots = [];
    private CadDocument? _document;

    public void Reset(
        CadDocument document,
        CadRenderInvalidationCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(document);

        _document = document;
        _snapshots.Clear();
        foreach (var entity in document.Entities.Values)
        {
            if (calculator.TryCaptureEntitySnapshot(entity.Id, out var snapshot))
                _snapshots[entity.Id] = snapshot;
        }
    }

    public CadRenderInvalidation CreateInvalidation(
        CadDocument document,
        CadDocumentChangeSet changes,
        CadRenderInvalidationCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);

        if (!ReferenceEquals(_document, document))
        {
            Reset(document, calculator);
            return changes.DocumentChanged
                ? CadRenderInvalidation.Full
                : CadRenderInvalidation.Empty;
        }

        if (changes.AffectsDocumentStructure)
        {
            Reset(document, calculator);
            return CadRenderInvalidation.Full;
        }

        List<CadScreenRect>? dirtyRects = null;
        var requiresFullRender =
            changes.AffectsLayouts ||
            changes.AffectsLayoutStructure ||
            changes.AffectsViewSettings;

        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & VisualChanges) == 0)
                continue;

            var hadPrevious = _snapshots.TryGetValue(change.EntityId, out var previous);
            var hasCurrent = calculator.TryCaptureEntitySnapshot(change.EntityId, out var current);

            if (hadPrevious)
            {
                if (!requiresFullRender && previous.IsRenderable)
                {
                    requiresFullRender |= !TryAddDirtyRects(
                        dirtyRects ??= [],
                        calculator.CreateEntitySnapshotInvalidation(previous));
                }
            }
            else if (!change.Kind.HasFlag(CadEntityChangeKind.Created))
            {
                // A mutation without a prior snapshot cannot safely clear the old pixels.
                requiresFullRender = true;
            }

            if (hasCurrent)
            {
                _snapshots[change.EntityId] = current;
                if (!requiresFullRender && current.IsRenderable)
                {
                    requiresFullRender |= !TryAddDirtyRects(
                        dirtyRects ??= [],
                        calculator.CreateCurrentEntityInvalidation(
                            change.EntityId,
                            current));
                }
            }
            else
            {
                _snapshots.Remove(change.EntityId);
            }
        }

        if (requiresFullRender)
            return CadRenderInvalidation.Full;

        return dirtyRects is null
            ? CadRenderInvalidation.Empty
            : CadRenderInvalidation.FromScreenRects(dirtyRects);
    }

    private static bool TryAddDirtyRects(
        ICollection<CadScreenRect> destination,
        CadRenderInvalidation invalidation)
    {
        if (invalidation.IsFull)
            return false;

        foreach (var rect in invalidation.DirtyScreenRects)
            destination.Add(rect);
        return true;
    }
}
