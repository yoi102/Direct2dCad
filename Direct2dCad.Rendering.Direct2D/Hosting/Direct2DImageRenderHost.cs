using System.Diagnostics;
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
    private const double PartialRenderMaxAreaRatio = 0.65;
    private const int DirtyRegionCostOptimizationThreshold = 4;
    private const double DirtyRegionMergeCostTolerance = 1.15;
    private const double DirtyRegionPassPenalty = 96.0;
    private const double InteractionPreviewMaxExposedAreaRatio = 0.55;
    private const double FrameRateWindowSeconds = 0.5;
    private const int FrameRateSampleResetMilliseconds = 750;
    private readonly ImageSourceDirect2DResource _target = new();
    private readonly Direct2DSceneRender _renderer = new();
    private readonly HashSet<EntityId> _pendingTextMeasurementIds = [];
    private readonly Queue<RenderFrameSample> _frameRateSamples = [];
    private ID3D11ImageSource? _imageSource;
    private CadDocument? _document;
    private CadViewport? _viewport;
    private CadTransientScene? _transientScene;
    private CadHandleScene? _handleScene;
    private CadRenderOptions _renderOptions = new();
    private ID2D1DeviceContext? _clearBrushContext;
    private ID2D1SolidColorBrush? _clearBrush;
    private Color4 _clearBrushColor;
    private long _lastRenderedFrameTimestamp;
    private double _renderDurationSecondsTotal;
    private ViewportInteractionSnapshot? _viewportInteractionSnapshot;
    private bool _hasRenderedFrame;
    private bool _disposed;

    public ICadGeometryResourceManager GeometryResourceManager => this;

    public int TargetWidth => _target.Width;

    public int TargetHeight => _target.Height;

    public double FramesPerSecond { get; private set; }

    public double LastFrameRenderTimeMilliseconds { get; private set; }

    public double AverageFrameRenderTimeMilliseconds { get; private set; }

    public double LastFullFrameRenderTimeMilliseconds { get; private set; }

    public CadRenderStatistics RenderStatistics { get; private set; } = CadRenderStatistics.Empty;

    public event EventHandler? RenderCacheBuildRequested;

    public bool IsViewportInteractionActive => _viewportInteractionSnapshot is not null;

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
        EndViewportInteraction();
        ResetRendererDeviceResources();
    }

    public void SetScene(CadDocument document, CadViewport viewport)
    {
        ThrowIfDisposed();

        _document = document ?? throw new ArgumentNullException(nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _hasRenderedFrame = false;
        EndViewportInteraction();
        RefreshPendingTextMeasurements(document);
        ResetRendererDeviceResources();
    }

    public void RebuildAll(CadDocument document)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);

        RefreshPendingTextMeasurements(document);
        _renderer.RebuildAll(document);
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);

        TrackPendingTextMeasurements(document, changes);
        _renderer.ApplyChanges(document, changes);
    }

    public void RebuildEntity(CadDocument document, EntityId entityId)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);

        TrackPendingTextMeasurement(document, entityId);
        _renderer.RebuildEntity(document, entityId);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();

        _pendingTextMeasurementIds.Remove(entityId);
        _renderer.RemoveEntity(entityId);
    }

    public void SetTransientScene(CadTransientScene? transientScene)
    {
        ThrowIfDisposed();

        _transientScene = transientScene;
        Render(CadRenderInvalidation.Full);
    }

    public void SetHandleScene(CadHandleScene? handleScene)
    {
        ThrowIfDisposed();

        _handleScene = handleScene;
        Render(CadRenderInvalidation.Full);
    }

    public void SetRenderOptions(CadRenderOptions? renderOptions)
    {
        ThrowIfDisposed();

        _renderOptions = renderOptions ?? new CadRenderOptions();
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

        _imageSource?.SetSize(width, height);

        if (_imageSource is not null)
            _target.SetSize(width, height);

        Render(CadRenderInvalidation.Full);
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

        var background = _document is null
            ? FallbackBackgroundColor
            : ToColor4(_document.ViewSettings.BackgroundColor);
        var transform =
            System.Numerics.Matrix3x2.CreateScale((float)scale) *
            System.Numerics.Matrix3x2.CreateTranslation(
                (float)translationX,
                (float)translationY);
        var isPanPreview = Math.Abs(scale - 1.0) <= 1e-6;
        var exposedRects = ResolveSnapshotExposedRects(
            scale,
            translationX,
            translationY);
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
                        _viewport,
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
                        exposedRects.Count > 0
                            ? context => DrawSnapshotExposedRegions(context, exposedRects)
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
                    _viewport.Zoom,
                    _viewport.Offset);
            }

            RecordRenderedFrame(frameStartTimestamp, isFullFrame: false);
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

    private IReadOnlyList<CadScreenRect>? ResolveSnapshotExposedRects(
        double scale,
        double translationX,
        double translationY)
    {
        if (_target.Width <= 0 || _target.Height <= 0)
            return [];

        var snapshotLeft = translationX;
        var snapshotTop = translationY;
        var snapshotRight = translationX + _target.Width * scale;
        var snapshotBottom = translationY + _target.Height * scale;
        if (!double.IsFinite(snapshotLeft) ||
            !double.IsFinite(snapshotTop) ||
            !double.IsFinite(snapshotRight) ||
            !double.IsFinite(snapshotBottom))
        {
            return null;
        }

        var coveredLeft = (int)Math.Ceiling(
            Math.Clamp(snapshotLeft, 0.0, _target.Width));
        var coveredTop = (int)Math.Ceiling(
            Math.Clamp(snapshotTop, 0.0, _target.Height));
        var coveredRight = (int)Math.Floor(
            Math.Clamp(snapshotRight, 0.0, _target.Width));
        var coveredBottom = (int)Math.Floor(
            Math.Clamp(snapshotBottom, 0.0, _target.Height));
        if (coveredRight <= coveredLeft || coveredBottom <= coveredTop)
            return null;

        var result = new List<CadScreenRect>(4);
        if (coveredTop > 0)
            result.Add(new CadScreenRect(0, 0, _target.Width, coveredTop));
        if (coveredBottom < _target.Height)
        {
            result.Add(new CadScreenRect(
                0,
                coveredBottom,
                _target.Width,
                _target.Height - coveredBottom));
        }

        var middleHeight = coveredBottom - coveredTop;
        if (coveredLeft > 0 && middleHeight > 0)
        {
            result.Add(new CadScreenRect(
                0,
                coveredTop,
                coveredLeft,
                middleHeight));
        }
        if (coveredRight < _target.Width && middleHeight > 0)
        {
            result.Add(new CadScreenRect(
                coveredRight,
                coveredTop,
                _target.Width - coveredRight,
                middleHeight));
        }

        var exposedArea = result.Sum(static rect => rect.Area);
        var targetArea = (double)_target.Width * _target.Height;
        if (targetArea > 0 &&
            exposedArea / targetArea > InteractionPreviewMaxExposedAreaRatio)
        {
            return null;
        }

        return result;
    }

    private void DrawSnapshotExposedRegions(
        ID2D1DeviceContext context,
        IReadOnlyList<CadScreenRect> exposedRects)
    {
        if (_document is null || _viewport is null)
            return;

        foreach (var exposed in exposedRects)
        {
            var clip = ToRawRectF(exposed);
            var previousTransform = context.Transform;
            context.Transform = System.Numerics.Matrix3x2.Identity;
            context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
            try
            {
                var dirtyWorldBounds = ScreenRectToWorldBounds(exposed, _viewport);
                _renderer.Render(
                    _document,
                    _viewport,
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

    public void EndViewportInteraction()
    {
        _viewportInteractionSnapshot = null;
        _target.ReleaseFrameSnapshot();
    }

    public void Render(CadRenderInvalidation? invalidation = null)
    {
        RenderCore(invalidation, retryAfterDeviceResourceRecreation: true);
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
                   buildStep: true);
    }

    private void RenderCore(
        CadRenderInvalidation? invalidation,
        bool retryAfterDeviceResourceRecreation)
    {
        ThrowIfDisposed();
        EndViewportInteraction();

        if (!_target.IsTargetReady)
            return;

        var effectiveInvalidation = NormalizeInvalidation(invalidation);
        if (effectiveInvalidation.IsEmpty)
            return;

        var background = _document is null
            ? FallbackBackgroundColor
            : ToColor4(_document.ViewSettings.BackgroundColor);
        var frameStartTimestamp = Stopwatch.GetTimestamp();
        var renderCacheBuildPending = false;

        try
        {
            _renderer.BeginFrame(
                effectiveInvalidation.IsFull,
                effectiveInvalidation.IsFull
                    ? 1
                    : effectiveInvalidation.DirtyScreenRects.Count);
            try
            {
                if (_document is not null && _viewport is not null)
                {
                    _renderer.PrepareOleTiles(
                        _document,
                        _viewport,
                        _transientScene,
                        _renderOptions);
                    renderCacheBuildPending = _renderer.PrepareRenderCaches(
                        _document,
                        _viewport,
                        _renderOptions,
                        buildStep: false);
                }

                _target.DrawFrame(context =>
                {
                    if (!effectiveInvalidation.IsFull)
                    {
                        foreach (var dirty in effectiveInvalidation.DirtyScreenRects)
                        {
                            var clip = ToRawRectF(dirty);
                            var previousTransform = context.Transform;
                            context.Transform = System.Numerics.Matrix3x2.Identity;
                            context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);

                            try
                            {
                                FillScreenRect(context, clip, background);

                                if (_document is not null && _viewport is not null)
                                {
                                    var dirtyWorldBounds = ScreenRectToWorldBounds(dirty, _viewport);
                                    _renderer.Render(
                                        _document,
                                        _viewport,
                                        _transientScene,
                                        _handleScene,
                                        CreateRenderOptions(dirtyWorldBounds));
                                }
                            }
                            finally
                            {
                                context.PopAxisAlignedClip();
                                context.Transform = previousTransform;
                            }
                        }

                        return;
                    }

                    context.Clear(background);

                    if (_document is not null && _viewport is not null)
                        _renderer.Render(_document, _viewport, _transientScene, _handleScene, _renderOptions);
                }, effectiveInvalidation.IsFull ? null : effectiveInvalidation.DirtyScreenRects);
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
            RecordRenderedFrame(frameStartTimestamp, effectiveInvalidation.IsFull);
            if (renderCacheBuildPending)
                RenderCacheBuildRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Direct2DDeviceResourcesRecreatedException) when (retryAfterDeviceResourceRecreation)
        {
            ResetRendererDeviceResources();
            RenderCore(CadRenderInvalidation.Full, retryAfterDeviceResourceRecreation: false);
        }
    }

    private CadRenderInvalidation NormalizeInvalidation(CadRenderInvalidation? invalidation)
    {
        if (invalidation is null || invalidation.IsFull)
            return CadRenderInvalidation.Full;

        if (_target.Width <= 0 || _target.Height <= 0)
            return CadRenderInvalidation.Full;

        var rects = new List<CadScreenRect>(invalidation.DirtyScreenRects.Count);
        foreach (var dirtyRect in invalidation.DirtyScreenRects)
        {
            var rect = ClampToTarget(dirtyRect);
            if (!rect.IsEmpty)
                rects.Add(rect);
        }

        if (rects.Count == 0)
            return CadRenderInvalidation.FromScreenRect(default);

        var normalizedInvalidation = CadRenderInvalidation.FromScreenRects(rects);
        if (normalizedInvalidation.DirtyScreenRects.Count >=
            DirtyRegionCostOptimizationThreshold)
        {
            var aggregateInvalidation = CadRenderInvalidation.FromScreenRect(
                normalizedInvalidation.DirtyScreenRect);
            var separateCost = 0.0;
            foreach (var rect in normalizedInvalidation.DirtyScreenRects)
                separateCost += EstimateDirtyRegionCost(rect);

            var aggregateCost = EstimateDirtyRegionCost(
                aggregateInvalidation.DirtyScreenRect);
            if (aggregateCost <= separateCost * DirtyRegionMergeCostTolerance)
                normalizedInvalidation = aggregateInvalidation;
        }

        var area = 0.0;
        foreach (var rect in normalizedInvalidation.DirtyScreenRects)
            area += rect.Area;

        var targetArea = (double)_target.Width * _target.Height;
        return targetArea > 0 && area / targetArea >= PartialRenderMaxAreaRatio
            ? CadRenderInvalidation.Full
            : normalizedInvalidation;
    }

    private double EstimateDirtyRegionCost(CadScreenRect rect)
    {
        if (rect.IsEmpty)
            return 0;

        if (_viewport is not null &&
            _renderOptions.EntityBoundsQuery is { } query)
        {
            var padding = 64.0 / Math.Max(_viewport.Zoom, double.Epsilon);
            var worldBounds = ScreenRectToWorldBounds(rect, _viewport).Inflate(padding);
            return query(_renderOptions.ActiveOwnerBlockId, worldBounds).Count +
                   DirtyRegionPassPenalty;
        }

        var targetArea = Math.Max(1.0, (double)_target.Width * _target.Height);
        var entityCount = Math.Max(1, _document?.Entities.Count ?? 1);
        return rect.Area / targetArea * entityCount + DirtyRegionPassPenalty;
    }

    private CadScreenRect ClampToTarget(CadScreenRect rect)
    {
        if (rect.IsEmpty || _target.Width <= 0 || _target.Height <= 0)
            return default;

        var x = Math.Clamp(rect.X, 0, _target.Width);
        var y = Math.Clamp(rect.Y, 0, _target.Height);
        var right = Math.Clamp(rect.X + rect.Width, 0, _target.Width);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, _target.Height);
        return new CadScreenRect(x, y, right - x, bottom - y);
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
            IsAntialiasingEnabled = _renderOptions.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = _renderOptions.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = _renderOptions.IsLevelOfDetailEnabled,
            AllowApproximateScaleFallback = _renderOptions.AllowApproximateScaleFallback,
            TransformScaleMultiplier = _renderOptions.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = _renderOptions.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = _renderOptions.MinimumScreenStrokeWidth,
            HiddenEntityIds = _renderOptions.HiddenEntityIds,
            DirtyWorldBounds = dirtyWorldBounds,
            EntityBoundsQuery = _renderOptions.EntityBoundsQuery,
            EntityBoundsQueryInto = _renderOptions.EntityBoundsQueryInto
        };
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

    private void RecordRenderedFrame(
        long frameStartTimestamp,
        bool isFullFrame)
    {
        var frameEndTimestamp = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(frameStartTimestamp, frameEndTimestamp).TotalSeconds;
        if (!double.IsFinite(elapsed) || elapsed <= 0)
            return;

        LastFrameRenderTimeMilliseconds = elapsed * 1000.0;
        if (isFullFrame)
            LastFullFrameRenderTimeMilliseconds = LastFrameRenderTimeMilliseconds;

        if (_lastRenderedFrameTimestamp != 0 &&
            Stopwatch.GetElapsedTime(_lastRenderedFrameTimestamp, frameEndTimestamp).TotalMilliseconds >
            FrameRateSampleResetMilliseconds)
        {
            _frameRateSamples.Clear();
            _renderDurationSecondsTotal = 0;
        }

        _lastRenderedFrameTimestamp = frameEndTimestamp;
        _frameRateSamples.Enqueue(new RenderFrameSample(frameEndTimestamp, elapsed));
        _renderDurationSecondsTotal += elapsed;
        while (_frameRateSamples.Count > 1 &&
               Stopwatch.GetElapsedTime(
                   _frameRateSamples.Peek().CompletionTimestamp,
                   frameEndTimestamp).TotalSeconds >
               FrameRateWindowSeconds)
        {
            _renderDurationSecondsTotal -= _frameRateSamples.Dequeue().RenderDurationSeconds;
        }

        _renderDurationSecondsTotal = Math.Max(0, _renderDurationSecondsTotal);
        var averageRenderDuration = _frameRateSamples.Count > 0
            ? _renderDurationSecondsTotal / _frameRateSamples.Count
            : 0;
        AverageFrameRenderTimeMilliseconds = averageRenderDuration * 1000.0;
        FramesPerSecond = averageRenderDuration > 0
            ? 1.0 / averageRenderDuration
            : 0;
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
        if (!_target.IsTargetReady)
            return;

        ReleaseClearBrush();
        _renderer.ResetDeviceResources(
            _target.Factory,
            _target.DwriteFactory,
            _target.Context,
            _document);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ReleaseClearBrush();
        EndViewportInteraction();
        _renderer.Dispose();
        _target.Dispose();
        _imageSource = null;
        _document = null;
        _viewport = null;
        _transientScene = null;
        _handleScene = null;
        _pendingTextMeasurementIds.Clear();
        _frameRateSamples.Clear();
        _lastRenderedFrameTimestamp = 0;
        _renderDurationSecondsTotal = 0;
        FramesPerSecond = 0;
        LastFrameRenderTimeMilliseconds = 0;
        AverageFrameRenderTimeMilliseconds = 0;
        LastFullFrameRenderTimeMilliseconds = 0;
        _disposed = true;
    }

    private readonly record struct RenderFrameSample(
        long CompletionTimestamp,
        double RenderDurationSeconds);

    private readonly record struct ViewportInteractionSnapshot(
        double Zoom,
        CadPointD Offset);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DImageRenderHost));
    }
}
