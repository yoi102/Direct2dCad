using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadOverlaySceneCoordinator
{
    private readonly CadHandleSceneBuilder _handleSceneBuilder = new();
    private CadRenderInvalidation _lastOverlayInvalidation = CadRenderInvalidation.FromScreenRect(default);

    public CadTransientScene TransientScene { get; } = new();

    public CadHandleScene HandleScene { get; } = new();

    public void ClearTransientScene()
    {
        TransientScene.Clear();
    }

    public void ClearHandleScene()
    {
        HandleScene.Clear();
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
        var previousOverlay = _lastOverlayInvalidation;
        UpdateOverlayScenes(editor, transientItems, updateHandleScene, activeHandleItems, handleOptions, interactionZoom);
        var currentOverlay = CreateOverlayInvalidation(invalidationCalculator, includeGripHandles);
        _lastOverlayInvalidation = currentOverlay;
        return previousOverlay.Union(currentOverlay);
    }

    public void RefreshLastOverlayInvalidation(
        CadRenderInvalidationCalculator invalidationCalculator,
        bool includeGripHandles)
    {
        _lastOverlayInvalidation = CreateOverlayInvalidation(invalidationCalculator, includeGripHandles);
    }

    public CadRenderInvalidation CreateOverlayInvalidation(
        CadRenderInvalidationCalculator invalidationCalculator,
        bool includeGripHandles)
    {
        return invalidationCalculator.CreateOverlayInvalidation(
            TransientScene,
            HandleScene,
            includeGripHandles);
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
            return;
        }

        var effectiveOptions = handleOptions with
        {
            RotationHandleOffset = 28.0 / Math.Max(interactionZoom, double.Epsilon)
        };
        var items = _handleSceneBuilder.BuildSelectionHandles(
            editor.Document,
            editor.Selection.EntityIds,
            effectiveOptions);
        HandleScene.Replace(items);
    }
}
