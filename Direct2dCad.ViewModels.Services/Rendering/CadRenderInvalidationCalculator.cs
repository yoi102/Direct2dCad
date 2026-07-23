using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal readonly struct CadRenderInvalidationCalculator(
    CadDocument document,
    CadViewport viewport,
    int targetWidth,
    int targetHeight,
    Func<CadEntity, CadTransientStyle> createEntityPreviewStyle)
{
    private const int MaxPathDirtyBounds = 16;
    private const int LargeHandleSceneAggregationThreshold = 512;
    private const double Direct2DDefaultMiterLimit = 10.0;

    public CadRenderInvalidation CreateTransientSceneInvalidation(
        CadTransientScene transientScene)
    {
        var dirtyRects = new List<CadScreenRect>();

        foreach (var item in transientScene.Items)
        {
            var itemInvalidation = CreateTransientInvalidation(item);
            if (itemInvalidation.IsFull)
                return CadRenderInvalidation.Full;

            dirtyRects.AddRange(itemInvalidation.DirtyScreenRects);
        }

        return CadRenderInvalidation.FromScreenRects(dirtyRects);
    }

    public CadRenderInvalidation CreateHandleSceneInvalidation(
        CadHandleScene handleScene,
        bool includeGripHandles = true)
    {
        if (handleScene.Items.Count > LargeHandleSceneAggregationThreshold)
            return CreateAggregatedHandleSceneInvalidation(handleScene, includeGripHandles);

        var dirtyRects = new List<CadScreenRect>();

        foreach (var item in handleScene.Items)
        {
            if (!includeGripHandles && item is CadGripHandle or CadRotationHandleGuide)
                continue;

            var itemInvalidation = CreateHandleInvalidation(item);
            if (itemInvalidation.IsFull)
                return CadRenderInvalidation.Full;

            dirtyRects.AddRange(itemInvalidation.DirtyScreenRects);
        }

        return CadRenderInvalidation.FromScreenRects(dirtyRects);
    }

    private CadRenderInvalidation CreateAggregatedHandleSceneInvalidation(
        CadHandleScene handleScene,
        bool includeGripHandles)
    {
        var worldBounds = handleScene.SelectionWorldBounds;
        var paddingPixels = 32.0;
        if (handleScene.SelectionReferences.Count > 0)
        {
            paddingPixels = Math.Max(
                paddingPixels,
                ResolveStrokeInvalidationPadding(
                    Math.Max(
                        handleScene.MaximumScreenConstantSelectionStrokeWidth,
                        handleScene.MaximumWorldSelectionStrokeWidth *
                        viewport.Zoom),
                    16.0,
                    Direct2DDefaultMiterLimit * 0.5));
        }

        foreach (var item in handleScene.NonSelectionItems)
        {
            switch (item)
            {
                case CadGripHandle grip when includeGripHandles:
                    worldBounds = worldBounds.ExpandToInclude(grip.Position);
                    paddingPixels = Math.Max(
                        paddingPixels,
                        Math.Max(grip.Style.Size, grip.Style.StrokeWidth) + 8.0);
                    break;
                case CadRotationHandleGuide guide when includeGripHandles:
                    worldBounds = worldBounds
                        .ExpandToInclude(guide.Start)
                        .ExpandToInclude(guide.End);
                    paddingPixels = Math.Max(
                        paddingPixels,
                        ResolveHandleStyleInvalidationPadding(
                            guide.Style,
                            12.0,
                            0.5));
                    break;
            }
        }

        return worldBounds.IsEmpty
            ? CadRenderInvalidation.Empty
            : CreateWorldBoundsInvalidation(worldBounds, paddingPixels);
    }

    public bool TryCaptureEntitySnapshot(
        EntityId entityId,
        out CadEntityInvalidationSnapshot snapshot)
    {
        if (!document.TryGetEntity(entityId, out var entity) || entity is null)
        {
            snapshot = default;
            return false;
        }

        var isRenderable =
            !entity.IsErased &&
            entity.IsVisible &&
            document.TryGetLayer(entity.LayerId, out var layer) &&
            layer is { IsVisible: true, IsFrozen: false };
        var style = isRenderable ? createEntityPreviewStyle(entity) : default;
        snapshot = new CadEntityInvalidationSnapshot(
            entity.Bounds,
            style.StrokeWidth,
            style.KeepStrokeWidthScreenConstant,
            style.MinimumScreenStrokeWidth,
            EntityUsesStrokeWidth(entity),
            ResolveEntityStrokeExtentMultiplier(entity),
            entity is CadBlockReference,
            style.HatchFill is not null,
            isRenderable);
        return true;
    }

    public CadRenderInvalidation CreateCurrentEntityInvalidation(
        EntityId entityId,
        CadEntityInvalidationSnapshot snapshot)
    {
        if (!snapshot.IsRenderable ||
            !document.TryGetEntity(entityId, out var entity) ||
            entity is null)
        {
            return CadRenderInvalidation.Empty;
        }

        var padding = ResolveEntityInvalidationPadding(snapshot);
        return entity switch
        {
            CadPolyline { FillStyleId: null } polyline =>
                CreatePolylinePathInvalidation(polyline, default, padding),
            CadSpline { Closed: false } spline =>
                CreateSplinePathInvalidation(spline, default, padding),
            CadSpline { FillStyleId: null } spline =>
                CreateSplinePathInvalidation(spline, default, padding),
            _ => CreateWorldBoundsInvalidation(snapshot.Bounds, padding)
        };
    }

    public CadRenderInvalidation CreateEntitySnapshotInvalidation(
        CadEntityInvalidationSnapshot snapshot)
    {
        return !snapshot.IsRenderable || snapshot.Bounds.IsEmpty
            ? CadRenderInvalidation.Empty
            : CreateWorldBoundsInvalidation(
                snapshot.Bounds,
                ResolveEntityInvalidationPadding(snapshot));
    }

    private CadRenderInvalidation CreateTransientInvalidation(CadTransientItem item)
    {
        return item switch
        {
            CadTransientGroup group => CreateTransientGroupInvalidation(group),
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
                polyline.Style,
                strokeExtentMultiplier: Direct2DDefaultMiterLimit * 0.5),
            CadTransientSpline spline => CreateTransientSplineInvalidation(spline),
            CadTransientRectangle rectangle => CreateTransientBoundsInvalidation(
                rectangle.Bounds,
                rectangle.Style),
            CadTransientImage image => CreateTransientBoundsInvalidation(
                RotateBounds(image.Bounds, image.RotationRadians),
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
                text.Style,
                strokeExtentMultiplier: Direct2DDefaultMiterLimit * 0.5),
            CadTransientEntityReference reference => CreateEntityReferenceInvalidation(reference.EntityId, reference.Offset),
            CadTransientBlockReference reference => CreateBlockReferenceInvalidation(reference),
            _ => CadRenderInvalidation.FromScreenRect(default)
        };
    }

    private CadRenderInvalidation CreateTransientGroupInvalidation(CadTransientGroup group)
    {
        var bounds = ResolveTransientGroupBounds(group);
        if (bounds.IsEmpty)
            return CadRenderInvalidation.Empty;

        var transformScale = ResolveMaximumScale(group.Transform);
        var padding = group.LocalBounds is not null
            ? ResolveTransientInvalidationPadding(group.Style, 24.0) * transformScale
            : ResolveTransientGroupPadding(group.Items, transformScale);
        return CreateWorldBoundsInvalidation(bounds, Math.Max(24.0, padding));
    }

    private CadRectD ResolveTransientGroupBounds(CadTransientGroup group)
    {
        if (group.LocalBounds is { } localBounds)
            return localBounds.Transform(group.Transform);

        var bounds = CadRectD.Empty;
        foreach (var child in group.Items)
            bounds = bounds.Union(ResolveTransientItemBounds(child));
        return bounds.Transform(group.Transform);
    }

    private CadRectD ResolveTransientItemBounds(CadTransientItem item)
    {
        return item switch
        {
            CadTransientGroup group => ResolveTransientGroupBounds(group),
            CadTransientLine line => BoundsFromPoints(line.Start, line.End),
            CadTransientCircle circle when circle.Radius > 0 =>
                CadRectD.FromCenter(circle.Center, circle.Radius * 2, circle.Radius * 2),
            CadTransientEllipse ellipse when ellipse.RadiusX > 0 && ellipse.RadiusY > 0 =>
                CadRectD.FromCenter(ellipse.Center, ellipse.RadiusX * 2, ellipse.RadiusY * 2),
            CadTransientEllipseArc ellipseArc when ellipseArc.RadiusX > 0 && ellipseArc.RadiusY > 0 =>
                CadRectD.FromCenter(ellipseArc.Center, ellipseArc.RadiusX * 2, ellipseArc.RadiusY * 2),
            CadTransientArc arc when arc.Radius > 0 =>
                CadRectD.FromCenter(arc.Center, arc.Radius * 2, arc.Radius * 2),
            CadTransientPolyline polyline => BoundsFromPoints(polyline.Points),
            CadTransientSpline spline => BoundsFromPoints(spline.FitPoints),
            CadTransientRectangle rectangle => rectangle.Bounds,
            CadTransientImage image => RotateBounds(image.Bounds, image.RotationRadians),
            CadTransientOleObject oleObject => oleObject.Bounds,
            CadTransientText text => ResolveTransientTextBounds(text),
            CadTransientShapeText text => ResolveTransientShapeTextBounds(text),
            CadTransientEntityReference reference
                when document.TryGetEntity(reference.EntityId, out var entity) && entity is not null =>
                entity.Bounds.Translate(reference.Offset),
            CadTransientBlockReference reference => ResolveTransientBlockReferenceBounds(reference),
            _ => CadRectD.Empty
        };
    }

    private CadRectD ResolveTransientBlockReferenceBounds(CadTransientBlockReference reference)
    {
        if (!document.TryGetBlock(reference.DefinitionBlockId, out var definition) || definition is null)
            return CadRectD.FromLTRB(reference.Position.X, reference.Position.Y, reference.Position.X, reference.Position.Y);

        var localBounds = document.GetBlockBounds(reference.DefinitionBlockId);
        return localBounds.IsEmpty
            ? CadRectD.FromLTRB(reference.Position.X, reference.Position.Y, reference.Position.X, reference.Position.Y)
            : CadBlockTransform.TransformBounds(
                definition,
                reference.Position,
                reference.RotationRadians,
                reference.ScaleX,
                reference.ScaleY,
                localBounds);
    }

    private double ResolveTransientGroupPadding(
        IReadOnlyList<CadTransientItem> items,
        double accumulatedScale)
    {
        var maximum = 0.0;
        foreach (var item in items)
        {
            if (item is CadTransientGroup group)
            {
                maximum = Math.Max(
                    maximum,
                    ResolveTransientGroupPadding(
                        group.Items,
                        accumulatedScale * ResolveMaximumScale(group.Transform)));
                continue;
            }

            var minimumPadding = item is CadTransientImage or CadTransientOleObject ? 4.0 : 12.0;
            maximum = Math.Max(
                maximum,
                ResolveTransientInvalidationPadding(item.Style, minimumPadding) * accumulatedScale);
        }

        return maximum;
    }

    private static double ResolveMaximumScale(CadMatrixD transform)
    {
        var scaleX = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        var scaleY = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        var scale = Math.Max(scaleX, scaleY);
        return double.IsFinite(scale) ? Math.Max(scale, 1.0) : 1.0;
    }

    private CadRenderInvalidation CreateBlockReferenceInvalidation(CadTransientBlockReference reference)
    {
        if (!document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null)
        {
            return CadRenderInvalidation.FromScreenRect(default);
        }

        var localBounds = document.GetBlockBounds(reference.DefinitionBlockId);
        if (localBounds.IsEmpty)
            return CreateScreenPointInvalidation(viewport.WorldToScreen(reference.Position), 12.0);

        var worldBounds = CadBlockTransform.TransformBounds(
            definition,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY,
            localBounds);
        return CreateTransientBoundsInvalidation(
            worldBounds,
            reference.Style,
            minimumPaddingPixels: 24.0);
    }

    private CadRenderInvalidation CreateTransientBoundsInvalidation(
        CadRectD bounds,
        CadTransientStyle style,
        double minimumPaddingPixels = 12.0,
        double strokeExtentMultiplier = 0.5)
    {
        return CreateWorldBoundsInvalidation(
            bounds,
            ResolveTransientInvalidationPadding(
                style,
                minimumPaddingPixels,
                strokeExtentMultiplier));
    }

    private CadRenderInvalidation CreateTransientSplineInvalidation(CadTransientSpline spline)
    {
        var padding = ResolveTransientInvalidationPadding(
            spline.Style,
            16.0,
            Direct2DDefaultMiterLimit * 0.5);
        return CreateSplinePathInvalidation(spline.FitPoints, spline.Closed, CadVectorD.Zero, padding);
    }

    private double ResolveTransientInvalidationPadding(
        CadTransientStyle style,
        double minimumPaddingPixels,
        double strokeExtentMultiplier = 0.5)
    {
        var strokeScreenWidth = ResolveStyleScreenStrokeWidth(style);
        return ResolveStrokeInvalidationPadding(
            strokeScreenWidth,
            minimumPaddingPixels,
            strokeExtentMultiplier,
            style.LinePattern != CadTransientLinePattern.Solid);
    }

    private static double ResolveStrokeInvalidationPadding(
        double strokeScreenWidth,
        double minimumPaddingPixels,
        double strokeExtentMultiplier,
        bool isPatterned = false)
    {
        var strokePadding =
            Math.Max(0.0, strokeScreenWidth) *
            Math.Max(0.5, strokeExtentMultiplier) +
            8.0;
        if (isPatterned)
            strokePadding += Math.Max(6.0, Math.Max(0.0, strokeScreenWidth));

        return Math.Max(minimumPaddingPixels, Math.Ceiling(strokePadding));
    }

    private CadRenderInvalidation CreateHandleInvalidation(CadHandleItem item)
    {
        return item switch
        {
            CadSelectionEntityReference reference =>
                CreateSelectionReferenceInvalidation(reference),
            CadGripHandle grip => CreateScreenPointInvalidation(
                viewport.WorldToScreen(grip.Position),
                Math.Max(grip.Style.Size, grip.Style.StrokeWidth) + 4.0),
            CadRotationHandleGuide guide => CreateTransientBoundsInvalidation(
                BoundsFromPoints(guide.Start, guide.End),
                new CadTransientStyle(
                    guide.Style.StrokeColor,
                    guide.Style.StrokeWidth,
                    KeepStrokeWidthScreenConstant: guide.Style.KeepSizeScreenConstant)),
            _ => CadRenderInvalidation.FromScreenRect(default)
        };
    }

    private CadRenderInvalidation CreateSelectionReferenceInvalidation(
        CadSelectionEntityReference reference)
    {
        var minimumPadding = document.TryGetEntity(reference.EntityId, out var entity) &&
                             entity is CadBlockReference
            ? 64.0
            : 16.0;
        return CreateWorldBoundsInvalidation(
            reference.EntityBounds.Translate(reference.Offset),
            ResolveHandleStyleInvalidationPadding(
                reference.Style,
                minimumPadding,
                Direct2DDefaultMiterLimit * 0.5));
    }

    private double ResolveHandleStyleInvalidationPadding(
        CadHandleStyle style,
        double minimumPaddingPixels,
        double strokeExtentMultiplier)
    {
        var screenStrokeWidth = style.KeepSizeScreenConstant
            ? style.StrokeWidth
            : style.StrokeWidth * viewport.Zoom;
        return ResolveStrokeInvalidationPadding(
            screenStrokeWidth,
            minimumPaddingPixels,
            strokeExtentMultiplier);
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
        var minimumPadding = entity is CadBlockReference
            ? 64.0
            : style.HatchFill is null ? 8.0 : 16.0;
        return ResolveTransientInvalidationPadding(
            style,
            minimumPadding,
            ResolveEntityStrokeExtentMultiplier(entity));
    }

    private double ResolveEntityInvalidationPadding(
        CadEntityInvalidationSnapshot snapshot)
    {
        var minimumPadding = snapshot.IsBlockReference
            ? 64.0
            : snapshot.HasHatchFill ? 16.0 : 8.0;
        return snapshot.UsesStrokeWidth
            ? ResolveStrokeInvalidationPadding(
                ResolveSnapshotScreenStrokeWidth(snapshot),
                minimumPadding,
                snapshot.StrokeExtentMultiplier)
            : minimumPadding;
    }

    private double ResolveStyleWorldStrokeWidth(CadTransientStyle style)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        return ResolveStyleScreenStrokeWidth(style) / zoom;
    }

    private double ResolveStyleScreenStrokeWidth(CadTransientStyle style)
    {
        var strokeScreenWidth = style.KeepStrokeWidthScreenConstant
            ? style.StrokeWidth
            : style.StrokeWidth * viewport.Zoom;
        return Math.Max(
            strokeScreenWidth,
            Math.Max(style.MinimumScreenStrokeWidth, 0.0));
    }

    private double ResolveSnapshotScreenStrokeWidth(
        CadEntityInvalidationSnapshot snapshot)
    {
        var strokeScreenWidth = snapshot.KeepStrokeWidthScreenConstant
            ? snapshot.StrokeWidth
            : snapshot.StrokeWidth * viewport.Zoom;
        return Math.Max(
            strokeScreenWidth,
            Math.Max(snapshot.MinimumScreenStrokeWidth, 0.0));
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

    private static double ResolveEntityStrokeExtentMultiplier(CadEntity entity)
    {
        var multiplier = 0.5;
        if (entity is CadShapeText ||
            CadEntityCapabilities.SupportsLineJoin(entity) &&
            entity.StrokeStyle.LineJoin is
                CadStrokeLineJoin.Miter or CadStrokeLineJoin.MiterOrBevel)
        {
            multiplier = Direct2DDefaultMiterLimit * 0.5;
        }

        if (CadEntityCapabilities.SupportsStartEndCaps(entity) &&
            (entity.StrokeStyle.StartCap == CadStrokeCap.Triangle ||
             entity.StrokeStyle.EndCap == CadStrokeCap.Triangle ||
             entity.StrokeStyle.DashCap == CadStrokeCap.Triangle))
        {
            multiplier = Math.Max(multiplier, 1.0);
        }

        return multiplier;
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

    private static CadRectD RotateBounds(CadRectD bounds, double rotationRadians)
    {
        if (bounds.IsEmpty || Math.Abs(rotationRadians) <= 1e-12)
            return bounds;

        var center = bounds.Center;
        var cos = Math.Cos(rotationRadians);
        var sin = Math.Sin(rotationRadians);
        var result = CadRectD.Empty;
        foreach (var point in new[]
                 {
                     new CadPointD(bounds.MinX, bounds.MinY),
                     new CadPointD(bounds.MaxX, bounds.MinY),
                     new CadPointD(bounds.MaxX, bounds.MaxY),
                     new CadPointD(bounds.MinX, bounds.MaxY)
                 })
        {
            var dx = point.X - center.X;
            var dy = point.Y - center.Y;
            result = result.ExpandToInclude(new CadPointD(
                center.X + dx * cos - dy * sin,
                center.Y + dx * sin + dy * cos));
        }

        return result;
    }
}

internal readonly record struct CadEntityInvalidationSnapshot(
    CadRectD Bounds,
    double StrokeWidth,
    bool KeepStrokeWidthScreenConstant,
    double MinimumScreenStrokeWidth,
    bool UsesStrokeWidth,
    double StrokeExtentMultiplier,
    bool IsBlockReference,
    bool HasHatchFill,
    bool IsRenderable);
