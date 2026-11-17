using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Lang;
using Direct2dCad.Commands;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.HitTesting;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Snapping;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using MessagePipe;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    public void RequestRender()
    {
        RequestRender(CadRenderInvalidation.Full);
    }

    public void RequestRenderCacheRefresh()
    {
        RequestRender();
    }

    public void SetRenderScheduler(Action<Action>? scheduler)
    {
        if (_disposed)
            return;
        _renderScheduler = scheduler;
        _renderScheduled = false;
        _renderSchedulerVersion = unchecked(_renderSchedulerVersion + 1);
        if (scheduler is not null)
            SchedulePendingRender();
    }

    private void RequestOverlayRender(bool updateHandleScene = false)
    {
        if (IsLayoutViewportActive)
        {
            RequestRender(
                CadRenderInvalidation.Full,
                updateHandleScene: updateHandleScene,
                baseSceneChanged: false);
            return;
        }

        RequestRender(
            CadRenderInvalidation.FromScreenRect(default),
            updateHandleScene: updateHandleScene,
            baseSceneChanged: false);
    }

    private void RequestRender(
        CadRenderInvalidation? invalidation,
        bool drawGripHandles = true,
        bool updateHandleScene = true,
        bool baseSceneChanged = true)
    {
        var requestedInvalidation = invalidation ?? CadRenderInvalidation.Full;
        if (_hasPendingRender)
            _pendingRenderInvalidation = _pendingRenderInvalidation.Union(requestedInvalidation);
        else
            _pendingRenderInvalidation = requestedInvalidation;

        _hasPendingRender = true;
        _pendingDrawGripHandles = drawGripHandles;
        _pendingUpdateHandleScene |= updateHandleScene;
        _pendingBaseSceneChanged |= baseSceneChanged;
        SchedulePendingRender();
    }

    private void SchedulePendingRender()
    {
        if (!_hasPendingRender || _renderScheduled)
            return;

        if (_renderScheduler is null)
        {
            FlushPendingRender();
            return;
        }

        _renderScheduled = true;
        var schedulerVersion = _renderSchedulerVersion;
        _renderScheduler(() =>
        {
            if (_disposed || schedulerVersion != _renderSchedulerVersion)
                return;

            _renderScheduled = false;
            FlushPendingRender();
        });
    }

    private void FlushPendingRender()
    {
        if (!_hasPendingRender || _disposed)
            return;

        var invalidation = _pendingRenderInvalidation;
        var drawGripHandles = _pendingDrawGripHandles;
        var updateHandleScene = _pendingUpdateHandleScene;
        var baseSceneChanged = _pendingBaseSceneChanged;
        _hasPendingRender = false;
        _pendingRenderInvalidation = CadRenderInvalidation.Empty;
        _pendingUpdateHandleScene = false;
        _pendingBaseSceneChanged = false;

        RenderCore(
            invalidation,
            drawGripHandles,
            updateHandleScene,
            baseSceneChanged);
    }

    private void RenderCore(
        CadRenderInvalidation? invalidation,
        bool drawGripHandles,
        bool updateHandleScene,
        bool baseSceneChanged)
    {
        var interruptedViewportInteraction =
            RenderSession.IsViewportInteractionActive;
        if (interruptedViewportInteraction)
        {
            RenderSession.EndViewportInteraction();
            _viewportInteractionRequiresHandleSceneUpdate = false;
        }

        UpdateTextMeasurements();
        var requestedInvalidation = interruptedViewportInteraction
            ? CadRenderInvalidation.Full
            : invalidation ?? CadRenderInvalidation.Full;
        if (!_deferredDocumentInvalidation.IsEmpty)
        {
            requestedInvalidation =
                requestedInvalidation.Union(_deferredDocumentInvalidation);
            _deferredDocumentInvalidation = CadRenderInvalidation.Empty;
        }
        var interactionZoom = InteractionZoom;
        var handleOptions = CreateHandleSceneBuildOptions();
        var activeHandleItems = _gripDrag.CreateActiveHandleItems(
            CadEditor,
            handleOptions,
            interactionZoom);
        var transientItems = CreateTransientItems();
        var invalidationCalculator = CreateRenderInvalidationCalculator();
        CadRenderInvalidation effectiveInvalidation;

        if (requestedInvalidation.IsFull)
        {
            _overlayScenes.UpdateOverlayScenes(
                CadEditor,
                transientItems,
                updateHandleScene,
                activeHandleItems,
                handleOptions,
                interactionZoom);
            _overlayScenes.RefreshLastOverlayInvalidation(
                invalidationCalculator,
                drawGripHandles);
            effectiveInvalidation = CadRenderInvalidation.Full;
        }
        else
        {
            var overlayInvalidation = _overlayScenes.UpdateOverlayScenesAndCreateInvalidation(
                invalidationCalculator,
                CadEditor,
                transientItems,
                drawGripHandles,
                updateHandleScene,
                activeHandleItems,
                handleOptions,
                interactionZoom);
            effectiveInvalidation = requestedInvalidation.Union(overlayInvalidation);
        }

        RenderSession.SetRenderOptions(CreateRenderOptions(drawGripHandles));
        RenderSession.Render(effectiveInvalidation, baseSceneChanged);
        RenderFrameTimeMilliseconds = RenderSession.AverageFrameRenderTimeMilliseconds;
        var framesPerSecond = RenderSession.FramesPerSecond;
        if (!RenderFramesPerSecond.Equals(framesPerSecond))
            RenderFramesPerSecond = framesPerSecond;
    }

    private void UpdateTextMeasurements()
    {
        _renderResources.UpdateTextMeasurements(CadEditor, RenderSession);
    }

    private CadRenderOptions CreateRenderOptions(bool drawGripHandles = true)
    {
        return new CadRenderOptions
        {
            ActiveOwnerBlockId = CadEditor.ActiveOwnerBlockId,
            ActiveLayoutId = ActiveLayoutId,
            ActiveLayoutViewportId = ActiveLayoutViewportId,
            DrawGrid = ActiveLayoutId is null,
            DrawOrigin = ActiveLayoutId is null,
            DrawGripHandles = drawGripHandles,
            KeepStrokeWidthScreenConstant = ActiveLayoutId is null,
            EntityLineWeightWorldScale = 1.0,
            IsAntialiasingEnabled = UserSettings.Rendering.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = UserSettings.Rendering.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = UserSettings.Rendering.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback =
                UserSettings.Rendering.AllowApproximateTileScaleFallback,
            IsBackgroundChunkRecordingEnabled =
                UserSettings.Rendering.IsBackgroundChunkRecordingEnabled,
            IsParallelRenderingEnabled =
                UserSettings.Rendering.IsParallelRenderingEnabled,
            ParallelRenderingMode =
                UserSettings.Rendering.ParallelRenderingMode,
            ParallelRenderingWorkerCount =
                UserSettings.Rendering.ParallelRenderingWorkerCount,
            EntityBoundsQuery = _entityBoundsQuery,
            EntityBoundsQueryInto = _entityBoundsQueryInto,
            EntityBoundsCount = _entityBoundsCount,
            HiddenEntityIds = _gripDrag.HiddenEntityIds
        };
    }
}
