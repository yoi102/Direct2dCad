using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DResourceCache : IDisposable
{
    internal const long GeometryRealizationCacheBudgetBytes = 128L * 1024 * 1024;
    private readonly Dictionary<EntityId, EntityResourceBucket> _entityResources = [];
    private readonly Direct2DStyleResourceCache _styleResources;
    private readonly Direct2DTextFormatResourceCache _textFormatResources;
    private readonly Direct2DImageBitmapResourceCache _imageBitmapResources;
    private readonly Direct2DGeometryRealizationCache _geometryRealizations = new();
    private readonly Direct2DGeometryFactory _geometryFactory = new();
    private readonly Direct2DHatchTileCache _hatchTiles;
    private float _maximumStrokeWidth;
    private bool _maximumStrokeWidthDirty;
    private bool _disposed;

    public Direct2DResourceCache(
        Direct2DStyleResourceCache styleResources,
        Direct2DTextFormatResourceCache textFormatResources,
        Direct2DRenderStatisticsCollector statistics,
        ID2D1Factory? d2D1Factory = null,
        IDWriteFactory? writeFactory = null,
        ID2D1DeviceContext? deviceContext = null)
    {
        _styleResources = styleResources;
        _textFormatResources = textFormatResources;
        _imageBitmapResources = new Direct2DImageBitmapResourceCache(deviceContext);
        _hatchTiles = new Direct2DHatchTileCache(statistics, deviceContext);
        _geometryRealizations.Reset(deviceContext);
        Factory = d2D1Factory;
        WriteFactory = writeFactory;
        DeviceContext = deviceContext;
    }

    public ID2D1Factory? Factory { get; private set; }
    public IDWriteFactory? WriteFactory { get; private set; }
    public ID2D1DeviceContext? DeviceContext { get; private set; }
    internal Direct2DHatchTileCache HatchTiles => _hatchTiles;
    public long GeometryRealizationEstimatedBytes => _geometryRealizations.EstimatedBytes;
    public long HatchTileEstimatedBytes => _hatchTiles.EstimatedBytes;
    public long ImageBitmapEstimatedBytes => _imageBitmapResources.EstimatedBytes;
    public static long HatchTileCacheBudgetBytes => Direct2DHatchTileCache.CacheBudgetBytes;
    public float MaximumStrokeWidth
    {
        get
        {
            ThrowIfDisposed();
            if (_maximumStrokeWidthDirty)
                RecalculateMaximumStrokeWidth();
            return _maximumStrokeWidth;
        }
    }

    public int EnforceGeometryRealizationBudget()
    {
        ThrowIfDisposed();
        var estimatedBytes = GeometryRealizationEstimatedBytes;
        if (estimatedBytes <= GeometryRealizationCacheBudgetBytes)
            return 0;

        var evictionCount = 0;
        var candidates = new PriorityQueue<
            (Direct2DGeometryRealizationCache.EntityCache Cache,
             Direct2DGeometryRealizationCache.Profile Profile),
            long>();
        foreach (var bucket in _entityResources.Values)
        {
            var cache = bucket.GeometryRealizations;
            if (cache is not null && cache.TryGetOldestProfile(out var profile))
                candidates.Enqueue((cache, profile), profile.LastUsed);
        }

        while (estimatedBytes > GeometryRealizationCacheBudgetBytes &&
               candidates.TryDequeue(out var candidate, out _))
        {
            if (!candidate.Cache.EvictProfile(candidate.Profile))
                continue;

            evictionCount++;
            estimatedBytes = GeometryRealizationEstimatedBytes;
            if (candidate.Cache.TryGetOldestProfile(out var nextProfile))
                candidates.Enqueue((candidate.Cache, nextProfile), nextProfile.LastUsed);
        }

        return evictionCount;
    }

    public IReadOnlyDictionary<EntityId, EntityResourceBucket> EntityResources => _entityResources;

    public void ResetDeviceResources(
        ID2D1Factory? d2D1Factory,
        IDWriteFactory? writeFactory,
        ID2D1DeviceContext? deviceContext,
        CadDocument? document = null)
    {
        ClearEntityResources();
        _imageBitmapResources.Reset(deviceContext);
        _hatchTiles.Reset(deviceContext);
        _geometryRealizations.Reset(deviceContext);
        Factory = d2D1Factory;
        WriteFactory = writeFactory;
        DeviceContext = deviceContext;

        if (document is not null)
            RebuildAll(document);
    }

    public bool TryGetEntityResources(EntityId entityId, out EntityResourceBucket? bucket)
    {
        ThrowIfDisposed();
        return _entityResources.TryGetValue(entityId, out bucket);
    }

    public void BeginFrame()
    {
        ThrowIfDisposed();
        _geometryRealizations.BeginFrame();
    }

    public void BeginGeometryRealizationBuildBatch()
    {
        ThrowIfDisposed();
        _geometryRealizations.BeginBuildBatch();
    }

    public Direct2DGeometryRealizationStatistics CaptureGeometryRealizationStatistics()
    {
        ThrowIfDisposed();
        return _geometryRealizations.CaptureStatistics();
    }

    public IDisposable PushGeometryRealizationScale(double scaleMultiplier)
    {
        ThrowIfDisposed();
        return _geometryRealizations.PushScaleMultiplier(scaleMultiplier);
    }

    public bool TryDrawFilledGeometry(
        ID2D1DeviceContext context,
        CadEntity entity,
        EntityResourceBucket resources,
        ID2D1Geometry geometry,
        ID2D1Brush brush)
    {
        return _geometryRealizations.TryDrawFill(
            context,
            entity,
            resources,
            geometry,
            brush);
    }

    public bool TryDrawStrokedGeometry(
        ID2D1DeviceContext context,
        CadEntity entity,
        EntityResourceBucket resources,
        ID2D1Geometry geometry,
        ID2D1Brush brush,
        float strokeWidth,
        ID2D1StrokeStyle? strokeStyle,
        Direct2DStrokeRealizationStyleKey strokeStyleKey,
        bool strokeWidthChangesWithScale)
    {
        return _geometryRealizations.TryDrawStroke(
            context,
            entity,
            resources,
            geometry,
            brush,
            strokeWidth,
            strokeStyle,
            strokeStyleKey,
            strokeWidthChangesWithScale);
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);
        ThrowIfDisposed();

        foreach (var change in changes.EntityChanges)
        {
            const CadEntityChangeKind resourceIndependentChanges =
                CadEntityChangeKind.DrawOrder |
                CadEntityChangeKind.Metadata |
                CadEntityChangeKind.Opacity |
                CadEntityChangeKind.Rotation;
            var resourceChanges = change.Kind & ~resourceIndependentChanges;
            if (resourceChanges == CadEntityChangeKind.None)
            {
                continue;
            }

            const CadEntityChangeKind fullRebuildChanges =
                CadEntityChangeKind.Created |
                CadEntityChangeKind.Deleted |
                CadEntityChangeKind.Visibility |
                CadEntityChangeKind.Layer |
                CadEntityChangeKind.EmbeddedData;
            if ((resourceChanges & fullRebuildChanges) != 0 ||
                !document.TryGetEntity(change.EntityId, out var entity) ||
                entity is null ||
                !_entityResources.TryGetValue(change.EntityId, out var bucket))
            {
                RebuildEntityResources(document, change.EntityId);
                continue;
            }

            if ((resourceChanges & CadEntityChangeKind.Fill) != 0 &&
                entity is CadCircle or CadEllipse or CadRectangle)
            {
                // Primitive fill geometry exists only for hatch clipping. These
                // entities are never geometry-realization candidates, so rebuilding
                // them remains cheap and keeps their resource state atomic.
                RebuildEntityResources(document, change.EntityId);
                continue;
            }

            if ((resourceChanges & CadEntityChangeKind.Geometry) != 0)
                UpdateGeometryResources(document, entity, bucket);

            if ((resourceChanges & CadEntityChangeKind.Fill) != 0)
                UpdateFillResources(document, entity, bucket);

            if ((resourceChanges & CadEntityChangeKind.Appearance) != 0)
                UpdateAppearanceResources(document, entity, bucket);
        }
    }

    public void RebuildAll(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();

        ClearEntityResources();

        foreach (var entity in document.Entities.Values)
            RebuildEntityResources(document, entity.Id);
    }

    public void RebuildEntityResources(CadDocument document, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();

        if (!CanCreateDeviceResources())
        {
            RemoveEntity(entityId);
            return;
        }

        if (!document.TryGetEntity(entityId, out var entity) || entity is null)
        {
            RemoveEntity(entityId);
            return;
        }

        if (entity.IsErased || !entity.IsVisible)
        {
            RemoveEntity(entityId);
            return;
        }

        if (!document.TryGetLayer(entity.LayerId, out var layer) || layer is null)
        {
            RemoveEntity(entityId);
            return;
        }

        var belongsToReusableBlockDefinition =
            document.TryGetBlock(entity.OwnerBlockId, out var ownerBlock) &&
            ownerBlock is { IsSystem: false };
        if (!belongsToReusableBlockDefinition && (!layer.IsVisible || layer.IsFrozen))
        {
            RemoveEntity(entityId);
            return;
        }

        var newBucket = CreateEntityResources(document, entity, layer);
        if (newBucket.IsEmpty)
        {
            newBucket.Dispose();
            RemoveEntity(entityId);
            return;
        }

        _entityResources.Remove(entityId, out var oldBucket);
        if (oldBucket is not null)
            RemoveStrokeWidthContribution(oldBucket);
        _entityResources[entityId] = newBucket;
        AddStrokeWidthContribution(newBucket);
        oldBucket?.Dispose();
    }

    public void RemoveEntity(EntityId entityId)
    {
        if (_entityResources.Remove(entityId, out var bucket))
        {
            RemoveStrokeWidthContribution(bucket);
            bucket.Dispose();
        }
    }

    public void ClearCache()
    {
        ClearEntityResources();
        _imageBitmapResources.Clear();
    }

    private EntityResourceBucket CreateEntityResources(
        CadDocument document,
        CadEntity entity,
        CadLayer layer)
    {
        var bucket = new EntityResourceBucket(entity.Id);
        try
        {
            var graphic = ResolveGraphicStyle(document, entity, layer);
            bucket.StrokeWidth = ResolveStrokeWidth(
                entity.LineWeight,
                entity.UseLayerLineWeight,
                graphic?.LineWeight,
                layer.LineWeight);
            bucket.StrokeStyleDefinition = entity.StrokeStyle;
            bucket.GraphicLineTypeId = graphic?.LineTypeId ?? LineTypeId.Continuous;
            if (UsesStrokeBrush(entity))
            {
                var strokeColor = ResolveStrokeColor(document, entity, layer, graphic);
                if (!strokeColor.IsTransparent)
                {
                    bucket.StrokeColor = strokeColor;
                    bucket.StrokeBrushLease = _styleResources.AcquireBrush(strokeColor);
                }
            }
            if (UsesStrokeStyle(entity))
            {
                bucket.StrokeStyleLease = _styleResources.AcquireStrokeStyle(entity.StrokeStyle);
                if (bucket.StrokeStyleLease is null &&
                    graphic is not null &&
                    graphic.LineTypeId != LineTypeId.Continuous &&
                    document.LineTypes.TryGetValue(graphic.LineTypeId, out var lineType))
                {
                    bucket.GraphicLineTypeLease = _styleResources.AcquireLineTypeStrokeStyle(lineType);
                }
            }

            var hasFillStyle = TryResolveFillStyle(document, entity, out var fillStyle);
            (bucket.Geometry, bucket.GeometryComplexity) = CreateGeometry(
                entity,
                fillStyle is CadHatchFillStyle);

            if (hasFillStyle)
            {
                switch (fillStyle)
                {
                    case CadGradientFillStyle { IsSolid: true } gradient:
                        var fillColor = gradient.Stops[0].Color;
                        if (!fillColor.IsTransparent)
                            bucket.FillBrushLease = _styleResources.AcquireBrush(fillColor);
                        break;

                    case CadHatchFillStyle hatch when document.TryGetHatchPattern(hatch.PatternId, out var pattern) &&
                                                      pattern is not null:
                        bucket.HatchFillStyle = hatch;
                        bucket.HatchPattern = pattern;
                        bucket.HatchRenderData = new CadTransientHatchFill(
                            hatch.ForegroundColor,
                            hatch.HatchScale,
                            hatch.HatchAngle,
                            hatch.HatchOrigin,
                            pattern.Lines);
                        if (!hatch.ForegroundColor.IsTransparent)
                            bucket.HatchBrushLease = _styleResources.AcquireBrush(hatch.ForegroundColor);
                        break;
                }
            }

            if (entity is CadText text)
            {
                bucket.TextFormatLease = _textFormatResources.Acquire(document, text);
                if (WriteFactory is not null && bucket.TextFormat is not null)
                {
                    bucket.TextLayout = Direct2DTextServices.CreateTextLayout(
                        WriteFactory,
                        text.Text,
                        bucket.TextFormat);
                }
            }

            if (entity is CadImage image)
            {
                bucket.BitmapLease = _imageBitmapResources.Acquire(image);
                if (bucket.Bitmap is not null)
                    bucket.BitmapBrush = CreateBitmapBrush(image.FrameBounds, image.PixelWidth, image.PixelHeight, bucket.Bitmap);
            }

            return bucket;
        }
        catch
        {
            bucket.Dispose();
            throw;
        }
    }

    private void UpdateAppearanceResources(
        CadDocument document,
        CadEntity entity,
        EntityResourceBucket bucket)
    {
        if (!document.TryGetLayer(entity.LayerId, out var layer) || layer is null)
        {
            RebuildEntityResources(document, entity.Id);
            return;
        }

        var graphic = ResolveGraphicStyle(document, entity, layer);
        KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? strokeBrushLease = null;
        KeyedResourceLease<ID2D1StrokeStyle, Direct2DStyleResourceCache.StrokeStyleKey>? strokeStyleLease = null;
        KeyedResourceLease<ID2D1StrokeStyle, Direct2DStyleResourceCache.StrokeStyleKey>? graphicLineTypeLease = null;
        CadColor? strokeColor = null;
        try
        {
            if (UsesStrokeBrush(entity))
            {
                var resolvedStrokeColor = ResolveStrokeColor(document, entity, layer, graphic);
                if (!resolvedStrokeColor.IsTransparent)
                {
                    strokeColor = resolvedStrokeColor;
                    strokeBrushLease = _styleResources.AcquireBrush(resolvedStrokeColor);
                }
            }
            if (UsesStrokeStyle(entity))
            {
                strokeStyleLease = _styleResources.AcquireStrokeStyle(entity.StrokeStyle);
                if (strokeStyleLease is null &&
                    graphic is not null &&
                    graphic.LineTypeId != LineTypeId.Continuous &&
                    document.LineTypes.TryGetValue(graphic.LineTypeId, out var lineType))
                {
                    graphicLineTypeLease = _styleResources.AcquireLineTypeStrokeStyle(lineType);
                }
            }
        }
        catch
        {
            strokeBrushLease?.Dispose();
            strokeStyleLease?.Dispose();
            graphicLineTypeLease?.Dispose();
            throw;
        }

        var strokeWidth = ResolveStrokeWidth(
            entity.LineWeight,
            entity.UseLayerLineWeight,
            graphic?.LineWeight,
            layer.LineWeight);
        var strokeRealizationChanged =
            Math.Abs(bucket.StrokeWidth - strokeWidth) >
            Math.Max(1e-6f, Math.Abs(strokeWidth) * 1e-5f) ||
            bucket.StrokeStyleDefinition != entity.StrokeStyle ||
            bucket.GraphicLineTypeId != (graphic?.LineTypeId ?? LineTypeId.Continuous);

        RemoveStrokeWidthContribution(bucket);
        if (strokeRealizationChanged)
            bucket.GeometryRealizations?.ClearStroke();
        bucket.StrokeBrushLease?.Dispose();
        bucket.StrokeStyleLease?.Dispose();
        bucket.GraphicLineTypeLease?.Dispose();
        bucket.StrokeColor = strokeColor;
        bucket.StrokeBrushLease = strokeBrushLease;
        bucket.StrokeStyleLease = strokeStyleLease;
        bucket.GraphicLineTypeLease = graphicLineTypeLease;
        bucket.GraphicLineTypeId = graphic?.LineTypeId ?? LineTypeId.Continuous;
        bucket.StrokeWidth = strokeWidth;
        bucket.StrokeStyleDefinition = entity.StrokeStyle;
        AddStrokeWidthContribution(bucket);
    }

    private void UpdateFillResources(
        CadDocument document,
        CadEntity entity,
        EntityResourceBucket bucket)
    {
        KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? fillBrushLease = null;
        KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? hatchBrushLease = null;
        CadHatchFillStyle? hatchFillStyle = null;
        CadHatchPatternDefinition? hatchPattern = null;
        CadTransientHatchFill? hatchRenderData = null;
        try
        {
            if (TryResolveFillStyle(document, entity, out var fillStyle))
            {
                switch (fillStyle)
                {
                    case CadGradientFillStyle { IsSolid: true } gradient:
                        var fillColor = gradient.Stops[0].Color;
                        if (!fillColor.IsTransparent)
                            fillBrushLease = _styleResources.AcquireBrush(fillColor);
                        break;

                    case CadHatchFillStyle hatch
                        when document.TryGetHatchPattern(hatch.PatternId, out var pattern) &&
                             pattern is not null:
                        hatchFillStyle = hatch;
                        hatchPattern = pattern;
                        hatchRenderData = new CadTransientHatchFill(
                            hatch.ForegroundColor,
                            hatch.HatchScale,
                            hatch.HatchAngle,
                            hatch.HatchOrigin,
                            pattern.Lines);
                        if (!hatch.ForegroundColor.IsTransparent)
                            hatchBrushLease = _styleResources.AcquireBrush(hatch.ForegroundColor);
                        break;
                }
            }
        }
        catch
        {
            fillBrushLease?.Dispose();
            hatchBrushLease?.Dispose();
            throw;
        }

        bucket.FillBrushLease?.Dispose();
        bucket.HatchBrushLease?.Dispose();
        bucket.FillBrushLease = fillBrushLease;
        bucket.HatchBrushLease = hatchBrushLease;
        bucket.HatchFillStyle = hatchFillStyle;
        bucket.HatchPattern = hatchPattern;
        bucket.HatchRenderData = hatchRenderData;
    }

    private void UpdateGeometryResources(
        CadDocument document,
        CadEntity entity,
        EntityResourceBucket bucket)
    {
        if (entity is CadText text)
        {
            if (text.RequiresBoundsMeasurement)
                UpdateTextResources(document, text, bucket);
            return;
        }

        if (entity is CadImage image)
        {
            var bitmapBrush = bucket.Bitmap is null
                ? null
                : CreateBitmapBrush(
                    image.FrameBounds,
                    image.PixelWidth,
                    image.PixelHeight,
                    bucket.Bitmap);
            bucket.BitmapBrush?.Dispose();
            bucket.BitmapBrush = bitmapBrush;
            return;
        }

        ID2D1Geometry? geometry = null;
        var geometryComplexity = 0;
        try
        {
            (geometry, geometryComplexity) = CreateGeometry(
                entity,
                bucket.HatchFillStyle is CadHatchFillStyle);
        }
        catch
        {
            geometry?.Dispose();
            throw;
        }

        bucket.GeometryRealizations?.Clear();
        bucket.Geometry?.Dispose();
        bucket.MediumDetailGeometry?.Dispose();
        bucket.LowDetailGeometry?.Dispose();
        bucket.Geometry = geometry;
        bucket.GeometryComplexity = geometryComplexity;
        bucket.MediumDetailGeometry = null;
        bucket.LowDetailGeometry = null;
        bucket.AreLevelOfDetailGeometriesInitialized = false;
    }

    internal void EnsureLevelOfDetailGeometries(
        CadEntity entity,
        EntityResourceBucket bucket)
    {
        ThrowIfDisposed();
        if (bucket.AreLevelOfDetailGeometriesInitialized ||
            entity is not (CadPolyline or CadSpline) ||
            Factory is null)
        {
            return;
        }

        ID2D1Geometry? medium = null;
        ID2D1Geometry? low = null;
        try
        {
            (medium, low) = CreateGeometryLods(entity);
            bucket.MediumDetailGeometry = medium;
            bucket.LowDetailGeometry = low;
            bucket.AreLevelOfDetailGeometriesInitialized = true;
        }
        catch
        {
            medium?.Dispose();
            low?.Dispose();
            throw;
        }
    }

    private void UpdateTextResources(
        CadDocument document,
        CadText text,
        EntityResourceBucket bucket)
    {
        KeyedResourceLease<IDWriteTextFormat, Direct2DTextFormatKey>? textFormatLease = null;
        IDWriteTextLayout? textLayout = null;
        try
        {
            textFormatLease = _textFormatResources.Acquire(document, text);
            if (WriteFactory is not null && textFormatLease?.Resource is { } textFormat)
            {
                textLayout = Direct2DTextServices.CreateTextLayout(
                    WriteFactory,
                    text.Text,
                    textFormat);
            }
        }
        catch
        {
            textLayout?.Dispose();
            textFormatLease?.Dispose();
            throw;
        }

        bucket.TextLayout?.Dispose();
        bucket.TextFormatLease?.Dispose();
        bucket.TextFormatLease = textFormatLease;
        bucket.TextLayout = textLayout;
    }

    private (ID2D1Geometry? Geometry, int Complexity) CreateGeometry(
        CadEntity entity,
        bool includePrimitiveFillGeometry)
    {
        return entity switch
        {
            CadCircle circle when includePrimitiveFillGeometry => (
                Factory!.CreateEllipseGeometry(
                    new Ellipse(ToVector2(circle.Center), (float)circle.Radius, (float)circle.Radius)),
                0),
            CadEllipse ellipse when includePrimitiveFillGeometry => (
                Factory!.CreateEllipseGeometry(
                    new Ellipse(ToVector2(ellipse.Center), (float)ellipse.RadiusX, (float)ellipse.RadiusY)),
                0),
            CadEllipseArc ellipseArc => (CreateEllipseArcPathGeometry(ellipseArc), 0),
            CadRectangle rectangle when includePrimitiveFillGeometry => (CreateRectangleGeometry(rectangle), 0),
            CadArc arc when !arc.IsFullCircle => (CreateArcPathGeometry(arc), 0),
            CadPolyline polyline => (
                CreatePolylineGeometry(polyline.Points, polyline.Closed),
                polyline.Points.Count),
            CadSpline spline => CreateSplineGeometry(spline.GetBezierSegments(), spline.Closed),
            CadCompositePath path => (
                _geometryFactory.CreateCompositePath(Factory!, path),
                path.Segments.Count),
            CadShapeText shapeText => CreateShapeTextGeometry(shapeText.CreateStrokeSegments()),
            _ => (null, 0)
        };
    }

    private (ID2D1Geometry? Medium, ID2D1Geometry? Low) CreateGeometryLods(CadEntity entity)
    {
        IReadOnlyList<CadPointD> points;
        bool closed;
        int sourceComplexity;
        switch (entity)
        {
            case CadPolyline polyline when polyline.Points.Count > 16:
                points = polyline.Points;
                closed = polyline.Closed;
                sourceComplexity = polyline.Points.Count;
                break;
            case CadSpline { Closed: true, FillStyleId: not null }:
                // RDP preserves distance but not winding topology. A simplified closed
                // fill can therefore grow thin spikes around self-intersections.
                return (null, null);
            case CadSpline spline when spline.FitPoints.Count > 16:
                points = spline.EnumerateFlattenedPoints(6).ToArray();
                closed = spline.Closed;
                sourceComplexity = points.Count;
                break;
            default:
                return (null, null);
        }

        var maximumExtent = Math.Max(entity.Bounds.Width, entity.Bounds.Height);
        if (!double.IsFinite(maximumExtent) || maximumExtent <= double.Epsilon)
            return (null, null);

        ID2D1Geometry? medium = null;
        ID2D1Geometry? low = null;
        try
        {
            var mediumPoints = CadPointLodSimplifier.Simplify(
                points,
                closed,
                maximumExtent / 1024.0);
            if (IsWorthCaching(mediumPoints.Count, sourceComplexity, closed))
                medium = CreatePolylineGeometry(mediumPoints, closed);

            var lowPoints = CadPointLodSimplifier.Simplify(
                points,
                closed,
                maximumExtent / 256.0);
            var comparisonCount = mediumPoints.Count < sourceComplexity
                ? mediumPoints.Count
                : sourceComplexity;
            if (IsWorthCaching(lowPoints.Count, comparisonCount, closed))
                low = CreatePolylineGeometry(lowPoints, closed);

            return (medium, low);
        }
        catch
        {
            medium?.Dispose();
            low?.Dispose();
            throw;
        }
    }

    private static bool IsWorthCaching(int simplifiedCount, int sourceCount, bool closed)
    {
        var minimumCount = closed ? 3 : 2;
        return simplifiedCount >= minimumCount &&
               simplifiedCount <= sourceCount * 3 / 4;
    }

    private ID2D1BitmapBrush? CreateBitmapBrush(CadRectD bounds, int pixelWidth, int pixelHeight, ID2D1Bitmap bitmap)
    {
        if (DeviceContext is null || bounds.IsEmpty)
            return null;

        var transform =
            Matrix3x2.CreateScale(
                (float)(bounds.Width / pixelWidth),
                (float)(bounds.Height / pixelHeight)) *
            Matrix3x2.CreateTranslation(
                (float)bounds.MinX,
                (float)bounds.MinY);

        return DeviceContext.CreateBitmapBrush(
            bitmap,
            new BitmapBrushProperties1(
                ExtendMode.Clamp,
                ExtendMode.Clamp,
                InterpolationMode.Linear),
            new BrushProperties(1.0f, transform));
    }

    private ID2D1PathGeometry CreatePolylineGeometry(IReadOnlyList<CadPointD> points, bool closed)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(points[0]), closed ? FigureBegin.Filled : FigureBegin.Hollow);

        for (var i = 1; i < points.Count; i++)
            sink.AddLine(ToVector2(points[i]));

        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private (ID2D1Geometry Geometry, int Complexity) CreateSplineGeometry(
        IReadOnlyList<CadBezierSegmentD> segments,
        bool closed)
    {
        var geometry = Factory!.CreatePathGeometry();
        if (segments.Count == 0)
            return (geometry, 0);

        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(segments[0].Start), closed ? FigureBegin.Filled : FigureBegin.Hollow);

        foreach (var segment in segments)
        {
            sink.AddBezier(new BezierSegment(
                ToVector2(segment.Control1),
                ToVector2(segment.Control2),
                ToVector2(segment.End)));
        }

        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return (geometry, segments.Count);
    }

    private (ID2D1Geometry Geometry, int Complexity) CreateShapeTextGeometry(
        IReadOnlyList<CadStrokeTextSegment> segments)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();

        foreach (var segment in segments)
        {
            sink.BeginFigure(ToVector2(segment.Start), FigureBegin.Hollow);
            sink.AddLine(ToVector2(segment.End));
            sink.EndFigure(FigureEnd.Open);
        }

        sink.Close();
        return (geometry, segments.Count);
    }

    private ID2D1PathGeometry CreateArcPathGeometry(CadArc arc)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(arc.StartPoint), FigureBegin.Hollow);
        sink.AddArc(CreateArcSegment(arc.EndPoint, arc.Radius, arc.SweepAngleRadians));
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private ID2D1PathGeometry CreateEllipseArcPathGeometry(CadEllipseArc ellipseArc)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(ellipseArc.StartPoint), FigureBegin.Hollow);
        sink.AddArc(CreateEllipseArcSegment(
            ellipseArc.EndPoint,
            ellipseArc.RadiusX,
            ellipseArc.RadiusY,
            ellipseArc.SweepAngleRadians));
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private static ArcSegment CreateEllipseArcSegment(
        CadPointD endPoint,
        double radiusX,
        double radiusY,
        double sweepAngleRadians)
    {
        return new ArcSegment(
            ToVector2(endPoint),
            new Size((float)radiusX, (float)radiusY),
            rotationAngle: 0,
            ToD2DSweepDirection(sweepAngleRadians),
            Math.Abs(sweepAngleRadians) > Math.PI ? ArcSize.Large : ArcSize.Small);
    }

    private static ArcSegment CreateArcSegment(
        CadPointD endPoint,
        double radius,
        double sweepAngleRadians)
    {
        return new ArcSegment(
            ToVector2(endPoint),
            new Size((float)radius, (float)radius),
            rotationAngle: 0,
            ToD2DSweepDirection(sweepAngleRadians),
            Math.Abs(sweepAngleRadians) > Math.PI ? ArcSize.Large : ArcSize.Small);
    }

    private static SweepDirection ToD2DSweepDirection(double sweepAngleRadians)
    {
        // The current viewport keeps Y increasing downward, so this maps to the
        // same visual direction as CadArc.GetPointAtAngle's Y + sin(angle).
        return sweepAngleRadians >= 0
            ? SweepDirection.Clockwise
            : SweepDirection.CounterClockwise;
    }

    private ID2D1Geometry CreateRectangleGeometry(CadRectangle rectangle)
    {
        var bounds = rectangle.Bounds;
        var radiusX = ClampCornerRadius(rectangle.CornerRadiusX, bounds.Width);
        var radiusY = ClampCornerRadius(rectangle.CornerRadiusY, bounds.Height);
        if (radiusX > 0 && radiusY > 0)
        {
            return Factory!.CreateRoundedRectangleGeometry(new RoundedRectangle(
                new RawRectF(
                    (float)bounds.MinX,
                    (float)bounds.MinY,
                    (float)bounds.MaxX,
                    (float)bounds.MaxY),
                (float)radiusX,
                (float)radiusY));
        }

        return Factory!.CreateRectangleGeometry(new RawRectF(
            (float)bounds.MinX,
            (float)bounds.MinY,
            (float)bounds.MaxX,
            (float)bounds.MaxY));
    }

    private static double ClampCornerRadius(double radius, double size)
    {
        if (!double.IsFinite(radius) || !double.IsFinite(size) || radius <= 0 || size <= 0)
            return 0;
        return Math.Min(radius, size * 0.5);
    }

    private CadGraphicStyle? ResolveGraphicStyle(
        CadDocument document,
        CadEntity entity,
        CadLayer layer)
    {
        var styleId = GetGraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
        if (styleId is null)
            return null;

        return document.TryGetStyle(styleId.Value, out var style) && style is CadGraphicStyle graphic
            ? graphic
            : null;
    }

    private static StyleId? GetGraphicStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadCompositePath path => path.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static bool UsesStrokeBrush(CadEntity entity)
    {
        return entity is CadLine or
            CadCircle or
            CadEllipse or
            CadEllipseArc or
            CadRectangle or
            CadArc or
            CadPolyline or
            CadSpline or
            CadCompositePath or
            CadText or
            CadShapeText;
    }

    private static bool UsesStrokeStyle(CadEntity entity)
    {
        return entity is CadLine or
            CadCircle or
            CadEllipse or
            CadEllipseArc or
            CadRectangle or
            CadArc or
            CadPolyline or
            CadSpline or
            CadCompositePath or
            CadShapeText;
    }

    private static float ResolveStrokeWidth(
        CadLineWeight? entityWeight,
        bool useLayerLineWeight,
        CadLineWeight? styleWeight,
        CadLineWeight layerWeight)
    {
        var weight = useLayerLineWeight
            ? layerWeight
            : entityWeight switch
            {
                { IsByLayer: false } explicitWeight => explicitWeight,
                { IsByLayer: true } => layerWeight,
                _ => styleWeight is { IsByLayer: false }
                    ? styleWeight.Value
                    : layerWeight
            };

        if (weight.IsByLayer || weight.Value <= 0)
            weight = CadLineWeight.Default;

        return (float)Math.Max(weight.Value, 0.01);
    }

    private static CadColor ResolveStrokeColor(
        CadDocument document,
        CadEntity entity,
        CadLayer layer,
        CadGraphicStyle? graphic)
    {
        return entity.ColorSource == CadColorSource.Explicit
            ? graphic?.StrokeColor ?? ResolveLayerStrokeColor(document, layer)
            : ResolveLayerStrokeColor(document, layer);
    }

    private static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        if (layer.DefaultGraphicStyleId is { } styleId &&
            document.TryGetStyle(styleId, out var style) &&
            style is CadGraphicStyle graphic)
        {
            return graphic.StrokeColor;
        }

        return layer.Color;
    }

    private static bool TryResolveFillStyle(CadDocument document, CadEntity entity, out CadFillStyle fillStyle)
    {
        var fillStyleId = entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            CadSpline { Closed: true } spline => spline.FillStyleId,
            CadCompositePath { Closed: true } path => path.FillStyleId,
            _ => null
        };

        if (fillStyleId is null ||
            !document.TryGetStyle(fillStyleId.Value, out var style) ||
            style is not CadFillStyle resolvedFillStyle)
        {
            fillStyle = default!;
            return false;
        }

        fillStyle = resolvedFillStyle;
        return true;
    }

    private bool CanCreateDeviceResources()
    {
        return Factory is not null && DeviceContext is not null;
    }

    private void ClearEntityResources()
    {
        foreach (var bucket in _entityResources.Values)
            bucket.Dispose();

        _entityResources.Clear();
        _maximumStrokeWidth = 0;
        _maximumStrokeWidthDirty = false;
    }

    private void AddStrokeWidthContribution(EntityResourceBucket bucket)
    {
        var strokeWidth = ResolvePaintedStrokeWidth(bucket);
        if (strokeWidth > _maximumStrokeWidth)
            _maximumStrokeWidth = strokeWidth;
    }

    private void RemoveStrokeWidthContribution(EntityResourceBucket bucket)
    {
        var strokeWidth = ResolvePaintedStrokeWidth(bucket);
        if (strokeWidth > 0 &&
            strokeWidth >= _maximumStrokeWidth - 1e-6f)
        {
            _maximumStrokeWidthDirty = true;
        }
    }

    private void RecalculateMaximumStrokeWidth()
    {
        var maximum = 0.0f;
        foreach (var bucket in _entityResources.Values)
            maximum = Math.Max(maximum, ResolvePaintedStrokeWidth(bucket));

        _maximumStrokeWidth = maximum;
        _maximumStrokeWidthDirty = false;
    }

    private static float ResolvePaintedStrokeWidth(EntityResourceBucket bucket)
    {
        return bucket.StrokeBrush is null
            ? 0
            : Math.Max(0, bucket.StrokeWidth);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ClearCache();
        _imageBitmapResources.Dispose();
        _hatchTiles.Dispose();
        _geometryRealizations.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DResourceCache));
    }

    private static Vector2 ToVector2(CadPointD point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    internal sealed class EntityResourceBucket : IDisposable
    {
        public EntityId EntityId { get; }
        public ID2D1Geometry? Geometry { get; set; }
        public ID2D1Geometry? MediumDetailGeometry { get; set; }
        public ID2D1Geometry? LowDetailGeometry { get; set; }
        public bool AreLevelOfDetailGeometriesInitialized { get; set; }
        public int GeometryComplexity { get; set; }
        public Direct2DGeometryRealizationCache.EntityCache? GeometryRealizations { get; set; }
        internal KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? StrokeBrushLease { get; set; }
        internal KeyedResourceLease<ID2D1StrokeStyle, Direct2DStyleResourceCache.StrokeStyleKey>? StrokeStyleLease { get; set; }
        internal KeyedResourceLease<ID2D1StrokeStyle, Direct2DStyleResourceCache.StrokeStyleKey>? GraphicLineTypeLease { get; set; }
        internal KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? FillBrushLease { get; set; }
        internal KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? HatchBrushLease { get; set; }
        public ID2D1Brush? StrokeBrush => StrokeBrushLease?.Resource;
        public CadColor? StrokeColor { get; set; }
        public ID2D1StrokeStyle? StrokeStyle => StrokeStyleLease?.Resource;
        public ID2D1StrokeStyle? GraphicLineTypeStrokeStyle => GraphicLineTypeLease?.Resource;
        public ID2D1Brush? FillBrush => FillBrushLease?.Resource;
        public ID2D1Brush? HatchBrush => HatchBrushLease?.Resource;
        public CadHatchFillStyle? HatchFillStyle { get; set; }
        public CadHatchPatternDefinition? HatchPattern { get; set; }
        public CadTransientHatchFill? HatchRenderData { get; set; }
        internal KeyedResourceLease<IDWriteTextFormat, Direct2DTextFormatKey>? TextFormatLease { get; set; }
        public IDWriteTextFormat? TextFormat => TextFormatLease?.Resource;
        public IDWriteTextLayout? TextLayout { get; set; }
        internal KeyedResourceLease<ID2D1Bitmap, Direct2DImageBitmapResourceCache.ImageBitmapKey>? BitmapLease { get; set; }
        public ID2D1Bitmap? Bitmap => BitmapLease?.Resource;
        public ID2D1BitmapBrush? BitmapBrush { get; set; }
        public float StrokeWidth { get; set; }
        public CadStrokeStyle StrokeStyleDefinition { get; set; } = CadStrokeStyle.Default;
        public LineTypeId GraphicLineTypeId { get; set; } = LineTypeId.Continuous;

        public bool IsEmpty =>
            Geometry is null &&
            MediumDetailGeometry is null &&
            LowDetailGeometry is null &&
            StrokeBrush is null &&
            StrokeStyle is null &&
            GraphicLineTypeStrokeStyle is null &&
            FillBrush is null &&
            HatchBrush is null &&
            TextFormat is null &&
            TextLayout is null &&
            Bitmap is null &&
            BitmapBrush is null;

        public EntityResourceBucket(EntityId entityId)
        {
            EntityId = entityId;
        }

        public void Dispose()
        {
            GeometryRealizations?.Dispose();
            Geometry?.Dispose();
            MediumDetailGeometry?.Dispose();
            LowDetailGeometry?.Dispose();
            StrokeBrushLease?.Dispose();
            StrokeStyleLease?.Dispose();
            GraphicLineTypeLease?.Dispose();
            FillBrushLease?.Dispose();
            HatchBrushLease?.Dispose();
            TextLayout?.Dispose();
            TextFormatLease?.Dispose();
            BitmapBrush?.Dispose();
            BitmapLease?.Dispose();
            Geometry = null;
            MediumDetailGeometry = null;
            LowDetailGeometry = null;
            AreLevelOfDetailGeometriesInitialized = false;
            GeometryComplexity = 0;
            GeometryRealizations = null;
            StrokeColor = null;
            StrokeStyleDefinition = CadStrokeStyle.Default;
            StrokeBrushLease = null;
            StrokeStyleLease = null;
            GraphicLineTypeLease = null;
            FillBrushLease = null;
            HatchBrushLease = null;
            HatchFillStyle = null;
            HatchPattern = null;
            HatchRenderData = null;
            TextLayout = null;
            TextFormatLease = null;
            BitmapBrush = null;
            BitmapLease = null;
        }
    }
}
