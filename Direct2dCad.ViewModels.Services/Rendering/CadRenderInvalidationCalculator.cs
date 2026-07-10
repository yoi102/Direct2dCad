using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadRenderInvalidationCalculator(
    CadDocument document,
    CadViewport viewport,
    int targetWidth,
    int targetHeight,
    Func<CadEntity, CadTransientStyle> createEntityPreviewStyle)
{
    private const int MaxPathDirtyBounds = 16;

    public CadRenderInvalidation CreateOverlayInvalidation(
        CadTransientScene transientScene,
        CadHandleScene handleScene,
        bool includeGripHandles = true)
    {
        var invalidation = CadRenderInvalidation.FromScreenRect(default);

        foreach (var item in transientScene.Items)
            invalidation = invalidation.Union(CreateTransientInvalidation(item));

        foreach (var item in handleScene.Items)
        {
            if (!includeGripHandles && item is CadGripHandle)
                continue;

            invalidation = invalidation.Union(CreateHandleInvalidation(item));
        }

        return invalidation;
    }

    public CadRenderInvalidation CreateDocumentInvalidation(CadDocumentChangeSet changes)
    {
        if (changes.AffectsDocumentStructure || changes.AffectsViewSettings)
            return CadRenderInvalidation.Full;

        var bounds = CadRectD.Empty;
        foreach (var change in changes.EntityChanges)
        {
            if (change.Kind == CadEntityChangeKind.Metadata)
                continue;

            if (RequiresFullRender(change))
                return CadRenderInvalidation.Full;

            if (!document.TryGetEntity(change.EntityId, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !entity.IsVisible)
            {
                return CadRenderInvalidation.Full;
            }

            bounds = bounds.Union(ResolveEntityPaintBounds(entity));
        }

        return bounds.IsEmpty
            ? CadRenderInvalidation.Empty
            : CreateWorldBoundsInvalidation(bounds, ResolveDocumentInvalidationPadding(changes));
    }

    private CadRenderInvalidation CreateTransientInvalidation(CadTransientItem item)
    {
        return item switch
        {
            CadTransientLine line => CreateTransientBoundsInvalidation(
                BoundsFromPoints(line.Start, line.End),
                line.Style),
            CadTransientCircle circle when circle.Radius > 0 => CreateTransientBoundsInvalidation(
                CadRectD.FromCenter(circle.Center, circle.Radius * 2, circle.Radius * 2),
                circle.Style,
                minimumPaddingPixels: 24.0),
            CadTransientEllipse ellipse when ellipse.RadiusX > 0 && ellipse.RadiusY > 0 => CreateTransientBoundsInvalidation(
                CadRectD.FromCenter(ellipse.Center, ellipse.RadiusX * 2, ellipse.RadiusY * 2),
                ellipse.Style,
                minimumPaddingPixels: 24.0),
            CadTransientEllipseArc ellipseArc when ellipseArc.RadiusX > 0 && ellipseArc.RadiusY > 0 => CreateTransientBoundsInvalidation(
                CadRectD.FromCenter(ellipseArc.Center, ellipseArc.RadiusX * 2, ellipseArc.RadiusY * 2),
                ellipseArc.Style,
                minimumPaddingPixels: 24.0),
            CadTransientArc arc when arc.Radius > 0 => CreateTransientBoundsInvalidation(
                CadRectD.FromCenter(arc.Center, arc.Radius * 2, arc.Radius * 2),
                arc.Style,
                minimumPaddingPixels: 24.0),
            CadTransientPolyline polyline => CreateTransientBoundsInvalidation(
                BoundsFromPoints(polyline.Points),
                polyline.Style),
            CadTransientSpline spline => CreateTransientSplineInvalidation(spline),
            CadTransientRectangle rectangle => CreateTransientBoundsInvalidation(
                rectangle.Bounds,
                rectangle.Style),
            CadTransientImage image => CreateTransientBoundsInvalidation(
                image.Bounds,
                image.Style,
                minimumPaddingPixels: 4.0),
            CadTransientOleObject oleObject => CreateTransientBoundsInvalidation(
                oleObject.Bounds,
                oleObject.Style,
                minimumPaddingPixels: 4.0),
            CadTransientText text => CreateTransientBoundsInvalidation(
                ResolveTransientTextBounds(text),
                text.Style),
            CadTransientShapeText text => CreateTransientBoundsInvalidation(
                ResolveTransientShapeTextBounds(text),
                text.Style),
            CadTransientEntityReference reference => CreateEntityReferenceInvalidation(reference.EntityId, reference.Offset),
            _ => CadRenderInvalidation.FromScreenRect(default)
        };
    }

    private CadRenderInvalidation CreateTransientBoundsInvalidation(
        CadRectD bounds,
        CadTransientStyle style,
        double minimumPaddingPixels = 12.0)
    {
        return CreateWorldBoundsInvalidation(bounds, ResolveTransientInvalidationPadding(style, minimumPaddingPixels));
    }

    private CadRenderInvalidation CreateTransientSplineInvalidation(CadTransientSpline spline)
    {
        var padding = ResolveTransientInvalidationPadding(spline.Style, 16.0);
        return CreateSplinePathInvalidation(spline.FitPoints, spline.Closed, CadVectorD.Zero, padding);
    }

    private double ResolveTransientInvalidationPadding(CadTransientStyle style, double minimumPaddingPixels)
    {
        var strokeScreenWidth = style.KeepStrokeWidthScreenConstant
            ? style.StrokeWidth
            : style.StrokeWidth * viewport.Zoom;
        strokeScreenWidth = Math.Max(strokeScreenWidth, Math.Max(style.MinimumScreenStrokeWidth, 0.0));

        var strokePadding = Math.Max(0.0, strokeScreenWidth) * 0.5 + 8.0;
        if (style.LinePattern != CadTransientLinePattern.Solid)
            strokePadding += Math.Max(6.0, Math.Max(0.0, strokeScreenWidth));

        return Math.Max(minimumPaddingPixels, Math.Ceiling(strokePadding));
    }

    private CadRenderInvalidation CreateHandleInvalidation(CadHandleItem item)
    {
        return item switch
        {
            CadSelectionEntityReference reference => CreateEntityReferenceInvalidation(reference.EntityId, reference.Offset),
            CadGripHandle grip => CreateScreenPointInvalidation(
                viewport.WorldToScreen(grip.Position),
                Math.Max(grip.Style.Size, grip.Style.StrokeWidth) + 4.0),
            _ => CadRenderInvalidation.FromScreenRect(default)
        };
    }

    private CadRenderInvalidation CreateEntityReferenceInvalidation(EntityId entityId, CadVectorD offset)
    {
        return document.TryGetEntity(entityId, out var entity) && entity is not null
            ? CreateEntityBoundsInvalidation(entity, offset)
            : CadRenderInvalidation.FromScreenRect(default);
    }

    private CadRenderInvalidation CreateEntityBoundsInvalidation(CadEntity entity, CadVectorD offset = default)
    {
        var padding = ResolveEntityInvalidationPadding(entity);
        return entity switch
        {
            CadPolyline { FillStyleId: null } polyline => CreatePolylinePathInvalidation(polyline, offset, padding),
            CadSpline { Closed: false } spline => CreateSplinePathInvalidation(spline, offset, padding),
            CadSpline { FillStyleId: null } spline => CreateSplinePathInvalidation(spline, offset, padding),
            _ => CreateWorldBoundsInvalidation(ResolveEntityPaintBounds(entity).Translate(offset), padding)
        };
    }

    private CadRenderInvalidation CreatePolylinePathInvalidation(
        CadPolyline polyline,
        CadVectorD offset,
        double paddingPixels)
    {
        var points = polyline.Points;
        if (points.Count < 2)
            return CadRenderInvalidation.FromScreenRect(default);

        var bounds = new List<CadRectD>(points.Count);
        for (var i = 1; i < points.Count; i++)
            bounds.Add(BoundsFromPoints(points[i - 1] + offset, points[i] + offset));

        if (polyline.Closed && points.Count > 2)
            bounds.Add(BoundsFromPoints(points[^1] + offset, points[0] + offset));

        return CreateChunkedPathInvalidation(bounds, paddingPixels);
    }

    private CadRenderInvalidation CreateSplinePathInvalidation(
        CadSpline spline,
        CadVectorD offset,
        double paddingPixels)
    {
        return CreateSplinePathInvalidation(spline.FitPoints, spline.Closed, offset, paddingPixels);
    }

    private CadRenderInvalidation CreateSplinePathInvalidation(
        IReadOnlyList<CadPointD> fitPoints,
        bool closed,
        CadVectorD offset,
        double paddingPixels)
    {
        var segments = CadSpline.CreateBezierSegments(fitPoints, closed);
        if (segments.Count == 0)
            return CreateWorldBoundsInvalidation(BoundsFromPoints(fitPoints).Translate(offset), paddingPixels);

        var bounds = new List<CadRectD>(segments.Count);
        foreach (var segment in segments)
        {
            bounds.Add(CadRectD.Empty
                .ExpandToInclude(segment.Start + offset)
                .ExpandToInclude(segment.Control1 + offset)
                .ExpandToInclude(segment.Control2 + offset)
                .ExpandToInclude(segment.End + offset));
        }

        return CreateChunkedPathInvalidation(bounds, paddingPixels);
    }

    private CadRenderInvalidation CreateChunkedPathInvalidation(
        IReadOnlyList<CadRectD> bounds,
        double paddingPixels)
    {
        if (bounds.Count <= MaxPathDirtyBounds)
            return CreateWorldBoundsInvalidation(bounds, paddingPixels);

        var chunked = new List<CadRectD>(MaxPathDirtyBounds);
        for (var chunk = 0; chunk < MaxPathDirtyBounds; chunk++)
        {
            var start = chunk * bounds.Count / MaxPathDirtyBounds;
            var end = (chunk + 1) * bounds.Count / MaxPathDirtyBounds;
            var aggregate = CadRectD.Empty;

            for (var i = start; i < end; i++)
                aggregate = aggregate.Union(bounds[i]);

            if (!aggregate.IsEmpty)
                chunked.Add(aggregate);
        }

        return CreateWorldBoundsInvalidation(chunked, paddingPixels);
    }

    private CadRectD ResolveEntityPaintBounds(CadEntity entity)
    {
        var bounds = entity.Bounds;
        if (bounds.IsEmpty || !EntityUsesStrokeWidth(entity))
            return bounds;

        var style = createEntityPreviewStyle(entity);
        var strokeWidth = ResolveStyleWorldStrokeWidth(style);
        return strokeWidth > 0 ? bounds.Inflate(strokeWidth * 0.5) : bounds;
    }

    private double ResolveEntityInvalidationPadding(CadEntity entity)
    {
        var style = createEntityPreviewStyle(entity);
        return ResolveTransientInvalidationPadding(style, style.HatchFill is null ? 8.0 : 16.0);
    }

    private double ResolveStyleWorldStrokeWidth(CadTransientStyle style)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var screenStrokeWidth = style.KeepStrokeWidthScreenConstant
            ? style.StrokeWidth
            : style.StrokeWidth * zoom;
        screenStrokeWidth = Math.Max(screenStrokeWidth, Math.Max(style.MinimumScreenStrokeWidth, 0.0));

        return screenStrokeWidth / zoom;
    }

    private static bool EntityUsesStrokeWidth(CadEntity entity)
    {
        return entity is CadLine or
            CadCircle or
            CadEllipse or
            CadEllipseArc or
            CadRectangle or
            CadArc or
            CadPolyline or
            CadSpline or
            CadShapeText or
            CadBlockReference;
    }

    private CadRenderInvalidation CreateWorldBoundsInvalidation(CadRectD bounds, double paddingPixels = 8.0)
    {
        return CadRenderInvalidation.FromWorldBounds(
            viewport,
            bounds,
            targetWidth,
            targetHeight,
            paddingPixels);
    }

    private CadRenderInvalidation CreateWorldBoundsInvalidation(
        IEnumerable<CadRectD> bounds,
        double paddingPixels = 8.0)
    {
        var rects = new List<CadScreenRect>();
        foreach (var item in bounds)
        {
            if (item.IsEmpty)
                continue;

            var invalidation = CreateWorldBoundsInvalidation(item, paddingPixels);
            rects.AddRange(invalidation.DirtyScreenRects);
        }

        return CadRenderInvalidation.FromScreenRects(rects);
    }

    private static CadRenderInvalidation CreateScreenPointInvalidation(CadPointD screenPoint, double radiusPixels)
    {
        var radius = Math.Max(1.0, radiusPixels);
        return CadRenderInvalidation.FromScreenRect(new CadScreenRect(
            Math.Max(0, (int)Math.Floor(screenPoint.X - radius)),
            Math.Max(0, (int)Math.Floor(screenPoint.Y - radius)),
            (int)Math.Ceiling(radius * 2),
            (int)Math.Ceiling(radius * 2)));
    }

    private static CadRectD ResolveTransientTextBounds(CadTransientText text)
    {
        return text.IsInverted
            ? text.Bounds.Inflate(text.Height * Math.Max(0, text.InvertedMarginFactor))
            : text.Bounds;
    }

    private static CadRectD ResolveTransientShapeTextBounds(CadTransientShapeText text)
    {
        var bounds = CadShapeFontMetrics.MeasureBounds(
            text.Text,
            text.Position,
            text.Height,
            text.WidthFactor,
            text.CharacterSpacingFactor,
            text.ObliqueAngleRadians,
            text.RotationRadians,
            text.ShapeFontId);

        return text.IsInverted
            ? bounds.Inflate(text.Height * Math.Max(0, text.InvertedMarginFactor))
            : bounds;
    }

    private static CadRectD BoundsFromPoints(CadPointD first, CadPointD second)
    {
        return CadRectD.Empty
            .ExpandToInclude(first)
            .ExpandToInclude(second);
    }

    private static CadRectD BoundsFromPoints(IEnumerable<CadPointD> points)
    {
        var bounds = CadRectD.Empty;
        foreach (var point in points)
            bounds = bounds.ExpandToInclude(point);

        return bounds;
    }

    private static bool RequiresFullRender(CadEntityChange change)
    {
        var kind = change.Kind;
        if (kind.HasFlag(CadEntityChangeKind.Deleted) ||
            kind.HasFlag(CadEntityChangeKind.DrawOrder) ||
            kind.HasFlag(CadEntityChangeKind.Layer))
        {
            return true;
        }

        if (kind.HasFlag(CadEntityChangeKind.Geometry) &&
            !kind.HasFlag(CadEntityChangeKind.Created))
        {
            return true;
        }

        if (kind.HasFlag(CadEntityChangeKind.Appearance) &&
            !kind.HasFlag(CadEntityChangeKind.Fill) &&
            !kind.HasFlag(CadEntityChangeKind.Created))
        {
            return true;
        }

        return kind.HasFlag(CadEntityChangeKind.Visibility) &&
               !kind.HasFlag(CadEntityChangeKind.Created);
    }

    private double ResolveDocumentInvalidationPadding(CadDocumentChangeSet changes)
    {
        var padding = 8.0;
        foreach (var change in changes.EntityChanges)
        {
            if (!document.TryGetEntity(change.EntityId, out var entity) || entity is null)
                continue;

            padding = Math.Max(padding, ResolveEntityInvalidationPadding(entity));
        }

        return padding;
    }
}
