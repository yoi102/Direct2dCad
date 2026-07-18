using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadOverlaySceneCoordinator
{
    private readonly CadHandleSceneBuilder _handleSceneBuilder = new();
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
        var invalidation = previousTransient.Union(currentTransient);

        if (!updateHandleScene)
            return invalidation;

        var currentHandles = invalidationCalculator.CreateHandleSceneInvalidation(
            HandleScene,
            includeGripHandles);
        _lastHandleInvalidation = currentHandles;
        return invalidation
            .Union(previousHandles)
            .Union(currentHandles);
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
