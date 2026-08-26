using System.Diagnostics;
using System.Numerics;
using SharpGen.Runtime;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

public sealed class Direct2DImageRenderHost : ICadGeometryResourceManager, IDisposable
{
    private const double DirtyRegionPassPenalty = 96.0;
    private const double InteractionPreviewMaxExposedAreaRatio = 0.55;
    private const int InteractionPreviewSeamOverlapPixels = 2;
    private readonly ImageSourceDirect2DResource _target;
    private readonly Direct2DSceneRender _renderer = new();
    private readonly Direct2DMultiDeviceSceneRenderer _multiDeviceRenderer = new();
    private readonly Direct2DSharedDeviceSceneRenderer _sharedDeviceRenderer = new();
    private readonly Direct2DDirtyRegionPlanner _dirtyRegionPlanner = new();
    private readonly Direct2DFrameRateTracker _frameRateTracker = new();
    private readonly HashSet<EntityId> _pendingTextMeasurementIds = [];
    private readonly List<CadScreenRect> _snapshotExposedRects = new(4);
    private readonly List<EntityId> _dirtyCostCandidateIds = new(256);
    private readonly CadViewport _interactionPreviewViewport = new();
    private ID3D11ImageSource? _imageSource;
    private CadDocument? _document;
    private CadViewport? _viewport;
    private CadTransientScene? _transientScene;
    private CadHandleScene? _handleScene;
    private CadRenderOptions _renderOptions = new();
    private ID2D1DeviceContext? _clearBrushContext;
    private ID2D1SolidColorBrush? _clearBrush;
    private Color4 _clearBrushColor;
    private ViewportInteractionSnapshot? _viewportInteractionSnapshot;
    private BaseSceneState _baseSceneState;
    private CadDocument? _baseSceneDocument;
    private bool _baseSceneValid;
    private bool _baseSceneDirty = true;
    private bool _deferInitialPresentationUntilResourcesReady;
    private bool _hasRenderedFrame;
    private bool _disposed;

    public Direct2DImageRenderHost()
        : this(endDrawOverride: null)
    {
    }

    internal Direct2DImageRenderHost(
        Func<ID2D1DeviceContext, Result>? endDrawOverride)
    {
        _target = new ImageSourceDirect2DResource(
            endDrawOverride,
            BeforeDeviceResourcesReleased);
    }

    public ICadGeometryResourceManager GeometryResourceManager => this;

    public int TargetWidth => _target.Width;

    public int TargetHeight => _target.Height;

    public double FramesPerSecond => _frameRateTracker.FramesPerSecond;

    public double LastFrameRenderTimeMilliseconds =>
        _frameRateTracker.LastFrameRenderTimeMilliseconds;

    public double AverageFrameRenderTimeMilliseconds =>
        _frameRateTracker.AverageFrameRenderTimeMilliseconds;

    public double LastFullFrameRenderTimeMilliseconds =>
        _frameRateTracker.LastFullFrameRenderTimeMilliseconds;

    public CadRenderStatistics RenderStatistics { get; private set; } = CadRenderStatistics.Empty;

    public event EventHandler? RenderCacheBuildRequested;

    public bool IsViewportInteractionActive => _viewportInteractionSnapshot is not null;

    public CadGraphicsDeviceMode GraphicsDeviceMode => _target.GraphicsDeviceMode;

    public bool UsingWarp => _target.UsingWarp;

    public Color4 FallbackBackgroundColor { get; set; } = new(0.08f, 0.09f, 0.10f, 1.0f);

    public CadDocumentChangeSet UpdateTextMeasurements(CadDocument document)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);

        if (_pendingTextMeasurementIds.Count == 0)
            return CadDocumentChangeSet.Empty;

        var changedIds = new List<EntityId>(_pendingTextMeasurementIds.Count);
        foreach (var entityId in _pendingTextMeasurementIds.ToArray())
        {
            if (!document.TryGetEntity(entityId, out var entity) ||
                entity is not CadText text ||
                text.IsErased ||
                !text.RequiresBoundsMeasurement)
            {
                _pendingTextMeasurementIds.Remove(entityId);
                continue;
            }

            if (Direct2DTextServices.TryMeasureTextBounds(
                    _target.DwriteFactory,
                    document,
                    text,
                    out var localBounds) &&
                text.SetLocalBounds(localBounds))
            {
                changedIds.Add(text.Id);
                _pendingTextMeasurementIds.Remove(entityId);
            }
        }

        return changedIds.Count == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(changedIds, CadEntityChangeKind.Geometry);
    }

    public bool TryMeasureTextBounds(
        CadDocument document,
        string text,
        CadPointD position,
        double height,
        StyleId? textStyleId,
        out CadRectD bounds)
    {
        ThrowIfDisposed();

        if (Direct2DTextServices.TryMeasureTextBounds(
                _target.DwriteFactory,
                document,
                text,
                height,
                textStyleId,
                out var localBounds))
        {
            bounds = localBounds.Translate(position - CadPointD.Origin);
            return true;
        }

        bounds = CadRectD.Empty;
        return false;
    }

    public void AttachImageSource(ID3D11ImageSource imageSource)
    {
        ThrowIfDisposed();

        _imageSource = imageSource ?? throw new ArgumentNullException(nameof(imageSource));
        _target.SetTarget(_imageSource);
        _hasRenderedFrame = false;
        InvalidateBaseScene(releaseSnapshot: true);
        EndViewportInteraction();
        ResetRendererDeviceResources();
    }

    public void SetScene(CadDocument document, CadViewport viewport)
    {
        ThrowIfDisposed();

        _document = document ?? throw new ArgumentNullException(nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _hasRenderedFrame = false;
        _deferInitialPresentationUntilResourcesReady = true;
        InvalidateBaseScene(releaseSnapshot: true);
        EndViewportInteraction();
        RefreshPendingTextMeasurements(document);
        ResetRendererDeviceResources();
    }

    public void RebuildAll(CadDocument document)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);

        RefreshPendingTextMeasurements(document);
        _baseSceneDirty = true;
        ResetParallelRenderers();
        _renderer.RebuildAll(document);
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);

        TrackPendingTextMeasurements(document, changes);
        _baseSceneDirty |= changes.DocumentChanged;
        if (changes.DocumentChanged)
            ResetParallelRenderers();
        _renderer.ApplyChanges(document, changes);
    }

    public void RebuildEntity(CadDocument document, EntityId entityId)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);

        TrackPendingTextMeasurement(document, entityId);
        _baseSceneDirty = true;
        ResetParallelRenderers();
        _renderer.RebuildEntity(document, entityId);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();

        _pendingTextMeasurementIds.Remove(entityId);
        _baseSceneDirty = true;
        ResetParallelRenderers();
        _renderer.RemoveEntity(entityId);
    }

    public void SetTransientScene(CadTransientScene? transientScene)
    {
        ThrowIfDisposed();

        _transientScene = transientScene;
    }

    public void SetHandleScene(CadHandleScene? handleScene)
    {
        ThrowIfDisposed();

        _handleScene = handleScene;
    }

    public void SetRenderOptions(CadRenderOptions? renderOptions)
    {
        ThrowIfDisposed();

        var nextOptions = renderOptions ?? new CadRenderOptions();
        if (_renderOptions.IsParallelRenderingEnabled !=
                nextOptions.IsParallelRenderingEnabled ||
            _renderOptions.ParallelRenderingMode !=
                nextOptions.ParallelRenderingMode ||
            _renderOptions.ParallelRenderingWorkerCount !=
                nextOptions.ParallelRenderingWorkerCount)
        {
            ResetParallelRenderers();
        }
        _renderOptions = nextOptions;
    }

    public void SetGraphicsDeviceMode(CadGraphicsDeviceMode mode)
    {
        ThrowIfDisposed();
        _target.SetGraphicsDeviceMode(mode);
        ResetRendererDeviceResources();
    }

    public void SetOleDrawCallback(Direct2DOleDrawCallback? callback)
    {
        ThrowIfDisposed();
        _renderer.OleDrawCallback = callback;
    }

    public void SetOleReleaseCallback(Direct2DOleReleaseCallback? callback)
    {
        ThrowIfDisposed();
        _renderer.OleReleaseCallback = callback;
    }

    public void InvalidateOleBitmap(EntityId entityId)
    {
        ThrowIfDisposed();
        _renderer.InvalidateOleBitmap(entityId);
    }

    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0)
            return;

        var sizeChanged = width != _target.Width || height != _target.Height;
        if (sizeChanged)
            ResetParallelRenderers();
        _imageSource?.SetSize(width, height);

        if (_imageSource is not null)
            _target.SetSize(width, height);
        if (sizeChanged)
            InvalidateBaseScene(releaseSnapshot: false);
    }

    public bool BeginViewportInteraction()
    {
        ThrowIfDisposed();
        if (_viewportInteractionSnapshot is not null)
            return true;
        if (!_hasRenderedFrame || _viewport is null || !_target.IsTargetReady)
            return false;
        if (!_target.CaptureFrameSnapshot())
            return false;

        _viewportInteractionSnapshot = new ViewportInteractionSnapshot(
            _viewport.Zoom,
            _viewport.Offset);
        return true;
    }

    public bool RenderViewportInteractionPreview()
    {
        ThrowIfDisposed();
        if (_viewportInteractionSnapshot is not { } snapshot ||
            _viewport is null ||
            snapshot.Zoom <= double.Epsilon ||
            !double.IsFinite(snapshot.Zoom) ||
            !double.IsFinite(_viewport.Zoom))
        {
            return false;
        }

        var scale = _viewport.Zoom / snapshot.Zoom;
        if (!double.IsFinite(scale) || scale <= double.Epsilon)
            return false;

        var translationX = _viewport.Offset.X - snapshot.Offset.X * scale;
        var translationY = _viewport.Offset.Y - snapshot.Offset.Y * scale;
        if (!double.IsFinite(translationX) || !double.IsFinite(translationY))
            return false;

        var isPanPreview = Math.Abs(scale - 1.0) <= 1e-6;
        var previewViewport = _viewport;
        if (isPanPreview)
        {
            translationX = QuantizePanPreviewTranslation(translationX);
            translationY = QuantizePanPreviewTranslation(translationY);
            _interactionPreviewViewport.SetSize(
                _viewport.ViewWidth,
                _viewport.ViewHeight);
            _interactionPreviewViewport.SetView(
                _viewport.Zoom,
                new CadPointD(
                    snapshot.Offset.X * scale + translationX,
                    snapshot.Offset.Y * scale + translationY));
            previewViewport = _interactionPreviewViewport;
        }

        var background = _document is null
            ? FallbackBackgroundColor
            : ToColor4(_document.ViewSettings.BackgroundColor);
        var transform =
            System.Numerics.Matrix3x2.CreateScale((float)scale) *
            System.Numerics.Matrix3x2.CreateTranslation(
                (float)translationX,
                (float)translationY);
        var exposedRects = ResolveSnapshotExposedRects(
            _target.Width,
            _target.Height,
            scale,
            translationX,
            translationY,
            _snapshotExposedRects);
        if (exposedRects is null)
            return false;

        var frameStartTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var frameStarted = false;
            if (exposedRects.Count > 0)
            {
                if (_document is not null)
                {
                    _renderer.PrepareOleTiles(
                        _document,
                        previewViewport,
                        _transientScene,
                        _renderOptions);
                }
                _renderer.BeginFrame();
                frameStarted = true;
            }

            try
            {
                if (!_target.DrawFrameSnapshot(
                        transform,
                        background,
                        isPanPreview
                            ? InterpolationMode.NearestNeighbor
                            : InterpolationMode.Linear,
                        exposedRects.Count > 0
                            ? context => DrawSnapshotExposedRegions(
                                context,
                                exposedRects,
                                previewViewport)
                            : null))
                {
                    return false;
                }
            }
            finally
            {
                if (frameStarted)
                    _renderer.CompleteFrame();
            }

            if (isPanPreview || scale < 1.0)
            {
                if (!_target.CaptureFrameSnapshot())
                    return false;
                _viewportInteractionSnapshot = new ViewportInteractionSnapshot(
                    previewViewport.Zoom,
                    previewViewport.Offset);
            }

            _frameRateTracker.Record(frameStartTimestamp, isFullFrame: false);
            return true;
        }
        catch (Direct2DDeviceResourcesRecreatedException)
        {
            EndViewportInteraction();
            _hasRenderedFrame = false;
            ResetRendererDeviceResources();
            return false;
        }
    }

    internal static IReadOnlyList<CadScreenRect>? ResolveSnapshotExposedRects(
        int targetWidth,
        int targetHeight,
        double scale,
        double translationX,
        double translationY,
        List<CadScreenRect>? result = null)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
            return [];

        var snapshotLeft = translationX;
        var snapshotTop = translationY;
        var snapshotRight = translationX + targetWidth * scale;
        var snapshotBottom = translationY + targetHeight * scale;
        if (!double.IsFinite(snapshotLeft) ||
            !double.IsFinite(snapshotTop) ||
            !double.IsFinite(snapshotRight) ||
            !double.IsFinite(snapshotBottom))
        {
            return null;
        }

        var coveredLeft = (int)Math.Ceiling(
            Math.Clamp(snapshotLeft, 0.0, targetWidth));
        var coveredTop = (int)Math.Ceiling(
            Math.Clamp(snapshotTop, 0.0, targetHeight));
        var coveredRight = (int)Math.Floor(
            Math.Clamp(snapshotRight, 0.0, targetWidth));
        var coveredBottom = (int)Math.Floor(
            Math.Clamp(snapshotBottom, 0.0, targetHeight));
        if (coveredRight <= coveredLeft || coveredBottom <= coveredTop)
            return null;

        result ??= [];
        result.Clear();
        if (coveredTop > 0)
        {
            result.Add(new CadScreenRect(
                0,
                0,
                targetWidth,
                Math.Min(
                    targetHeight,
                    coveredTop + InteractionPreviewSeamOverlapPixels)));
        }

        var bottomStart = targetHeight;
        if (coveredBottom < targetHeight)
        {
            bottomStart = Math.Max(
                0,
                coveredBottom - InteractionPreviewSeamOverlapPixels);
            result.Add(new CadScreenRect(
                0,
                bottomStart,
                targetWidth,
                targetHeight - bottomStart));
        }

        var middleTop = coveredTop > 0
            ? Math.Min(
                targetHeight,
                coveredTop + InteractionPreviewSeamOverlapPixels)
            : 0;
        var middleBottom = coveredBottom < targetHeight
            ? bottomStart
            : targetHeight;
        var middleHeight = Math.Max(0, middleBottom - middleTop);
        if (coveredLeft > 0 && middleHeight > 0)
        {
            result.Add(new CadScreenRect(
                0,
                middleTop,
                Math.Min(
                    targetWidth,
                    coveredLeft + InteractionPreviewSeamOverlapPixels),
                middleHeight));
        }
        if (coveredRight < targetWidth && middleHeight > 0)
        {
            var rightStart = Math.Max(
                0,
                coveredRight - InteractionPreviewSeamOverlapPixels);
            result.Add(new CadScreenRect(
                rightStart,
                middleTop,
                targetWidth - rightStart,
                middleHeight));
        }

        long exposedArea = 0;
        foreach (var rect in result)
            exposedArea += rect.Area;
        var targetArea = (double)targetWidth * targetHeight;
        if (targetArea > 0 &&
            exposedArea / targetArea > InteractionPreviewMaxExposedAreaRatio)
        {
            return null;
        }

        return result;
    }

    private void DrawSnapshotExposedRegions(
        ID2D1DeviceContext context,
        IReadOnlyList<CadScreenRect> exposedRects,
        CadViewport previewViewport)
    {
        if (_document is null)
            return;

        foreach (var exposed in exposedRects)
        {
            var clip = ToRawRectF(exposed);
            var previousTransform = context.Transform;
            context.Transform = System.Numerics.Matrix3x2.Identity;
            context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
            try
            {
                var background = ToColor4(_document.ViewSettings.BackgroundColor);
                FillScreenRect(context, clip, background);
                var dirtyWorldBounds = ScreenRectToWorldBounds(
                    exposed,
                    previewViewport);
                _renderer.Render(
                    _document,
                    previewViewport,
                    _transientScene,
                    _handleScene,
                    CreateRenderOptions(dirtyWorldBounds));
            }
            finally
            {
                context.PopAxisAlignedClip();
                context.Transform = previousTransform;
            }
        }
    }

    internal static double QuantizePanPreviewTranslation(double translation)
    {
        return Math.Round(translation, MidpointRounding.AwayFromZero);
    }

    internal byte[] CaptureBackBufferPixels() => _target.CaptureBackBufferPixels();

    internal byte[] CapturePresentedPixels() => _target.CapturePresentedPixels();

    public void EndViewportInteraction()
    {
        _viewportInteractionSnapshot = null;
        _target.ReleaseFrameSnapshot();
    }

    public void Render(
        CadRenderInvalidation? invalidation = null,
        bool baseSceneChanged = true)
    {
        RenderCore(
            invalidation,
            baseSceneChanged,
            retryAfterDeviceResourceRecreation: true);
    }

    public bool PrepareRenderCacheStep()
    {
        ThrowIfDisposed();
        return _document is not null &&
               _viewport is not null &&
               _target.IsTargetReady &&
               _renderer.PrepareRenderCaches(
                   _document,
                   _viewport,
                   _renderOptions,
                   buildStep: true,
                   handleScene: _handleScene,
                   transientScene: _transientScene);
    }

    private void RenderCore(
        CadRenderInvalidation? invalidation,
        bool baseSceneChanged,
        bool retryAfterDeviceResourceRecreation)
    {
        ThrowIfDisposed();
        EndViewportInteraction();

        if (!_target.IsTargetReady)
            return;

        var requestedInvalidation = invalidation;
        var baseSceneState = CreateBaseSceneState();
        var baseStateChanged =
            !_baseSceneValid ||
            !_target.HasBaseSceneSnapshot ||
            !ReferenceEquals(_baseSceneDocument, _document) ||
            !_baseSceneState.Equals(baseSceneState);
        var previousStaticBaseState = _baseSceneState with
        {
            SelectionVersion = 0,
            InlineMovePreviewVersion = 0
        };
        var currentStaticBaseState = baseSceneState with
        {
            SelectionVersion = 0,
            InlineMovePreviewVersion = 0
        };
        var staticBaseStateChanged =
            !previousStaticBaseState.Equals(currentStaticBaseState);
        baseSceneChanged |= _baseSceneDirty || baseStateChanged;
        if (staticBaseStateChanged)
            requestedInvalidation = CadRenderInvalidation.Full;

        var dirtyPlanningStarted = Stopwatch.GetTimestamp();
        var effectiveInvalidation = _dirtyRegionPlanner.Normalize(
            requestedInvalidation,
            _target.Width,
            _target.Height,
            EstimateDirtyRegionCost);
        if (effectiveInvalidation.IsEmpty)
            return;

        if (!baseSceneChanged &&
            !_target.RestoreBaseScene(
                effectiveInvalidation.IsFull
                    ? null
                    : effectiveInvalidation.DirtyScreenRects))
        {
            baseSceneChanged = true;
            effectiveInvalidation = CadRenderInvalidation.Full;
        }

        var background = _document is null
            ? FallbackBackgroundColor
            : ToColor4(_document.ViewSettings.BackgroundColor);
        var frameStartTimestamp = Stopwatch.GetTimestamp();
        var renderCacheBuildPending = false;
        var combineDirtyRegionPasses = _dirtyRegionPlanner.TryGetCombinedBounds(
            effectiveInvalidation,
            _target.Width,
            _target.Height,
            EstimateDirtyRegionCost,
            out var combinedDirtyRegionBounds);
        using var combinedDirtyRegionMask = combineDirtyRegionPasses
            ? CreateDirtyRegionMask(effectiveInvalidation.DirtyScreenRects)
            : null;
        var dirtyPlanningMilliseconds = Stopwatch
            .GetElapsedTime(dirtyPlanningStarted)
            .TotalMilliseconds;

        try
        {
            _renderer.BeginFrame(
                effectiveInvalidation.IsFull,
                effectiveInvalidation.IsFull || combinedDirtyRegionMask is not null
                    ? 1
                    : effectiveInvalidation.DirtyScreenRects.Count,
                dirtyPlanningMilliseconds);
            try
            {
                if (_document is not null && _viewport is not null)
                {
                    var oleStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        _renderer.PrepareOleTiles(
                            _document,
                            _viewport,
                            _transientScene,
                            _renderOptions);
                    }
                    finally
                    {
                        _renderer.RecordOlePreparation(
                            Stopwatch.GetElapsedTime(oleStarted).TotalMilliseconds);
                    }

                    var cacheStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        renderCacheBuildPending = _renderer.PrepareRenderCaches(
                            _document,
                            _viewport,
                            _renderOptions,
                            buildStep: false,
                            handleScene: _handleScene,
                            transientScene: _transientScene);
                    }
                    finally
                    {
                        _renderer.RecordCachePreparation(
                            Stopwatch.GetElapsedTime(cacheStarted).TotalMilliseconds);
                    }
                }

                if (_deferInitialPresentationUntilResourcesReady &&
                    renderCacheBuildPending &&
                    RenderCacheBuildRequested is not null)
                {
                    // Geometry is prepared off the UI thread, but resources tied to the
                    // device context must be attached here. Let the UI idle scheduler finish
                    // those batches before the first Present so a new document never appears
                    // as a progressively populated scene.
                    RenderCacheBuildRequested.Invoke(this, EventArgs.Empty);
                    return;
                }

                _deferInitialPresentationUntilResourcesReady = false;

                var surfaceStarted = Stopwatch.GetTimestamp();
                try
                {
                    if (baseSceneChanged)
                    {
                        IDisposable? parallelFrameLease = null;
                        try
                        {
                            _target.DrawFrame(context =>
                            {
                                if (!effectiveInvalidation.IsFull)
                                {
                                    if (combinedDirtyRegionMask is not null)
                                    {
                                        DrawCombinedDirtyRegions(
                                            context,
                                            combinedDirtyRegionMask,
                                            combinedDirtyRegionBounds,
                                            background,
                                            drawBase: true,
                                            effectiveInvalidation.DirtyScreenRects);
                                    }
                                    else
                                    {
                                        foreach (var dirty in effectiveInvalidation.DirtyScreenRects)
                                        {
                                            DrawDirtyRegion(
                                                context,
                                                dirty,
                                                background,
                                                drawBase: true);
                                        }
                                    }

                                    return;
                                }

                                context.Clear(background);

                                if (_document is not null && _viewport is not null &&
                                    !TryRenderParallelBase(
                                        context,
                                        _document,
                                        _viewport,
                                        out parallelFrameLease))
                                {
                                    _renderer.RenderBase(
                                        _document,
                                        _viewport,
                                        _transientScene,
                                        _handleScene,
                                        _renderOptions);
                                }
                            },
                            effectiveInvalidation.IsFull
                                ? null
                                : effectiveInvalidation.DirtyScreenRects,
                            present: false);
                        }
                        finally
                        {
                            // Multi-device keyed-mutex ownership is released only after the main
                            // context has submitted every DrawImage that reads worker textures.
                            parallelFrameLease?.Dispose();
                        }

                        _baseSceneValid = _target.CaptureBaseScene(
                            effectiveInvalidation.IsFull
                                ? null
                                : effectiveInvalidation.DirtyScreenRects);
                        if (_baseSceneValid)
                        {
                            _baseSceneDocument = _document;
                            _baseSceneState = baseSceneState;
                            _baseSceneDirty = false;
                        }
                    }

                    _target.DrawFrame(context =>
                    {
                        if (!effectiveInvalidation.IsFull)
                        {
                            if (combinedDirtyRegionMask is not null)
                            {
                                DrawCombinedDirtyRegions(
                                    context,
                                    combinedDirtyRegionMask,
                                    combinedDirtyRegionBounds,
                                    background,
                                    drawBase: false,
                                    effectiveInvalidation.DirtyScreenRects);
                            }
                            else
                            {
                                foreach (var dirty in effectiveInvalidation.DirtyScreenRects)
                                {
                                    DrawDirtyRegion(
                                        context,
                                        dirty,
                                        background,
                                        drawBase: false);
                                }
                            }

                            return;
                        }

                        if (_document is not null && _viewport is not null)
                        {
                            _renderer.RenderOverlays(
                                _document,
                                _viewport,
                                _transientScene,
                                _handleScene,
                                _renderOptions);
                        }
                    }, effectiveInvalidation.IsFull ? null : effectiveInvalidation.DirtyScreenRects);
                }
                finally
                {
                    _renderer.RecordSurfaceDraw(
                        Stopwatch.GetElapsedTime(surfaceStarted).TotalMilliseconds);
                }
            }
            finally
            {
                _renderer.CompleteFrame();
            }

            _hasRenderedFrame = true;
            RenderStatistics = _renderer.RenderStatistics with
            {
                RenderDurationMilliseconds = Stopwatch
                    .GetElapsedTime(frameStartTimestamp)
                    .TotalMilliseconds
            };
            _frameRateTracker.Record(frameStartTimestamp, effectiveInvalidation.IsFull);
            if (renderCacheBuildPending)
                RenderCacheBuildRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Direct2DDeviceResourcesRecreatedException) when (retryAfterDeviceResourceRecreation)
        {
            ResetRendererDeviceResources();
            RenderCore(
                CadRenderInvalidation.Full,
                baseSceneChanged: true,
                retryAfterDeviceResourceRecreation: false);
        }
    }

    private void DrawDirtyRegion(
        ID2D1DeviceContext context,
        CadScreenRect dirty,
        Color4 background,
        bool drawBase)
    {
        var clip = ToRawRectF(dirty);
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.Identity;
        context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
        try
        {
            if (drawBase)
                FillScreenRect(context, clip, background);
            if (_document is null || _viewport is null)
                return;

            var options = CreateRenderOptions(
                ScreenRectToWorldBounds(dirty, _viewport));
            if (drawBase)
            {
                _renderer.RenderBase(
                    _document,
                    _viewport,
                    _transientScene,
                    _handleScene,
                    options);
            }
            else
            {
                _renderer.RenderOverlays(
                    _document,
                    _viewport,
                    _transientScene,
                    _handleScene,
                    options);
            }
        }
        finally
        {
            context.PopAxisAlignedClip();
            context.Transform = previousTransform;
        }
    }

    private void DrawCombinedDirtyRegions(
        ID2D1DeviceContext context,
        ID2D1Geometry dirtyRegionMask,
        CadScreenRect combinedBounds,
        Color4 background,
        bool drawBase,
        IReadOnlyList<CadScreenRect> dirtyRects)
    {
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.Identity;
        if (drawBase)
        {
            foreach (var dirtyRect in dirtyRects)
                FillScreenRect(context, ToRawRectF(dirtyRect), background);
        }

        var layerParameters = new LayerParameters1
        {
            ContentBounds = ToRawRectF(combinedBounds),
            GeometricMask = dirtyRegionMask,
            MaskAntialiasMode = AntialiasMode.Aliased,
            MaskTransform = Matrix3x2.Identity,
            Opacity = 1.0f,
            OpacityBrush = null,
            LayerOptions = LayerOptions1.None
        };
        var layerPushed = false;
        try
        {
            context.PushLayer(ref layerParameters, null!);
            layerPushed = true;
            if (_document is null || _viewport is null)
                return;

            var options = CreateRenderOptions(
                ScreenRectToWorldBounds(combinedBounds, _viewport));
            if (drawBase)
            {
                _renderer.RenderBase(
                    _document,
                    _viewport,
                    _transientScene,
                    _handleScene,
                    options);
            }
            else
            {
                _renderer.RenderOverlays(
                    _document,
                    _viewport,
                    _transientScene,
                    _handleScene,
                    options);
            }
        }
        finally
        {
            if (layerPushed)
                context.PopLayer();
            context.Transform = previousTransform;
        }
    }

    private ID2D1PathGeometry CreateDirtyRegionMask(IReadOnlyList<CadScreenRect> dirtyRects)
    {
        var factory = _target.Factory ??
                      throw new InvalidOperationException("Direct2D factory is not created.");
        var geometry = factory.CreatePathGeometry();
        try
        {
            using var sink = geometry.Open();
            foreach (var rect in dirtyRects)
            {
                var left = rect.X;
                var top = rect.Y;
                var right = rect.X + rect.Width;
                var bottom = rect.Y + rect.Height;
                sink.BeginFigure(new Vector2(left, top), FigureBegin.Filled);
                sink.AddLine(new Vector2(right, top));
                sink.AddLine(new Vector2(right, bottom));
                sink.AddLine(new Vector2(left, bottom));
                sink.EndFigure(FigureEnd.Closed);
            }

            sink.Close();
            return geometry;
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    private double EstimateDirtyRegionCost(CadScreenRect rect)
    {
        if (rect.IsEmpty)
            return 0;

        if (_viewport is not null)
        {
            var padding = 64.0 / Math.Max(_viewport.Zoom, double.Epsilon);
            var worldBounds = ScreenRectToWorldBounds(rect, _viewport).Inflate(padding);
            if (_renderOptions.EntityBoundsQueryInto is { } bufferedQuery)
            {
                _dirtyCostCandidateIds.Clear();
                bufferedQuery(
                    _renderOptions.ActiveOwnerBlockId,
                    worldBounds,
                    _dirtyCostCandidateIds);
                return _dirtyCostCandidateIds.Count + DirtyRegionPassPenalty;
            }

            if (_renderOptions.EntityBoundsQuery is { } query)
            {
                return query(_renderOptions.ActiveOwnerBlockId, worldBounds).Count +
                       DirtyRegionPassPenalty;
            }
        }

        var targetArea = Math.Max(1.0, (double)_target.Width * _target.Height);
        var entityCount = Math.Max(1, _document?.Entities.Count ?? 1);
        return rect.Area / targetArea * entityCount + DirtyRegionPassPenalty;
    }

    private CadRenderOptions CreateRenderOptions(CadRectD? dirtyWorldBounds)
    {
        return new CadRenderOptions
        {
            ActiveOwnerBlockId = _renderOptions.ActiveOwnerBlockId,
            ActiveLayoutId = _renderOptions.ActiveLayoutId,
            ActiveLayoutViewportId = _renderOptions.ActiveLayoutViewportId,
            DrawGrid = _renderOptions.DrawGrid,
            DrawOrigin = _renderOptions.DrawOrigin,
            DrawGripHandles = _renderOptions.DrawGripHandles,
            DrawLayoutGuides = _renderOptions.DrawLayoutGuides,
            IsAntialiasingEnabled = _renderOptions.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = _renderOptions.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = _renderOptions.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = _renderOptions.AllowApproximateTileScaleFallback,
            IsBackgroundChunkRecordingEnabled =
                _renderOptions.IsBackgroundChunkRecordingEnabled,
            IsParallelRenderingEnabled =
                _renderOptions.IsParallelRenderingEnabled,
            ParallelRenderingMode = _renderOptions.ParallelRenderingMode,
            ParallelRenderingWorkerCount =
                _renderOptions.ParallelRenderingWorkerCount,
            ParallelRenderingEntityThreshold =
                _renderOptions.ParallelRenderingEntityThreshold,
            EnableGeometryRealizations = _renderOptions.EnableGeometryRealizations,
            TransformScaleMultiplier = _renderOptions.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = _renderOptions.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = _renderOptions.MinimumScreenStrokeWidth,
            HiddenEntityIds = _renderOptions.HiddenEntityIds,
            DirtyWorldBounds = dirtyWorldBounds,
            EntityBoundsQuery = _renderOptions.EntityBoundsQuery,
            EntityBoundsQueryInto = _renderOptions.EntityBoundsQueryInto
        };
    }

    private bool TryRenderParallelBase(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        out IDisposable? frameLease)
    {
        frameLease = null;
        if (!_renderOptions.IsParallelRenderingEnabled ||
            _target.UsingWarp ||
            _handleScene is { SelectionReferenceCount: > 0 } ||
            _target.D3DDevice is not { } d3dDevice ||
            _target.Factory is not { } d2dFactory ||
            _target.Device is not { } d2dDevice ||
            _target.DwriteFactory is not { } dwriteFactory)
        {
            return false;
        }

        var visibleEntities = _renderer.GetVisibleEntitiesForParallelRendering(
            document,
            viewport,
            _renderOptions);
        Direct2DParallelFrameMetrics metrics;
        bool rendered;
        if (_renderOptions.ParallelRenderingMode ==
            CadParallelRenderingMode.SharedDeviceContexts)
        {
            _multiDeviceRenderer.Reset();
            rendered = _sharedDeviceRenderer.TryDraw(
                d3dDevice,
                d2dFactory,
                d2dDevice,
                context,
                dwriteFactory,
                document,
                viewport,
                _renderOptions,
                visibleEntities,
                _target.Width,
                _target.Height,
                () => _renderer.RenderBackground(document, viewport, _renderOptions),
                out metrics);
        }
        else
        {
            _sharedDeviceRenderer.Reset();
            rendered = _multiDeviceRenderer.TryDraw(
                d3dDevice,
                context,
                dwriteFactory,
                document,
                viewport,
                _renderOptions,
                visibleEntities,
                _target.Width,
                _target.Height,
                () => _renderer.RenderBackground(document, viewport, _renderOptions),
                out var multiDeviceFrameLease,
                out metrics);
            frameLease = multiDeviceFrameLease;
        }
        if (rendered)
        {
            _renderer.RecordParallelFrame(
                metrics.Mode,
                metrics.WorkerCount,
                metrics.EntityCount,
                metrics.ElapsedMilliseconds,
                metrics.WorkerStatistics);
        }
        return rendered;
    }

    private static CadRectD ScreenRectToWorldBounds(CadScreenRect rect, CadViewport viewport)
    {
        return CadRectD.Empty
            .ExpandToInclude(viewport.ScreenToWorld(new CadPointD(rect.X, rect.Y)))
            .ExpandToInclude(viewport.ScreenToWorld(new CadPointD(rect.X + rect.Width, rect.Y + rect.Height)));
    }

    private static RawRectF ToRawRectF(CadScreenRect rect)
    {
        return new RawRectF(
            rect.X,
            rect.Y,
            rect.X + rect.Width,
            rect.Y + rect.Height);
    }

    private void FillScreenRect(ID2D1DeviceContext context, RawRectF rect, Color4 color)
    {
        var previousTransform = context.Transform;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        context.Transform = System.Numerics.Matrix3x2.Identity;
        context.PrimitiveBlend = PrimitiveBlend.Copy;

        try
        {
            context.FillRectangle(rect, GetClearBrush(context, color));
        }
        finally
        {
            context.PrimitiveBlend = previousPrimitiveBlend;
            context.Transform = previousTransform;
        }
    }

    private ID2D1SolidColorBrush GetClearBrush(ID2D1DeviceContext context, Color4 color)
    {
        if (_clearBrush is not null &&
            ReferenceEquals(_clearBrushContext, context) &&
            _clearBrushColor.Equals(color))
        {
            return _clearBrush;
        }

        ReleaseClearBrush();
        _clearBrushContext = context;
        _clearBrushColor = color;
        _clearBrush = context.CreateSolidColorBrush(color);
        return _clearBrush;
    }

    private void ReleaseClearBrush()
    {
        _clearBrush?.Dispose();
        _clearBrush = null;
        _clearBrushContext = null;
    }

    private static Color4 ToColor4(CadColor color)
    {
        return new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f);
    }

    private void TrackPendingTextMeasurements(
        CadDocument document,
        CadDocumentChangeSet changes)
    {
        foreach (var change in changes.EntityChanges)
            TrackPendingTextMeasurement(document, change.EntityId);
    }

    private void TrackPendingTextMeasurement(
        CadDocument document,
        EntityId entityId)
    {
        if (document.TryGetEntity(entityId, out var entity) &&
            entity is CadText { IsErased: false, RequiresBoundsMeasurement: true })
        {
            _pendingTextMeasurementIds.Add(entityId);
        }
        else
        {
            _pendingTextMeasurementIds.Remove(entityId);
        }
    }

    private void RefreshPendingTextMeasurements(CadDocument document)
    {
        _pendingTextMeasurementIds.Clear();
        foreach (var text in document.Entities.Values.OfType<CadText>())
        {
            if (!text.IsErased && text.RequiresBoundsMeasurement)
                _pendingTextMeasurementIds.Add(text.Id);
        }
    }

    private void ResetRendererDeviceResources()
    {
        EndViewportInteraction();
        _hasRenderedFrame = false;
        InvalidateBaseScene(releaseSnapshot: true);
        ResetParallelRenderers();
        if (!_target.IsTargetReady)
            return;

        ReleaseClearBrush();
        _renderer.ResetDeviceResources(
            _target.Factory,
            _target.DwriteFactory,
            _target.Device,
            _target.Context,
            _document);
    }

    private void BeforeDeviceResourcesReleased()
    {
        ResetParallelRenderers();
        _renderer.SuspendBackgroundChunkRecording();
    }

    private void ResetParallelRenderers()
    {
        _multiDeviceRenderer.Reset();
        _sharedDeviceRenderer.Reset();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ReleaseClearBrush();
        EndViewportInteraction();
        _multiDeviceRenderer.Dispose();
        _sharedDeviceRenderer.Dispose();
        _renderer.Dispose();
        _target.Dispose();
        _imageSource = null;
        _document = null;
        _viewport = null;
        _transientScene = null;
        _handleScene = null;
        _pendingTextMeasurementIds.Clear();
        _frameRateTracker.Reset();
        _disposed = true;
    }

    private readonly record struct ViewportInteractionSnapshot(
        double Zoom,
        CadPointD Offset);

    private BaseSceneState CreateBaseSceneState()
    {
        var hiddenHash = new HashCode();
        var hiddenCount = 0;
        foreach (var entityId in _renderOptions.HiddenEntityIds)
        {
            hiddenHash.Add(entityId);
            hiddenCount++;
        }

        return new BaseSceneState(
            _viewport?.Zoom ?? 0,
            _viewport?.Offset ?? CadPointD.Origin,
            _viewport?.ViewWidth ?? 0,
            _viewport?.ViewHeight ?? 0,
            _target.Width,
            _target.Height,
            _renderOptions.ActiveOwnerBlockId,
            _renderOptions.ActiveLayoutId,
            _renderOptions.ActiveLayoutViewportId,
            _renderOptions.DrawGrid,
            _renderOptions.DrawOrigin,
            _renderOptions.DrawLayoutGuides,
            _renderOptions.IsAntialiasingEnabled,
            _renderOptions.IsTextAntialiasingEnabled,
            _renderOptions.EnableGeometryRealizations,
            _renderOptions.IsLevelOfDetailEnabled,
            _renderOptions.AllowApproximateTileScaleFallback,
            _renderOptions.TransformScaleMultiplier,
            _renderOptions.KeepStrokeWidthScreenConstant,
            _renderOptions.MinimumScreenStrokeWidth,
            hiddenCount,
            hiddenHash.ToHashCode(),
            _handleScene?.SelectionVersion ?? 0,
            _renderOptions.HiddenEntityIds.Count > 0
                ? _transientScene?.Version ?? 0
                : 0,
            _document is null
                ? FallbackBackgroundColor.GetHashCode()
                : _document.ViewSettings.BackgroundColor.GetHashCode());
    }

    private void InvalidateBaseScene(bool releaseSnapshot)
    {
        _baseSceneValid = false;
        _baseSceneDirty = true;
        _baseSceneDocument = null;
        _baseSceneState = default;
        if (releaseSnapshot)
            _target.ReleaseBaseScene();
    }

    private readonly record struct BaseSceneState(
        double Zoom,
        CadPointD Offset,
        double ViewWidth,
        double ViewHeight,
        int TargetWidth,
        int TargetHeight,
        BlockId ActiveOwnerBlockId,
        LayoutId? ActiveLayoutId,
        LayoutViewportId? ActiveLayoutViewportId,
        bool DrawGrid,
        bool DrawOrigin,
        bool DrawLayoutGuides,
        bool IsAntialiasingEnabled,
        bool IsTextAntialiasingEnabled,
        bool EnableGeometryRealizations,
        bool IsLevelOfDetailEnabled,
        bool AllowApproximateTileScaleFallback,
        double TransformScaleMultiplier,
        bool KeepStrokeWidthScreenConstant,
        double MinimumScreenStrokeWidth,
        int HiddenEntityCount,
        int HiddenEntityHash,
        long SelectionVersion,
        long InlineMovePreviewVersion,
        int BackgroundColorHash);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DImageRenderHost));
    }
}
