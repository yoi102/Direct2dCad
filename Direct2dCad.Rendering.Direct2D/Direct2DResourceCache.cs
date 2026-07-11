using System.Numerics;
using System.Runtime.InteropServices;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DResourceCache : IDisposable
{
    private readonly Dictionary<EntityId, EntityResourceBucket> _entityResources = [];
    private bool _disposed;

    public Direct2DResourceCache(
        ID2D1Factory? d2D1Factory = null,
        IDWriteFactory? writeFactory = null,
        ID2D1DeviceContext? deviceContext = null)
    {
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
        ClearCache();
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

        if (changes.AffectsDocumentStructure)
        {
            RebuildAll(document);
            return;
        }

        foreach (var change in changes.EntityChanges)
        {
            const CadEntityChangeKind resourceIndependentChanges =
                CadEntityChangeKind.DrawOrder |
                CadEntityChangeKind.Metadata |
                CadEntityChangeKind.Opacity |
                CadEntityChangeKind.Rotation;
            if ((change.Kind & ~resourceIndependentChanges) == 0)
            {
                continue;
            }

            RebuildEntityResources(document, change.EntityId);
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

        RemoveEntity(entityId);

        if (!CanCreateDeviceResources())
            return;

        if (!document.TryGetEntity(entityId, out var entity) || entity is null)
            return;

        if (entity.IsErased || !entity.IsVisible)
            return;

        if (!document.TryGetLayer(entity.LayerId, out var layer) ||
            layer is null ||
            !layer.IsVisible ||
            layer.IsFrozen)
        {
            return;
        }

        var bucket = CreateEntityResources(document, entity, layer);
        if (!bucket.IsEmpty)
            _entityResources[entityId] = bucket;
        else
            bucket.Dispose();
    }

    public void RemoveEntity(EntityId entityId)
    {
        if (_entityResources.Remove(entityId, out var bucket))
            bucket.Dispose();
    }

    public void ClearCache()
    {
        ClearEntityResources();
    }

    private EntityResourceBucket CreateEntityResources(
        CadDocument document,
        CadEntity entity,
        CadLayer layer)
    {
        var bucket = new EntityResourceBucket(entity.Id);
        var graphic = ResolveGraphicStyle(document, entity, layer);

        bucket.StrokeWidth = ResolveStrokeWidth(
            entity.LineWeight,
            entity.UseLayerLineWeight,
            graphic?.LineWeight,
            layer.LineWeight);
        bucket.StrokeBrush = CreateBrush(ResolveStrokeColor(document, entity, layer, graphic));
        bucket.StrokeStyle = CreateStrokeStyle(entity.StrokeStyle);

        bucket.Geometry = CreateGeometry(entity);

        if (TryResolveFillStyle(document, entity, out var fillStyle))
        {
            switch (fillStyle)
            {
                case CadGradientFillStyle { IsSolid: true } gradient:
                    var fillColor = gradient.Stops[0].Color;
                    if (!fillColor.IsTransparent)
                        bucket.FillBrush = CreateBrush(fillColor);
                    break;

                case CadHatchFillStyle hatch when document.TryGetHatchPattern(hatch.PatternId, out var pattern) &&
                                                  pattern is not null:
                    bucket.HatchFillStyle = hatch;
                    bucket.HatchPattern = pattern;
                    if (!hatch.ForegroundColor.IsTransparent)
                        bucket.HatchBrush = CreateBrush(hatch.ForegroundColor);
                    break;
            }
        }

        if (entity is CadText text)
            bucket.TextFormat = Direct2DTextServices.CreateTextFormat(WriteFactory, document, text);

        if (entity is CadImage image)
        {
            bucket.Bitmap = CreateBitmap(image.PixelWidth, image.PixelHeight, image.Stride, image.CopyPixels());
            if (bucket.Bitmap is not null)
                bucket.BitmapBrush = CreateBitmapBrush(image.FrameBounds, image.PixelWidth, image.PixelHeight, bucket.Bitmap);
        }

        return bucket;
    }

    private ID2D1Geometry? CreateGeometry(CadEntity entity)
    {
        return entity switch
        {
            CadLine => null,
            CadCircle => null,
            CadEllipse => null,
            CadEllipseArc ellipseArc => CreateEllipseArcPathGeometry(ellipseArc),
            CadRectangle => null,
            CadArc arc => arc.IsFullCircle ? null : CreateArcPathGeometry(arc),
            CadPolyline polyline => CreatePolylineGeometry(polyline.Points, polyline.Closed),
            CadSpline spline => CreateSplineGeometry(spline.FitPoints, spline.Closed),
            CadShapeText shapeText => CreateShapeTextGeometry(shapeText),
            CadText => null,
            CadBlockReference blockReference => CreateRectangleGeometry(blockReference.Bounds),
            _ => null
        };
    }

    private ID2D1Bitmap? CreateBitmap(int pixelWidth, int pixelHeight, int stride, byte[] pixels)
    {
        if (DeviceContext is null)
            return null;

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return DeviceContext.CreateBitmap(
                new SizeI(pixelWidth, pixelHeight),
                handle.AddrOfPinnedObject(),
                (uint)stride,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        DXGIFormat.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied),
                    DpiX = 96.0f,
                    DpiY = 96.0f,
                    BitmapOptions = BitmapOptions.None
                });
        }
        finally
        {
            handle.Free();
        }
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

    private ID2D1Geometry CreateRectangleGeometry(CadRectD bounds)
    {
        return Factory!.CreateRectangleGeometry(new RawRectF(
            (float)bounds.MinX,
            (float)bounds.MinY,
            (float)bounds.MaxX,
            (float)bounds.MaxY));
    }

    private ID2D1SolidColorBrush CreateBrush(CadColor color)
    {
        return DeviceContext!.CreateSolidColorBrush(ToColor4(color));
    }

    private ID2D1StrokeStyle? CreateStrokeStyle(CadStrokeStyle strokeStyle)
    {
        if (Factory is null || strokeStyle == CadStrokeStyle.Default)
            return null;

        return Factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = ToD2DCapStyle(strokeStyle.StartCap),
            EndCap = ToD2DCapStyle(strokeStyle.EndCap),
            DashCap = ToD2DCapStyle(strokeStyle.DashCap),
            LineJoin = ToD2DLineJoin(strokeStyle.LineJoin),
            DashStyle = ToD2DDashStyle(strokeStyle.DashStyle)
        });
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
        return entity.UseLayerColor
            ? ResolveLayerStrokeColor(document, layer)
            : graphic?.StrokeColor ?? ResolveLayerStrokeColor(document, layer);
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

    private static Color4 ToColor4(CadColor color)
    {
        return new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f);
    }

    private static CapStyle ToD2DCapStyle(CadStrokeCap cap)
    {
        return cap switch
        {
            CadStrokeCap.Square => CapStyle.Square,
            CadStrokeCap.Round => CapStyle.Round,
            CadStrokeCap.Triangle => CapStyle.Triangle,
            _ => CapStyle.Flat
        };
    }

    private static DashStyle ToD2DDashStyle(CadStrokeDashStyle dashStyle)
    {
        return dashStyle switch
        {
            CadStrokeDashStyle.Dash => DashStyle.Dash,
            CadStrokeDashStyle.Dot => DashStyle.Dot,
            CadStrokeDashStyle.DashDot => DashStyle.DashDot,
            CadStrokeDashStyle.DashDotDot => DashStyle.DashDotDot,
            _ => DashStyle.Solid
        };
    }

    private static LineJoin ToD2DLineJoin(CadStrokeLineJoin lineJoin)
    {
        return lineJoin switch
        {
            CadStrokeLineJoin.Bevel => LineJoin.Bevel,
            CadStrokeLineJoin.Round => LineJoin.Round,
            CadStrokeLineJoin.MiterOrBevel => LineJoin.MiterOrBevel,
            _ => LineJoin.Miter
        };
    }

    internal sealed class EntityResourceBucket : IDisposable
    {
        public EntityId EntityId { get; }
        public ID2D1Geometry? Geometry { get; set; }
        public ID2D1Brush? StrokeBrush { get; set; }
        public ID2D1StrokeStyle? StrokeStyle { get; set; }
        public ID2D1Brush? FillBrush { get; set; }
        public ID2D1Brush? HatchBrush { get; set; }
        public CadHatchFillStyle? HatchFillStyle { get; set; }
        public CadHatchPatternDefinition? HatchPattern { get; set; }
        public IDWriteTextFormat? TextFormat { get; set; }
        public ID2D1Bitmap? Bitmap { get; set; }
        public ID2D1BitmapBrush? BitmapBrush { get; set; }
        public float StrokeWidth { get; set; }

        public bool IsEmpty =>
            Geometry is null &&
            StrokeBrush is null &&
            StrokeStyle is null &&
            FillBrush is null &&
            HatchBrush is null &&
            TextFormat is null &&
            Bitmap is null &&
            BitmapBrush is null;

        public EntityResourceBucket(EntityId entityId)
        {
            EntityId = entityId;
        }

        public void Dispose()
        {
            Geometry?.Dispose();
            StrokeBrush?.Dispose();
            StrokeStyle?.Dispose();
            FillBrush?.Dispose();
            HatchBrush?.Dispose();
            TextFormat?.Dispose();
            BitmapBrush?.Dispose();
            Bitmap?.Dispose();
            Geometry = null;
            StrokeBrush = null;
            StrokeStyle = null;
            FillBrush = null;
            HatchBrush = null;
            HatchFillStyle = null;
            HatchPattern = null;
            TextFormat = null;
            BitmapBrush = null;
            Bitmap = null;
        }
    }
}
