using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DResourceCache : IDisposable
{
    private const double ArcSegmentMaxAngle = Math.PI / 18.0;
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
            if (change.Kind == CadEntityChangeKind.DrawOrder)
                continue;

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

        bucket.StrokeWidth = ResolveStrokeWidth(entity.LineWeight, graphic?.LineWeight, layer.LineWeight);
        bucket.StrokeBrush = CreateBrush(graphic?.StrokeColor ?? layer.Color);

        bucket.Geometry = CreateGeometry(entity);

        if (TryResolveFillColor(document, entity, out var fillColor))
            bucket.FillBrush = CreateBrush(fillColor);

        if (entity is CadText text)
            bucket.TextFormat = CreateTextFormat(document, text);

        return bucket;
    }

    private ID2D1Geometry? CreateGeometry(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => CreateLineGeometry(line.Start, line.End),
            CadCircle circle => Factory!.CreateEllipseGeometry(
                new Ellipse(ToVector2(circle.Center), (float)circle.Radius, (float)circle.Radius)),
            CadArc arc => arc.IsFullCircle
                ? Factory!.CreateEllipseGeometry(new Ellipse(ToVector2(arc.Center), (float)arc.Radius, (float)arc.Radius))
                : CreateArcPathGeometry(arc),
            CadPolyline polyline => CreatePolylineGeometry(polyline.Points, polyline.Closed),
            CadText => null,
            CadBlockReference blockReference => CreateRectangleGeometry(blockReference.Bounds),
            _ => null
        };
    }

    private ID2D1PathGeometry CreateLineGeometry(CadPointD start, CadPointD end)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(start), FigureBegin.Hollow);
        sink.AddLine(ToVector2(end));
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
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

    private ID2D1PathGeometry CreateArcPathGeometry(CadArc arc)
    {
        var geometry = Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(arc.StartPoint), FigureBegin.Hollow);

        var segmentCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.SweepAngleRadians) / ArcSegmentMaxAngle));
        for (var i = 1; i <= segmentCount; i++)
        {
            var angle = arc.StartAngleRadians + arc.SweepAngleRadians * i / segmentCount;
            sink.AddLine(ToVector2(arc.GetPointAtAngle(angle)));
        }

        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private ID2D1RectangleGeometry CreateRectangleGeometry(CadRectD bounds)
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

    private IDWriteTextFormat? CreateTextFormat(CadDocument document, CadText text)
    {
        if (WriteFactory is null)
            return null;

        var fontFamily = "Meiryo";
        var fontSize = (float)(text.Height * CadText.FontSizeScale);
        var fontWeight = FontWeight.Normal;
        var fontStyle = FontStyle.Normal;

        if (text.TextStyleId is not null &&
            document.TryGetStyle(text.TextStyleId.Value, out var style) &&
            style is CadTextStyle textStyle)
        {
            fontFamily = textStyle.FontFamily;
            fontWeight = textStyle.IsBold ? FontWeight.Bold : FontWeight.Normal;
            fontStyle = textStyle.IsItalic ? FontStyle.Italic : FontStyle.Normal;
        }

        var format = WriteFactory.CreateTextFormat(
            fontFamily,
            null,
            fontWeight,
            fontStyle,
            FontStretch.Normal,
            fontSize,
            "ja-JP");

        format.TextAlignment = TextAlignment.Leading;
        format.ParagraphAlignment = ParagraphAlignment.Near;
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
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
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static float ResolveStrokeWidth(
        CadLineWeight? entityWeight,
        CadLineWeight? styleWeight,
        CadLineWeight layerWeight)
    {
        var weight = entityWeight is { IsByLayer: false }
            ? entityWeight.Value
            : styleWeight is { IsByLayer: false }
            ? styleWeight.Value
            : layerWeight;

        if (weight.IsByLayer || weight.Value <= 0)
            weight = CadLineWeight.Default;

        return (float)Math.Max(weight.Value, 0.01);
    }

    private static bool TryResolveFillColor(CadDocument document, CadEntity entity, out CadColor color)
    {
        var fillStyleId = entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            _ => null
        };

        if (fillStyleId is null ||
            !document.TryGetStyle(fillStyleId.Value, out var style) ||
            style is not CadGradientFillStyle { IsSolid: true } fillStyle)
        {
            color = default;
            return false;
        }

        color = fillStyle.Stops[0].Color;
        return !color.IsTransparent;
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

    internal sealed class EntityResourceBucket : IDisposable
    {
        public EntityId EntityId { get; }
        public ID2D1Geometry? Geometry { get; set; }
        public ID2D1Brush? StrokeBrush { get; set; }
        public ID2D1Brush? FillBrush { get; set; }
        public IDWriteTextFormat? TextFormat { get; set; }
        public float StrokeWidth { get; set; }

        public bool IsEmpty =>
            Geometry is null &&
            StrokeBrush is null &&
            FillBrush is null &&
            TextFormat is null;

        public EntityResourceBucket(EntityId entityId)
        {
            EntityId = entityId;
        }

        public void Dispose()
        {
            Geometry?.Dispose();
            StrokeBrush?.Dispose();
            FillBrush?.Dispose();
            TextFormat?.Dispose();
            Geometry = null;
            StrokeBrush = null;
            FillBrush = null;
            TextFormat = null;
        }
    }
}
