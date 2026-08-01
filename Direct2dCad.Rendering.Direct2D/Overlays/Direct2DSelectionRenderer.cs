using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Direct2D.Transient;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Overlays;

internal sealed class Direct2DSelectionRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DTransientRenderer transientRenderer,
    Direct2DStyleResourceCache styleResources,
    Direct2DHandleRenderer handleRenderer,
    Direct2DEntityOrderCache entityOrderCache,
    Direct2DRenderStatisticsCollector statistics)
{
    private const byte SelectedSolidFillMaximumAlpha = 64;
    private readonly HashSet<BlockId> _visitedBlocks = [];

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene? scene,
        CadRenderOptions options)
    {
        if (scene is not null)
            DrawNonSelectionItems(context, viewport, scene.NonSelectionItems, options);
    }

    internal void DrawInlineSelectionReference(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference,
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        var renderWorldBounds = options.DirtyWorldBounds is { IsEmpty: false } dirty
            ? dirty
            : viewport.VisibleWorldBounds;
        DrawSelectionReference(
            context,
            document,
            viewport,
            reference,
            renderWorldBounds,
            options,
            _visitedBlocks);
    }

    private void DrawNonSelectionItems(
        ID2D1DeviceContext context,
        CadViewport viewport,
        IReadOnlyList<CadHandleItem> items,
        CadRenderOptions options)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case CadGripHandle grip when options.DrawGripHandles && IsGripVisible(viewport, grip):
                    handleRenderer.DrawGrip(context, resourceCache.Factory, viewport, grip);
                    break;
                case CadRotationHandleGuide guide when options.DrawGripHandles:
                    transientRenderer.DrawLine(
                        context,
                        viewport,
                        guide.Start,
                        guide.End,
                        ToTransientStyle(guide.Style));
                    break;
            }
        }
    }

    private void DrawSelectionReference(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference,
        CadRectD? dirtyWorldBounds,
        CadRenderOptions options,
        HashSet<BlockId> visitedBlocks)
    {
        if (!IntersectsRenderBounds(
                reference.EntityBounds,
                reference.Offset,
                viewport,
                dirtyWorldBounds,
                reference.Style))
        {
            return;
        }

        if (!document.TryGetEntity(reference.EntityId, out var entity) ||
            entity is null ||
            entity.IsErased ||
            !entity.IsVisible ||
            !document.TryGetLayer(entity.LayerId, out var layer) ||
            layer is not { IsVisible: true, IsFrozen: false })
            return;

        DrawSelectionEntity(
            context,
            document,
            viewport,
            entity,
            reference.Offset,
            reference.Style,
            dirtyWorldBounds,
            options,
            visitedBlocks);
    }


    private void DrawSelectionEntity(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadEntity entity,
        CadVectorD offset,
        CadHandleStyle selectionStyle,
        CadRectD? dirtyWorldBounds,
        CadRenderOptions options,
        HashSet<BlockId> visitedBlocks)
    {
        if (!IntersectsRenderBounds(
                entity,
                offset,
                viewport,
                dirtyWorldBounds,
                selectionStyle))
            return;

        statistics.RecordSelectionEntity();

        var detail = Direct2DEntityLevelOfDetail.ResolveSelection(
            entity,
            context.Transform,
            options);
        if (detail == Direct2DEntityRenderDetail.Skip)
            return;
        if (detail == Direct2DEntityRenderDetail.Simplified)
        {
            var brush = styleResources.GetBrush(context, selectionStyle.StrokeColor);
            var bounds = entity.Bounds.Translate(offset);
            Direct2DEntityRenderer.DrawRectangularProxy(context, bounds, brush);
            return;
        }

        if (entity is CadBlockReference blockReference)
        {
            DrawBlockReferenceSelection(
                context,
                document,
                viewport,
                blockReference,
                selectionStyle,
                options,
                visitedBlocks);
            return;
        }

        resourceCache.TryGetEntityResources(entity.Id, out var resources);
        var style = ToTransientStyle(selectionStyle, resources);
        if (TryDrawCachedGeometry(
                context,
                entity,
                resources,
                viewport,
                offset,
                style,
                options))
            return;

        switch (entity)
        {
            case CadLine line:
                transientRenderer.DrawLine(context, viewport, line.Start + offset, line.End + offset, style);
                break;
            case CadCircle circle:
                transientRenderer.DrawCircle(
                    context,
                    viewport,
                    circle.Center + offset,
                    circle.Radius,
                    style,
                    options.IsLevelOfDetailEnabled);
                break;
            case CadEllipse ellipse:
                transientRenderer.DrawEllipse(
                    context,
                    viewport,
                    ellipse.Center + offset,
                    ellipse.RadiusX,
                    ellipse.RadiusY,
                    style,
                    options.IsLevelOfDetailEnabled);
                break;
            case CadEllipseArc arc:
                transientRenderer.DrawEllipseArc(
                    context,
                    viewport,
                    arc.Center + offset,
                    arc.RadiusX,
                    arc.RadiusY,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style);
                break;
            case CadRectangle rectangle:
                transientRenderer.DrawRectangle(
                    context,
                    viewport,
                    rectangle.Bounds.Translate(offset),
                    style,
                    rectangle.CornerRadiusX,
                    rectangle.CornerRadiusY,
                    options.IsLevelOfDetailEnabled);
                break;
            case CadArc arc:
                transientRenderer.DrawArc(
                    context,
                    viewport,
                    arc.Center + offset,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style);
                break;
            case CadPolyline polyline:
                DrawTranslatedPolyline(
                    context,
                    viewport,
                    polyline,
                    offset,
                    style,
                    options.IsLevelOfDetailEnabled);
                break;
            case CadSpline spline:
                DrawTranslatedSpline(
                    context,
                    viewport,
                    spline,
                    offset,
                    style,
                    options.IsLevelOfDetailEnabled);
                break;
            case CadCompositePath path:
                DrawTranslatedCompositePath(
                    context,
                    viewport,
                    path,
                    offset,
                    style,
                    options.IsLevelOfDetailEnabled);
                break;
            case CadShapeText text:
                transientRenderer.DrawShapeText(
                    context,
                    viewport,
                    text.Text,
                    text.Position + offset,
                    text.Height,
                    text.RotationRadians,
                    text.WidthFactor,
                    text.CharacterSpacingFactor,
                    text.ObliqueAngleRadians,
                    style,
                    shapeFontId: text.ShapeFontId);
                break;
            case CadImage image:
                DrawImageFrame(context, viewport, image, offset, style);
                break;
            default:
                transientRenderer.DrawRectangle(
                    context,
                    viewport,
                    entity.Bounds.Translate(offset),
                    style,
                    isLevelOfDetailEnabled: options.IsLevelOfDetailEnabled);
                break;
        }
    }

    private void DrawTranslatedPolyline(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadPolyline polyline,
        CadVectorD offset,
        CadTransientStyle style,
        bool isLevelOfDetailEnabled)
    {
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)offset.X,
            (float)offset.Y) * previousTransform;
        try
        {
            transientRenderer.DrawPolyline(
                context,
                viewport,
                polyline.Points,
                polyline.Closed,
                style,
                isLevelOfDetailEnabled);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private void DrawTranslatedSpline(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadSpline spline,
        CadVectorD offset,
        CadTransientStyle style,
        bool isLevelOfDetailEnabled)
    {
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)offset.X,
            (float)offset.Y) * previousTransform;
        try
        {
            transientRenderer.DrawSpline(
                context,
                viewport,
                spline.FitPoints,
                spline.Closed,
                style,
                isLevelOfDetailEnabled);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private void DrawTranslatedCompositePath(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadCompositePath path,
        CadVectorD offset,
        CadTransientStyle style,
        bool isLevelOfDetailEnabled)
    {
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)offset.X,
            (float)offset.Y) * previousTransform;
        try
        {
            transientRenderer.DrawPolyline(
                context,
                viewport,
                path.EnumerateFlattenedPoints(96, 24).ToArray(),
                path.Closed,
                style,
                isLevelOfDetailEnabled);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private void DrawBlockReferenceSelection(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadBlockReference reference,
        CadHandleStyle selectionStyle,
        CadRenderOptions options,
        HashSet<BlockId> visitedBlocks)
    {
        if (!visitedBlocks.Add(reference.DefinitionBlockId) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null)
        {
            return;
        }

        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
                                (float)-definition.BasePoint.X,
                                (float)-definition.BasePoint.Y) *
                            Matrix3x2.CreateScale((float)reference.ScaleX, (float)reference.ScaleY) *
                            Matrix3x2.CreateRotation((float)reference.RotationRadians) *
                            Matrix3x2.CreateTranslation(
                                (float)reference.Position.X,
                                (float)reference.Position.Y) *
                            previousTransform;
        try
        {
            foreach (var child in entityOrderCache.GetOrderedEntities(
                         document,
                         reference.DefinitionBlockId))
            {
                if (child.IsErased ||
                    !child.IsVisible ||
                    !document.TryGetLayer(child.LayerId, out var layer) ||
                    layer is not { IsVisible: true, IsFrozen: false })
                {
                    continue;
                }

                DrawSelectionEntity(
                    context,
                    document,
                    viewport,
                    child,
                    CadVectorD.Zero,
                    selectionStyle,
                    dirtyWorldBounds: null,
                    options,
                    visitedBlocks: visitedBlocks);
            }
        }
        finally
        {
            context.Transform = previousTransform;
            visitedBlocks.Remove(reference.DefinitionBlockId);
        }
    }

    private bool TryDrawCachedGeometry(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadVectorD offset,
        CadTransientStyle style,
        CadRenderOptions options)
    {
        if (resources?.Geometry is null)
            return false;

        if (options.IsLevelOfDetailEnabled)
            resourceCache.EnsureLevelOfDetailGeometries(entity, resources);
        var geometry = Direct2DEntityLevelOfDetail.ResolveGeometry(
            entity,
            resources,
            context.Transform,
            options);
        if (geometry is null)
            return false;

        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var geometrySimplified = !ReferenceEquals(geometry, resources.Geometry);
        var useLevelOfDetailStrokeStyle = geometrySimplified ||
                                          Direct2DEntityLevelOfDetail.ShouldSimplifyStrokeStyle(
                                              entity,
                                              context.Transform,
                                              options);
        var strokeStyle = useLevelOfDetailStrokeStyle
            ? styleResources.GetLevelOfDetailStrokeStyle(
                resourceCache.Factory,
                entity.StrokeStyle)
            : styleResources.GetStrokeStyle(resourceCache.Factory, style);
        var strokeStyleKey = useLevelOfDetailStrokeStyle
            ? Direct2DStrokeRealizationStyleKey.ForLevelOfDetail(entity.StrokeStyle)
            : Direct2DStrokeRealizationStyleKey.ForTransient(style.LinePattern);
        var strokeWidth = styleResources.ResolveStrokeWidth(style, viewport);
        var strokeWidthChangesWithScale = style.KeepStrokeWidthScreenConstant ||
                                          Math.Abs(strokeWidth - style.StrokeWidth) >
                                          Math.Max(1e-6, Math.Abs(style.StrokeWidth) * 1e-5);
        if (offset == CadVectorD.Zero)
        {
            DrawCachedFill(context, entity, resources, geometry, entity.Bounds, style, viewport, options);
            if (!resourceCache.TryDrawStrokedGeometry(
                    context,
                    entity,
                    resources,
                    geometry,
                    brush,
                    strokeWidth,
                    strokeStyle,
                    strokeStyleKey,
                    strokeWidthChangesWithScale))
            {
                context.DrawGeometry(geometry, brush, strokeWidth, strokeStyle);
            }
            return true;
        }

        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)offset.X,
            (float)offset.Y) * previousTransform;
        try
        {
            DrawCachedFill(context, entity, resources, geometry, entity.Bounds, style, viewport, options);
            if (!resourceCache.TryDrawStrokedGeometry(
                    context,
                    entity,
                    resources,
                    geometry,
                    brush,
                    strokeWidth,
                    strokeStyle,
                    strokeStyleKey,
                    strokeWidthChangesWithScale))
            {
                context.DrawGeometry(geometry, brush, strokeWidth, strokeStyle);
            }
        }
        finally
        {
            context.Transform = previousTransform;
        }

        return true;
    }

    private void DrawCachedFill(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        ID2D1Geometry geometry,
        CadRectD bounds,
        CadTransientStyle style,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (style.FillColor is { IsTransparent: false } fillColor)
        {
            var fillBrush = styleResources.GetBrush(context, fillColor);
            if (!resourceCache.TryDrawFilledGeometry(
                    context,
                    entity,
                    resources,
                    geometry,
                    fillBrush))
            {
                context.FillGeometry(geometry, fillBrush);
            }
        }

        if (style.HatchFill is not { ForegroundColor.IsTransparent: false } hatchFill || bounds.IsEmpty)
            return;

        var hatchBrush = styleResources.GetBrush(context, hatchFill.ForegroundColor);
        Direct2DHatchRenderer.Draw(
            context,
            geometry,
            bounds,
            hatchFill,
            hatchBrush,
            viewport,
            options.IsLevelOfDetailEnabled,
            options.TransformScaleMultiplier,
            resourceCache.HatchTiles,
            statistics);
    }

    private void DrawImageFrame(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadImage image,
        CadVectorD offset,
        CadTransientStyle style)
    {
        var bounds = image.FrameBounds.Translate(offset);
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateRotation(
            (float)image.RotationRadians,
            new Vector2((float)bounds.Center.X, (float)bounds.Center.Y)) * previousTransform;
        try
        {
            transientRenderer.DrawRectangle(context, viewport, bounds, style);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private static bool IsGripVisible(CadViewport viewport, CadGripHandle grip)
    {
        var screen = viewport.WorldToScreen(grip.Position);
        var margin = Math.Max(grip.Style.Size, grip.Style.StrokeWidth) + 8.0;
        return screen.X >= -margin &&
               screen.Y >= -margin &&
               screen.X <= viewport.ViewWidth + margin &&
               screen.Y <= viewport.ViewHeight + margin;
    }

    private static bool IntersectsRenderBounds(
        CadEntity entity,
        CadVectorD offset,
        CadViewport viewport,
        CadRectD? dirtyWorldBounds,
        CadHandleStyle style)
    {
        return IntersectsRenderBounds(
            entity.Bounds,
            offset,
            viewport,
            dirtyWorldBounds,
            style);
    }

    private static bool IntersectsRenderBounds(
        CadRectD bounds,
        CadVectorD offset,
        CadViewport viewport,
        CadRectD? dirtyWorldBounds,
        CadHandleStyle style)
    {
        var entityBounds = bounds.Translate(offset);
        if (entityBounds.IsEmpty)
            return true;

        if (dirtyWorldBounds is not { IsEmpty: false } renderBounds)
            return true;

        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var strokePadding = style.KeepSizeScreenConstant
            ? (Math.Max(0.0, style.StrokeWidth) * 5.0 + 8.0) / zoom
            : Math.Max(0.0, style.StrokeWidth) * 5.0 + 8.0 / zoom;
        var padding = Math.Max(32.0 / zoom, strokePadding);
        return entityBounds.Intersects(renderBounds.Inflate(padding));
    }

    internal static double ResolveSelectionRenderPadding(
        CadHandleScene scene,
        double zoom,
        double minimumPaddingPixels = 64.0)
    {
        ArgumentNullException.ThrowIfNull(scene);

        zoom = Math.Max(zoom, double.Epsilon);
        var screenConstantPadding =
            (scene.MaximumScreenConstantSelectionStrokeWidth * 5.0 + 8.0) / zoom;
        var worldPadding =
            scene.MaximumWorldSelectionStrokeWidth * 5.0 + 8.0 / zoom;
        return Math.Max(
            Math.Max(0.0, minimumPaddingPixels) / zoom,
            Math.Max(screenConstantPadding, worldPadding));
    }

    private static CadTransientStyle ToTransientStyle(
        CadHandleStyle style,
        Direct2DResourceCache.EntityResourceBucket? resources = null)
    {
        CadColor? fillColor = resources?.FillBrush is not null
            ? WithMaximumAlpha(style.StrokeColor, SelectedSolidFillMaximumAlpha)
            : null;
        CadTransientHatchFill? hatchFill = resources is
        {
            HatchRenderData: { } hatch,
            HatchBrush: not null
        }
            ? hatch with { ForegroundColor = style.StrokeColor }
            : null;

        return new CadTransientStyle(
            style.StrokeColor,
            style.StrokeWidth,
            CadTransientLinePattern.Solid,
            fillColor,
            style.KeepSizeScreenConstant,
            HatchFill: hatchFill);
    }

    private static CadColor WithMaximumAlpha(CadColor color, byte maximumAlpha)
    {
        return CadColor.FromArgb(
            Math.Min(color.A, maximumAlpha),
            color.R,
            color.G,
            color.B);
    }

}
