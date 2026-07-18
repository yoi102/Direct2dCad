using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DResourceCache : IDisposable
{
    private readonly Dictionary<EntityId, EntityResourceBucket> _entityResources = [];
    private readonly Direct2DStyleResourceCache _styleResources;
    private readonly Direct2DTextFormatResourceCache _textFormatResources;
    private readonly Direct2DImageBitmapResourceCache _imageBitmapResources;
    private bool _disposed;

    public Direct2DResourceCache(
        Direct2DStyleResourceCache styleResources,
        Direct2DTextFormatResourceCache textFormatResources,
        ID2D1Factory? d2D1Factory = null,
        IDWriteFactory? writeFactory = null,
        ID2D1DeviceContext? deviceContext = null)
    {
        _styleResources = styleResources;
        _textFormatResources = textFormatResources;
        _imageBitmapResources = new Direct2DImageBitmapResourceCache(deviceContext);
        Factory = d2D1Factory;
        WriteFactory = writeFactory;
        DeviceContext = deviceContext;
    }

    public ID2D1Factory? Factory { get; private set; }
    public IDWriteFactory? WriteFactory { get; private set; }
    public ID2D1DeviceContext? DeviceContext { get; private set; }

    public IReadOnlyDictionary<EntityId, EntityResourceBucket> EntityResources => _entityResources;

    public void ResetDeviceResources(
        ID2D1Factory? d2D1Factory,
        IDWriteFactory? writeFactory,
        ID2D1DeviceContext? deviceContext,
        CadDocument? document = null)
    {
        ClearEntityResources();
        _imageBitmapResources.Reset(deviceContext);
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
                CadEntityChangeKind.Fill |
                CadEntityChangeKind.EmbeddedData;
            if ((resourceChanges & fullRebuildChanges) != 0 ||
                !document.TryGetEntity(change.EntityId, out var entity) ||
                entity is null ||
                !_entityResources.TryGetValue(change.EntityId, out var bucket))
            {
                RebuildEntityResources(document, change.EntityId);
                continue;
            }

            if ((resourceChanges & CadEntityChangeKind.Geometry) != 0)
                UpdateGeometryResources(document, entity, bucket);

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
        _entityResources[entityId] = newBucket;
        oldBucket?.Dispose();
    }

    public void RemoveEntity(EntityId entityId)
    {
        if (_entityResources.Remove(entityId, out var bucket))
            bucket.Dispose();
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
            if (UsesStrokeBrush(entity))
            {
                bucket.StrokeBrushLease = _styleResources.AcquireBrush(
                    ResolveStrokeColor(document, entity, layer, graphic));
            }
            if (UsesStrokeStyle(entity))
                bucket.StrokeStyleLease = _styleResources.AcquireStrokeStyle(entity.StrokeStyle);

            var hasFillStyle = TryResolveFillStyle(document, entity, out var fillStyle);
            bucket.Geometry = CreateGeometry(entity, fillStyle is CadHatchFillStyle);
            (bucket.MediumDetailGeometry, bucket.LowDetailGeometry) =
                CreateGeometryLods(entity);

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
        ResourceLease<ID2D1StrokeStyle>? strokeStyleLease = null;
        try
        {
            if (UsesStrokeBrush(entity))
            {
                strokeBrushLease = _styleResources.AcquireBrush(
                    ResolveStrokeColor(document, entity, layer, graphic));
            }
            if (UsesStrokeStyle(entity))
                strokeStyleLease = _styleResources.AcquireStrokeStyle(entity.StrokeStyle);
        }
        catch
        {
            strokeBrushLease?.Dispose();
            strokeStyleLease?.Dispose();
            throw;
        }

        bucket.StrokeBrushLease?.Dispose();
        bucket.StrokeStyleLease?.Dispose();
        bucket.StrokeBrushLease = strokeBrushLease;
        bucket.StrokeStyleLease = strokeStyleLease;
        bucket.StrokeWidth = ResolveStrokeWidth(
            entity.LineWeight,
            entity.UseLayerLineWeight,
            graphic?.LineWeight,
            layer.LineWeight);
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
        ID2D1Geometry? mediumDetailGeometry = null;
        ID2D1Geometry? lowDetailGeometry = null;
        try
        {
            geometry = CreateGeometry(entity, bucket.HatchFillStyle is CadHatchFillStyle);
            (mediumDetailGeometry, lowDetailGeometry) = CreateGeometryLods(entity);
        }
        catch
        {
            geometry?.Dispose();
            mediumDetailGeometry?.Dispose();
            lowDetailGeometry?.Dispose();
            throw;
        }

        bucket.Geometry?.Dispose();
        bucket.MediumDetailGeometry?.Dispose();
        bucket.LowDetailGeometry?.Dispose();
        bucket.Geometry = geometry;
        bucket.MediumDetailGeometry = mediumDetailGeometry;
        bucket.LowDetailGeometry = lowDetailGeometry;
    }

    private void UpdateTextResources(
        CadDocument document,
        CadText text,
        EntityResourceBucket bucket)
    {
        ResourceLease<IDWriteTextFormat>? textFormatLease = null;
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

    private ID2D1Geometry? CreateGeometry(CadEntity entity, bool includePrimitiveFillGeometry)
    {
        return entity switch
        {
            CadLine => null,
            CadCircle circle when includePrimitiveFillGeometry => Factory!.CreateEllipseGeometry(
                new Ellipse(ToVector2(circle.Center), (float)circle.Radius, (float)circle.Radius)),
            CadCircle => null,
            CadEllipse ellipse when includePrimitiveFillGeometry => Factory!.CreateEllipseGeometry(
                new Ellipse(ToVector2(ellipse.Center), (float)ellipse.RadiusX, (float)ellipse.RadiusY)),
            CadEllipse => null,
            CadEllipseArc ellipseArc => CreateEllipseArcPathGeometry(ellipseArc),
            CadRectangle rectangle when includePrimitiveFillGeometry => CreateRectangleGeometry(rectangle),
            CadRectangle => null,
            CadArc arc => arc.IsFullCircle ? null : CreateArcPathGeometry(arc),
            CadPolyline polyline => CreatePolylineGeometry(polyline.Points, polyline.Closed),
            CadSpline spline => CreateSplineGeometry(spline.FitPoints, spline.Closed),
            CadShapeText shapeText => CreateShapeTextGeometry(shapeText),
            CadText => null,
            CadBlockReference => null,
            _ => null
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
            case CadSpline spline when spline.FitPoints.Count > 16:
                points = spline.EnumerateFlattenedPoints(6).ToArray();
                closed = spline.Closed;
                sourceComplexity = spline.FitPoints.Count;
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

    private ID2D1PathGeometry CreateSplineGeometry(IReadOnlyList<CadPointD> fitPoints, bool closed)
    {
        var geometry = Factory!.CreatePathGeometry();
        var segments = CadSpline.CreateBezierSegments(fitPoints, closed);
        if (segments.Count == 0)
            return geometry;

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
        return geometry;
    }

    private ID2D1PathGeometry CreateShapeTextGeometry(CadShapeText text)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();

        foreach (var segment in text.CreateStrokeSegments())
        {
            sink.BeginFigure(ToVector2(segment.Start), FigureBegin.Hollow);
            sink.AddLine(ToVector2(segment.End));
            sink.EndFigure(FigureEnd.Open);
        }

        sink.Close();
        return geometry;
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
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ClearCache();
        _imageBitmapResources.Dispose();
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
        internal KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? StrokeBrushLease { get; set; }
        internal ResourceLease<ID2D1StrokeStyle>? StrokeStyleLease { get; set; }
        internal KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? FillBrushLease { get; set; }
        internal KeyedResourceLease<ID2D1SolidColorBrush, CadColor>? HatchBrushLease { get; set; }
        public ID2D1Brush? StrokeBrush => StrokeBrushLease?.Resource;
        public ID2D1StrokeStyle? StrokeStyle => StrokeStyleLease?.Resource;
        public ID2D1Brush? FillBrush => FillBrushLease?.Resource;
        public ID2D1Brush? HatchBrush => HatchBrushLease?.Resource;
        public CadHatchFillStyle? HatchFillStyle { get; set; }
        public CadHatchPatternDefinition? HatchPattern { get; set; }
        public CadTransientHatchFill? HatchRenderData { get; set; }
        internal ResourceLease<IDWriteTextFormat>? TextFormatLease { get; set; }
        public IDWriteTextFormat? TextFormat => TextFormatLease?.Resource;
        public IDWriteTextLayout? TextLayout { get; set; }
        internal ResourceLease<ID2D1Bitmap>? BitmapLease { get; set; }
        public ID2D1Bitmap? Bitmap => BitmapLease?.Resource;
        public ID2D1BitmapBrush? BitmapBrush { get; set; }
        public float StrokeWidth { get; set; }

        public bool IsEmpty =>
            Geometry is null &&
            MediumDetailGeometry is null &&
            LowDetailGeometry is null &&
            StrokeBrush is null &&
            StrokeStyle is null &&
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
            Geometry?.Dispose();
            MediumDetailGeometry?.Dispose();
            LowDetailGeometry?.Dispose();
            StrokeBrushLease?.Dispose();
            StrokeStyleLease?.Dispose();
            FillBrushLease?.Dispose();
            HatchBrushLease?.Dispose();
            TextLayout?.Dispose();
            TextFormatLease?.Dispose();
            BitmapBrush?.Dispose();
            BitmapLease?.Dispose();
            Geometry = null;
            MediumDetailGeometry = null;
            LowDetailGeometry = null;
            StrokeBrushLease = null;
            StrokeStyleLease = null;
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
