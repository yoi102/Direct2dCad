using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadOverlaySceneCoordinator
{
    private readonly CadHandleSceneBuilder _handleSceneBuilder = new();
    private readonly CadHandleSceneBuildBuffer _handleSceneBuildBuffer = new();
    private readonly CadHandleSceneUpdateTracker _handleSceneUpdateTracker = new();
    private CadRenderInvalidation _lastTransientInvalidation = CadRenderInvalidation.FromScreenRect(default);
    private CadRenderInvalidation _lastHandleInvalidation = CadRenderInvalidation.FromScreenRect(default);

    public CadTransientScene TransientScene { get; } = new();

    public CadHandleScene HandleScene { get; } = new();

    public void ClearTransientScene()
    {
        TransientScene.Clear();
    }

    public void ClearHandleScene()
    {
        HandleScene.Clear();
        _handleSceneUpdateTracker.Reset();
    }

    public void ApplyDocumentChanges(
        CadDocumentChangeSet changes,
        IReadOnlySet<EntityId> selectedEntityIds)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        if (changes.AffectsDocumentStructure ||
            changes.AffectsLayouts ||
            changes.AffectsLayoutStructure)
        {
            _handleSceneUpdateTracker.Invalidate();
            return;
        }

        const CadEntityChangeKind handleChanges =
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.Rotation;
        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & handleChanges) != 0 &&
                selectedEntityIds.Contains(change.EntityId))
            {
                _handleSceneUpdateTracker.Invalidate();
                return;
            }
        }
    }

    public void UpdateOverlayScenes(
        CadEditor editor,
        IReadOnlyList<CadTransientItem> transientItems,
        bool updateHandleScene,
        IReadOnlyList<CadHandleItem>? activeHandleItems,
        CadHandleSceneBuildOptions handleOptions,
        double interactionZoom)
    {
        TransientScene.Replace(transientItems);

        if (updateHandleScene)
            UpdateHandleScene(editor, activeHandleItems, handleOptions, interactionZoom);
    }

    public CadRenderInvalidation UpdateOverlayScenesAndCreateInvalidation(
        CadRenderInvalidationCalculator invalidationCalculator,
        CadEditor editor,
        IReadOnlyList<CadTransientItem> transientItems,
        bool includeGripHandles,
        bool updateHandleScene,
        IReadOnlyList<CadHandleItem>? activeHandleItems,
        CadHandleSceneBuildOptions handleOptions,
        double interactionZoom)
    {
        var previousTransient = _lastTransientInvalidation;
        var previousHandles = _lastHandleInvalidation;
        UpdateOverlayScenes(editor, transientItems, updateHandleScene, activeHandleItems, handleOptions, interactionZoom);

        var currentTransient = invalidationCalculator.CreateTransientSceneInvalidation(TransientScene);
        _lastTransientInvalidation = currentTransient;
        var invalidation = previousTransient.UnionPreservingCoverage(currentTransient);

        if (!updateHandleScene)
            return invalidation;

        var currentHandles = invalidationCalculator.CreateHandleSceneInvalidation(
            HandleScene,
            includeGripHandles);
        _lastHandleInvalidation = currentHandles;
        return invalidation
            .UnionPreservingCoverage(previousHandles)
            .UnionPreservingCoverage(currentHandles);
    }

    public void RefreshLastOverlayInvalidation(
        CadRenderInvalidationCalculator invalidationCalculator,
        bool includeGripHandles)
    {
        _lastTransientInvalidation =
            invalidationCalculator.CreateTransientSceneInvalidation(TransientScene);
        _lastHandleInvalidation =
            invalidationCalculator.CreateHandleSceneInvalidation(HandleScene, includeGripHandles);
    }

    public void UpdateHandleScene(
        CadEditor editor,
        IReadOnlyList<CadHandleItem>? activeHandleItems,
        CadHandleSceneBuildOptions handleOptions,
        double interactionZoom)
    {
        if (activeHandleItems is { Count: > 0 })
        {
            HandleScene.Replace(activeHandleItems);
            _handleSceneUpdateTracker.Invalidate();
            return;
        }

        var selection = editor.Selection;
        var includeIndividualGrips =
            handleOptions.IncludeGripHandles &&
            selection.Count <= Math.Max(0, handleOptions.MaximumIndividualGripEntityCount);
        var effectiveOptions = handleOptions with
        {
            RotationHandleOffset = includeIndividualGrips
                ? 28.0 / Math.Max(interactionZoom, double.Epsilon)
                : 0
        };
        if (_handleSceneUpdateTracker.IsCurrent(
                editor.Document,
                selection.Version,
                effectiveOptions))
        {
            return;
        }

        var items = _handleSceneBuilder.BuildSelectionHandles(
            editor.Document,
            selection.EntityIds,
            _handleSceneBuildBuffer,
            HandleScene,
            effectiveOptions);
        HandleScene.Replace(items);
        _handleSceneUpdateTracker.MarkCurrent(
            editor.Document,
            selection.Version,
            effectiveOptions);
    }
}
